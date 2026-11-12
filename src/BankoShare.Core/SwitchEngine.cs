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
}

/// <summary>
/// Decides, on the controlling machine, which device owns the pointer. Pure logic without OS calls;
/// the caller serialises all calls.
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
/// </summary>
public sealed class SwitchEngine(DeviceInfo local, ScreenLayout layout)
{
    public const int EscapeDoubleTapMs = 400;
    private const int VkEscape = 0x1B;
    private readonly Dictionary<string, DeviceInfo> peers = new(StringComparer.Ordinal);
    private readonly HashSet<(int Scan, int Extended)> localKeys = [];
    private PointI exitPoint;
    private uint lastEscape;
    private bool escapeArmed, escapeHeld;

    public DeviceInfo Local { get; private set; } = local;
    public ScreenLayout Layout { get; set; } = layout;
    public bool Enabled { get; set; } = true;
    public string? ActivePeer { get; private set; }
    public PointI RemotePosition { get; private set; }
    public PointI Anchor { get; private set; }
    public IReadOnlyCollection<DeviceInfo> Peers => peers.Values;

    public void SetLocal(DeviceInfo device, List<EngineEffect> fx)
    {
        // The parking anchor and the return point belong to the old monitor set.
        ForceLocal(fx);
        Local = device;
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

    public HookResult OnMouseMove(PointI pt, PointI cursor, bool localButtonsHeld, List<EngineEffect> fx)
    {
        if (ActivePeer != null)
        {
            var delta = pt - Anchor;
            if (delta != default) MoveRemote(delta, fx);
            return HookResult.Block;
        }
        if (!Enabled || localButtonsHeld || Local.MonitorAt(pt) != null) return HookResult.Pass;
        var from = Local.MonitorAt(cursor) ?? Local.NearestMonitor(pt);
        if (FindCrossing(Local, from, pt) is not { } hit) return HookResult.Pass;
        exitPoint = from.Clamp(pt);
        Anchor = Local.PrimaryBounds.Center;
        ActivePeer = hit.Device.Id;
        RemotePosition = hit.Point;
        escapeArmed = escapeHeld = false;
        fx.Add(new EngineEffect.HideLocalCursor(true, Anchor));
        fx.Add(new EngineEffect.WarpLocal(Anchor));
        fx.Add(new EngineEffect.Enter(ActivePeer, RemotePosition));
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

    /// <summary>Brings the pointer home to where it left, e.g. after a reclaim, lock, panic key or error.</summary>
    public void ForceLocal(List<EngineEffect> fx)
    {
        if (ActivePeer != null) ReturnLocal(exitPoint, fx, sendLeave: true);
    }

    private void MoveRemote(PointI delta, List<EngineEffect> fx)
    {
        if (!peers.TryGetValue(ActivePeer!, out var peer)) { ReturnLocal(exitPoint, fx, sendLeave: false); return; }
        var next = RemotePosition + delta;
        if (peer.MonitorAt(next) != null) { SetRemote(next, fx); return; }
        var from = peer.MonitorAt(RemotePosition) ?? peer.NearestMonitor(RemotePosition);
        if (FindCrossing(peer, from, next) is { } hit)
        {
            if (hit.Device.Id == Local.Id) { ReturnLocal(hit.Point, fx, sendLeave: true); return; }
            fx.Add(new EngineEffect.Leave(ActivePeer!));
            ActivePeer = hit.Device.Id; RemotePosition = hit.Point;
            fx.Add(new EngineEffect.Enter(ActivePeer, RemotePosition));
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
        if (sendLeave) fx.Add(new EngineEffect.Leave(peer));
        fx.Add(new EngineEffect.WarpLocal(Local.MonitorAt(point) != null ? point : Local.NearestMonitor(point).Clamp(point)));
        fx.Add(new EngineEffect.HideLocalCursor(false, Anchor));
    }

    /// <summary>
    /// Looks past the edge of <paramref name="from"/> in the shared layout. The dominant overflow axis is
    /// tried first, so a diagonal push into a corner picks the edge the user pushed hardest against.
    /// </summary>
    private (DeviceInfo Device, PointI Point)? FindCrossing(DeviceInfo source, RectI from, PointI p)
    {
        if (!Layout.Offsets.ContainsKey(source.Id)) return null;
        var clamped = from.Clamp(p);
        int overX = Math.Abs(p.X - clamped.X), overY = Math.Abs(p.Y - clamped.Y);
        List<PointI> candidates = [];
        if (overX >= overY) { if (overX > 0) candidates.Add(new(p.X, clamped.Y)); if (overY > 0) candidates.Add(new(clamped.X, p.Y)); }
        else { candidates.Add(new(clamped.X, p.Y)); if (overX > 0) candidates.Add(new(p.X, clamped.Y)); }
        var targets = peers.Values.Append(Local).Where(d => d.Id != source.Id).ToList();
        var origin = Layout.OffsetOf(source.Id);
        foreach (var c in candidates)
        {
            var layoutPoint = c + origin;
            if (Layout.HitTest(layoutPoint, targets) is { } hit)
                return (hit.Device, hit.Monitor.Clamp(layoutPoint - Layout.OffsetOf(hit.Device.Id)));
        }
        return null;
    }
}
