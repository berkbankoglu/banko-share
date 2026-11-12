namespace BankoShare.Core;

public enum HookResult { Pass, Block }

/// <summary>Side effects requested by the engine. The caller performs them in order.</summary>
public abstract record EngineEffect
{
    public sealed record WarpLocal(PointI Point) : EngineEffect;
    public sealed record HideLocalCursor(bool Hidden, PointI Anchor) : EngineEffect;
    public sealed record Enter(string Peer, PointI Position) : EngineEffect;
    public sealed record Move(string Peer, PointI Position) : EngineEffect;
    public sealed record Leave(string Peer) : EngineEffect;
    public sealed record Forward(string Peer, InputEvent Input) : EngineEffect;
    /// <summary>A hotkey fired. Index is the zero-based monitor for <see cref="HotkeyAction.JumpToMonitor"/>.</summary>
    public sealed record Command(HotkeyAction Action, int Index = 0) : EngineEffect;
    /// <summary>A hotkey was swallowed while the local left Alt is down; Windows would open the menu bar when Alt is released.</summary>
    public sealed record MaskMenu() : EngineEffect;
    /// <summary>A new push against an edge was refused.</summary>
    public sealed record Blocked(BlockReason Reason, bool DoubleTapAllowed) : EngineEffect;
}

/// <summary>
/// Decides, on the controlling machine, which device owns the pointer. Pure logic; OS conditions come
/// through <see cref="ISwitchEnvironment"/>. The caller serialises all calls.
/// <para>
/// Low-level hook coordinates are not clipped to the desktop: pushing against an outer edge reports a
/// point beyond it. A point outside every local monitor is therefore a push past an edge, and the
/// layout decides whether a neighbouring device is there.
/// </para>
/// <para>
/// While a peer owns the pointer, every local event is blocked, so the local cursor stays parked
/// at <see cref="Anchor"/> and each hook point minus the anchor is the user's accelerated movement.
/// The peer receives absolute positions, so its own pointer acceleration is never applied twice.
/// </para>
/// <para>
/// A push is one contact with an edge: it starts at the first point past the edge and ends when the
/// pointer comes back <see cref="TapRetreat"/> pixels. Foreground checks run once per push; two pushes
/// within <see cref="EdgeDoubleTapMs"/> make a double tap.
/// </para>
/// </summary>
public sealed class SwitchEngine(DeviceInfo local, ScreenLayout layout)
{
    public const int EscapeDoubleTapMs = 400;
    public const int EdgeDoubleTapMs = 500;
    public const int TapRetreat = 6;
    /// <summary>Corner zone in pixels at 96 DPI, scaled with the monitor's DPI.</summary>
    public const int CornerSize = 40;
    private const int VkEscape = 0x1B;

    private enum Side { Left, Right, Top, Bottom }
    private readonly record struct Crossing(DeviceInfo Device, PointI Point, Side Side, PointI EdgePoint);
    private readonly record struct Push(string Device, Side Side, int Line, BlockReason Foreground, bool Tapped);
    private readonly record struct Tap(string Device, Side Side, uint Time);

    private readonly Dictionary<string, DeviceInfo> peers = new(StringComparer.Ordinal);
    private readonly HashSet<(int Scan, int Extended)> localKeys = [];
    private readonly HashSet<int> heldVks = [];
    private readonly HashSet<int> swallowed = [];
    private PointI exitPoint;
    private PointI? lastLocal;
    private Push? push;
    private Tap? lastTap;
    private uint lastEscape;
    private bool escapeArmed, escapeHeld;

    public DeviceInfo Local { get; private set; } = local;
    public ScreenLayout Layout { get; set; } = layout;
    public SwitchOptions Options { get; set; } = new();
    public ISwitchEnvironment? Environment { get; set; }
    /// <summary>Set while the settings page records a new hotkey, so the old one does not fire.</summary>
    public bool HotkeysSuspended { get; set; }
    public string? ActivePeer { get; private set; }
    public PointI RemotePosition { get; private set; }
    public PointI Anchor { get; private set; }
    public IReadOnlyCollection<DeviceInfo> Peers => peers.Values;

    public void SetLocal(DeviceInfo device, List<EngineEffect> fx)
    {
        // The parking anchor and the return point belong to the old monitor set.
        ForceLocal(fx);
        Local = device;
        lastLocal = null; push = null; lastTap = null;
    }

    public void SetPeer(DeviceInfo peer, List<EngineEffect> fx)
    {
        peers[peer.Id] = peer;
        if (ActivePeer == peer.Id && peer.MonitorAt(RemotePosition) == null)
            SetRemote(peer.NearestMonitor(RemotePosition).Clamp(RemotePosition), fx);
    }

    public void RemovePeer(string id, List<EngineEffect> fx)
    {
        if (!peers.Remove(id) || ActivePeer != id) return;
        ReturnLocal(exitPoint, fx, sendLeave: false);
    }

    public HookResult OnMouseMove(PointI pt, PointI cursor, bool localButtonsHeld, List<EngineEffect> fx, uint timeMs = 0)
    {
        if (ActivePeer != null)
        {
            var delta = pt - Anchor;
            if (delta != default) MoveRemote(delta, timeMs, fx);
            return HookResult.Block;
        }
        if (Local.MonitorAt(pt) != null) { lastLocal = pt; Retreat(Local.Id, pt); return HookResult.Pass; }
        if (!Options.SwitchingEnabled || localButtonsHeld) return HookResult.Pass;
        var from = Local.MonitorAt(cursor) ?? Local.NearestMonitor(pt);
        if (FindCrossing(Local, from, pt) is not { } hit || !MayCross(Local, from, hit, timeMs, fx)) return HookResult.Pass;
        if (hit.Device.Id == Local.Id)
        {
            // Wrapped around to the far side of this machine.
            ResetPush(); lastLocal = hit.Point;
            fx.Add(new EngineEffect.WarpLocal(hit.Point));
            return HookResult.Block;
        }
        BeginRemote(from.Clamp(pt), hit.Device.Id, hit.Point, fx);
        return HookResult.Block;
    }

    public HookResult OnButton(int button, bool down, List<EngineEffect> fx)
    {
        if (ActivePeer == null) return HookResult.Pass;
        fx.Add(new EngineEffect.Forward(ActivePeer, new(EventKind.Button, button, down ? 1 : 0)));
        return HookResult.Block;
    }

    public HookResult OnWheel(int delta, bool horizontal, List<EngineEffect> fx)
    {
        if (ActivePeer == null) return HookResult.Pass;
        fx.Add(new EngineEffect.Forward(ActivePeer, new(EventKind.Wheel, Math.Clamp(delta, -32767, 32767), horizontal ? 1 : 0)));
        return HookResult.Block;
    }

    public HookResult OnKey(int vk, int scan, bool extended, bool down, uint timeMs, List<EngineEffect> fx)
    {
        var repeat = down && heldVks.Contains(vk);
        TrackKey(vk, down);
        // Both halves of a hotkey stay with us, wherever the pointer went in between.
        if (swallowed.Contains(vk)) { if (!down) swallowed.Remove(vk); return HookResult.Block; }
        if (down && !repeat && !HotkeysSuspended && MatchHotkey(vk) is { } hotkey)
        {
            swallowed.Add(vk);
            if (ActivePeer == null && hotkey.Mods.HasFlag(KeyMods.Alt)) fx.Add(new EngineEffect.MaskMenu());
            fx.Add(hotkey.Command);
            return HookResult.Block;
        }
        var key = (scan, extended ? 1 : 0);
        if (ActivePeer == null)
        {
            if (down) localKeys.Add(key); else localKeys.Remove(key);
            return HookResult.Pass;
        }
        // A key held on this machine before the pointer left keeps repeating and releasing here.
        if (localKeys.Contains(key)) { if (!down) localKeys.Remove(key); return HookResult.Pass; }
        if (vk == VkEscape)
        {
            if (down && !escapeHeld)
            {
                if (escapeArmed && unchecked(timeMs - lastEscape) <= EscapeDoubleTapMs) { ForceLocal(fx); return HookResult.Block; }
                escapeArmed = true; lastEscape = timeMs;
            }
            escapeHeld = down;
        }
        else if (down) escapeArmed = false;
        if (scan is >= 1 and <= 255)
            fx.Add(new EngineEffect.Forward(ActivePeer, new(EventKind.Key, scan, down ? 1 : 0, extended ? 1 : 0)));
        return HookResult.Block;
    }

    /// <summary>Keeps modifier tracking right while keys bypass <see cref="OnKey"/> (a peer is controlling us).</summary>
    public void TrackKey(int vk, bool down)
    {
        if (vk is <= 0 or >= 0xFF) return;
        if (down) heldVks.Add(vk); else heldVks.Remove(vk);
    }

    /// <summary>Brings the pointer home to where it left, e.g. after a reclaim, lock, panic key or error.</summary>
    public void ForceLocal(List<EngineEffect> fx)
    {
        if (ActivePeer != null) ReturnLocal(exitPoint, fx, sendLeave: true);
    }

    /// <summary>Moves the pointer to a point on any connected device. Returns false if that device is not connected.</summary>
    public bool JumpTo(string deviceId, PointI point, List<EngineEffect> fx)
    {
        if (deviceId == Local.Id)
        {
            var home = Local.MonitorAt(point) != null ? point : Local.NearestMonitor(point).Clamp(point);
            if (ActivePeer != null) ReturnLocal(home, fx, sendLeave: true);
            else { lastLocal = home; fx.Add(new EngineEffect.WarpLocal(home)); }
            return true;
        }
        if (!peers.TryGetValue(deviceId, out var peer)) return false;
        var target = peer.MonitorAt(point) != null ? point : peer.NearestMonitor(point).Clamp(point);
        if (ActivePeer == deviceId) { SetRemote(target, fx); return true; }
        if (ActivePeer != null) { SwitchPeer(deviceId, target, fx); return true; }
        BeginRemote(lastLocal ?? Local.PrimaryBounds.Center, deviceId, target, fx);
        return true;
    }

    /// <summary>Cycles through connected devices in reading order on the desk.</summary>
    public void JumpToNext(List<EngineEffect> fx)
    {
        var ring = Online().OrderBy(d => RectI.Union(Layout.RectsOf(d)).Top).ThenBy(d => RectI.Union(Layout.RectsOf(d)).Left)
            .ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (ring.Count < 2) return;
        var next = ring[(ring.FindIndex(d => d.Id == (ActivePeer ?? Local.Id)) + 1) % ring.Count];
        if (next.Id == Local.Id) ForceLocal(fx);
        else JumpTo(next.Id, next.PrimaryBounds.Center, fx);
    }

    // ---- Pointer ownership ----

    private void BeginRemote(PointI exit, string peerId, PointI position, List<EngineEffect> fx)
    {
        exitPoint = exit;
        Anchor = Local.PrimaryBounds.Center;
        ActivePeer = peerId;
        RemotePosition = position;
        escapeArmed = escapeHeld = false;
        ResetPush();
        // Keys released where our hook could not see it (secure desktop) must not count as held while remote.
        if (Environment is { } env) heldVks.RemoveWhere(vk => !env.IsKeyDown(vk));
        fx.Add(new EngineEffect.HideLocalCursor(true, Anchor));
        fx.Add(new EngineEffect.WarpLocal(Anchor));
        fx.Add(new EngineEffect.Enter(peerId, position));
    }

    private void SwitchPeer(string peerId, PointI position, List<EngineEffect> fx)
    {
        fx.Add(new EngineEffect.Leave(ActivePeer!));
        ActivePeer = peerId; RemotePosition = position;
        ResetPush();
        fx.Add(new EngineEffect.Enter(peerId, position));
    }

    private void MoveRemote(PointI delta, uint timeMs, List<EngineEffect> fx)
    {
        if (!peers.TryGetValue(ActivePeer!, out var peer)) { ReturnLocal(exitPoint, fx, sendLeave: false); return; }
        var next = RemotePosition + delta;
        if (peer.MonitorAt(next) != null) { Retreat(peer.Id, next); SetRemote(next, fx); return; }
        var from = peer.MonitorAt(RemotePosition) ?? peer.NearestMonitor(RemotePosition);
        if (FindCrossing(peer, from, next) is { } hit && MayCross(peer, from, hit, timeMs, fx))
        {
            if (hit.Device.Id == Local.Id) ReturnLocal(hit.Point, fx, sendLeave: true);
            else if (hit.Device.Id == peer.Id) { ResetPush(); SetRemote(hit.Point, fx); }
            else SwitchPeer(hit.Device.Id, hit.Point, fx);
            return;
        }
        SetRemote(peer.NearestMonitor(next).Clamp(next), fx);
    }

    private void SetRemote(PointI p, List<EngineEffect> fx)
    {
        if (p == RemotePosition) return;
        RemotePosition = p;
        fx.Add(new EngineEffect.Move(ActivePeer!, p));
    }

    private void ReturnLocal(PointI point, List<EngineEffect> fx, bool sendLeave)
    {
        var peer = ActivePeer!;
        ActivePeer = null;
        ResetPush();
        if (sendLeave) fx.Add(new EngineEffect.Leave(peer));
        var home = Local.MonitorAt(point) != null ? point : Local.NearestMonitor(point).Clamp(point);
        lastLocal = home;
        fx.Add(new EngineEffect.WarpLocal(home));
        fx.Add(new EngineEffect.HideLocalCursor(false, Anchor));
    }

    private IEnumerable<DeviceInfo> Online() => peers.Values.Append(Local).Where(d => Layout.Offsets.ContainsKey(d.Id));

    // ---- Blocking conditions and double tap ----

    private void ResetPush() { push = null; lastTap = null; }

    private void Retreat(string device, PointI p)
    {
        if (push is not { } e || e.Device != device) return;
        var away = e.Side switch { Side.Left => p.X - e.Line, Side.Right => e.Line - p.X, Side.Top => p.Y - e.Line, _ => e.Line - p.Y };
        if (away >= TapRetreat) push = null;
    }

    private bool MayCross(DeviceInfo source, RectI from, Crossing hit, uint timeMs, List<EngineEffect> fx)
    {
        var line = hit.Side switch { Side.Left => from.Left, Side.Right => from.Right - 1, Side.Top => from.Top, _ => from.Bottom - 1 };
        var fresh = push is not { } p || p.Device != source.Id || p.Side != hit.Side || p.Line != line;
        if (fresh)
        {
            var o = Options;
            var foreground = source.Id == Local.Id && Environment != null && (o.BlockFullscreen || o.BlockMaximized || o.BlockedPrograms.Length > 0)
                ? Environment.CheckForeground(from, o) : BlockReason.None;
            var tapped = lastTap is { } t && t.Device == source.Id && t.Side == hit.Side && unchecked(timeMs - t.Time) <= EdgeDoubleTapMs;
            push = new Push(source.Id, hit.Side, line, foreground, tapped);
            lastTap = new Tap(source.Id, hit.Side, timeMs);
        }
        var current = push!.Value;
        // Corners and the switch key are re-checked on every point: sliding out of a corner or pressing the key switches mid-push.
        var reason = current.Foreground != BlockReason.None ? current.Foreground
            : Options.BlockCorners && InCorner(source, from, hit) ? BlockReason.Corner
            : !SwitchKeyHeld() ? BlockReason.Key
            : Options.DoubleTap == DoubleTapMode.Always ? BlockReason.DoubleTap
            : BlockReason.None;
        if (reason == BlockReason.None || Options.DoubleTap != DoubleTapMode.Off && current.Tapped) return true;
        if (fresh) fx.Add(new EngineEffect.Blocked(reason, Options.DoubleTap != DoubleTapMode.Off));
        return false;
    }

    private static bool InCorner(DeviceInfo source, RectI from, Crossing hit)
    {
        var size = CornerSize * (source.Monitors.FirstOrDefault(m => m.Bounds == from)?.Dpi ?? 96) / 96;
        var e = hit.EdgePoint;
        return hit.Side is Side.Top or Side.Bottom
            ? e.X - from.Left < size || from.Right - 1 - e.X < size
            : e.Y - from.Top < size || from.Bottom - 1 - e.Y < size;
    }

    private bool SwitchKeyHeld()
    {
        if (Options.RequiredKey == SwitchKey.None) return true;
        var mods = CurrentMods();
        return Options.RequiredKey switch
        {
            SwitchKey.Shift => mods.HasFlag(KeyMods.Shift),
            SwitchKey.Ctrl => mods.HasFlag(KeyMods.Ctrl),
            _ => mods.HasFlag(KeyMods.Alt)
        };
    }

    /// <summary>
    /// While the pointer is here Windows knows the real key state. While it is remote our hook blocks every
    /// key, so Windows never sees them and our own tracking is the only source.
    /// </summary>
    private KeyMods CurrentMods() => ActivePeer == null && Environment is { } env
        ? VirtualKeys.ModsFrom(env.IsKeyDown)
        : VirtualKeys.ModsFrom(heldVks.Contains);

    private (EngineEffect.Command Command, KeyMods Mods)? MatchHotkey(int vk)
    {
        var o = Options;
        if (VirtualKeys.IsModifier(vk) || o.ToggleHotkey == null && o.NextDeviceHotkey == null && o.HomeHotkey == null && o.JumpMods == KeyMods.None)
            return null;
        var mods = CurrentMods();
        if (mods == KeyMods.None) return null;
        bool Is(Hotkey? h) => h != null && h.Vk == vk && h.Mods == mods;
        if (Is(o.ToggleHotkey)) return (new(HotkeyAction.ToggleSwitching), mods);
        if (Is(o.NextDeviceHotkey)) return (new(HotkeyAction.NextDevice), mods);
        if (Is(o.HomeHotkey)) return (new(HotkeyAction.ReturnHome), mods);
        if (o.JumpMods != KeyMods.None && mods == o.JumpMods && VirtualKeys.DigitIndex(vk) is >= 0 and var index)
            return (new(HotkeyAction.JumpToMonitor, index), mods);
        return null;
    }

    // ---- Geometry ----

    /// <summary>
    /// Looks past the edge of <paramref name="from"/> in the shared layout. The dominant overflow axis is
    /// tried first, so a diagonal push into a corner picks the edge the user pushed hardest against.
    /// </summary>
    private Crossing? FindCrossing(DeviceInfo source, RectI from, PointI p)
    {
        if (!Layout.Offsets.ContainsKey(source.Id)) return null;
        var clamped = from.Clamp(p);
        int dx = p.X - clamped.X, dy = p.Y - clamped.Y;
        List<Side> sides = [];
        void Add(int d, Side negative, Side positive) { if (d != 0) sides.Add(d < 0 ? negative : positive); }
        if (Math.Abs(dx) >= Math.Abs(dy)) { Add(dx, Side.Left, Side.Right); Add(dy, Side.Top, Side.Bottom); }
        else { Add(dy, Side.Top, Side.Bottom); Add(dx, Side.Left, Side.Right); }
        var origin = Layout.OffsetOf(source.Id);
        var targets = Online().Where(d => d.Id != source.Id).ToList();
        foreach (var side in sides)
        {
            var across = side is Side.Left or Side.Right;
            if (Options.EqualizeEdges && Equalized(from.Offset(origin), clamped + origin, side, Math.Abs(across ? dx : dy), targets) is { } eq)
                return new Crossing(eq.Device, eq.Point, side, clamped);
            var beyond = (across ? new PointI(p.X, clamped.Y) : new PointI(clamped.X, p.Y)) + origin;
            if (Layout.HitTest(beyond, targets) is { } hit)
                return new Crossing(hit.Device, hit.Monitor.Clamp(beyond - Layout.OffsetOf(hit.Device.Id)), side, clamped);
        }
        if (Options.Wrap)
            foreach (var side in sides)
                if (Wrapped(clamped + origin, side) is { } w) return new Crossing(w.Device, w.Point, side, clamped);
        return null;
    }

    private IEnumerable<(DeviceInfo Device, RectI Rect)> PlacedMonitors(IEnumerable<DeviceInfo> devices) =>
        devices.SelectMany(d => d.Monitors.Select(m => (d, m.Bounds.Offset(Layout.OffsetOf(d.Id)))));

    /// <summary>
    /// Maps the whole edge of source monitor <paramref name="s"/> onto the whole span of the monitors that
    /// touch it on <paramref name="side"/>, so the pointer crosses anywhere along the edge. Inputs are layout
    /// coordinates; the result is in the target device's own coordinates.
    /// </summary>
    private (DeviceInfo Device, PointI Point)? Equalized(RectI s, PointI cp, Side side, int overflow, List<DeviceInfo> targets)
    {
        var horizontalEdge = side is Side.Top or Side.Bottom;
        int Lo(RectI r) => horizontalEdge ? r.Left : r.Top;
        int Hi(RectI r) => horizontalEdge ? r.Right : r.Bottom;
        var facing = PlacedMonitors(targets).Where(t => side switch
        {
            Side.Top => t.Rect.Bottom == s.Top,
            Side.Bottom => t.Rect.Top == s.Bottom,
            Side.Left => t.Rect.Right == s.Left,
            _ => t.Rect.Left == s.Right
        } && Hi(t.Rect) > Lo(s) && Lo(t.Rect) < Hi(s)).ToList();
        if (facing.Count == 0) return null;
        int lo = facing.Min(t => Lo(t.Rect)), hi = facing.Max(t => Hi(t.Rect));
        long along = (horizontalEdge ? cp.X : cp.Y) - Lo(s);
        // Pixel centre to pixel centre, so a round trip lands where it started.
        var mapped = lo + (int)((2 * along + 1) * (hi - lo) / (2L * (Hi(s) - Lo(s))));
        var (device, r) = facing.MinBy(t => mapped < Lo(t.Rect) ? Lo(t.Rect) - mapped : mapped >= Hi(t.Rect) ? mapped - Hi(t.Rect) + 1 : 0);
        var point = side switch
        {
            Side.Top => new PointI(mapped, r.Bottom - overflow),
            Side.Bottom => new PointI(mapped, r.Top + overflow - 1),
            Side.Left => new PointI(r.Right - overflow, mapped),
            _ => new PointI(r.Left + overflow - 1, mapped)
        };
        return (device, r.Clamp(point) - Layout.OffsetOf(device.Id));
    }

    /// <summary>Enters from the far end of the row or column that <paramref name="cp"/> (layout coordinates) is on.</summary>
    private (DeviceInfo Device, PointI Point)? Wrapped(PointI cp, Side side)
    {
        var across = side is Side.Left or Side.Right;
        var line = PlacedMonitors(Online())
            .Where(t => across ? t.Rect.Top <= cp.Y && cp.Y < t.Rect.Bottom : t.Rect.Left <= cp.X && cp.X < t.Rect.Right).ToList();
        if (line.Count == 0) return null;
        var (device, r) = side switch
        {
            Side.Left => line.MaxBy(t => t.Rect.Right),
            Side.Right => line.MinBy(t => t.Rect.Left),
            Side.Top => line.MaxBy(t => t.Rect.Bottom),
            _ => line.MinBy(t => t.Rect.Top)
        };
        var point = side switch
        {
            Side.Left => new PointI(r.Right - 1, cp.Y),
            Side.Right => new PointI(r.Left, cp.Y),
            Side.Top => new PointI(cp.X, r.Bottom - 1),
            _ => new PointI(cp.X, r.Top)
        };
        return (device, point - Layout.OffsetOf(device.Id));
    }
}
