using System.Net;

namespace BankoShare.Core;

/// <summary>Operating-system input. Implementations must be safe to call from any thread.</summary>
public interface IInputPlatform
{
    void WarpCursor(PointI point);
    /// <summary>Hides the local pointer while it is parked at <paramref name="anchor"/>.</summary>
    void SetCursorHidden(bool hidden, PointI anchor);
    /// <summary>Applies input on this machine. Move carries an absolute desktop position. Throws on failure.</summary>
    void Inject(InputEvent input);
    /// <summary>Sends an unassigned key so that releasing a held Alt does not open the focused window's menu bar.</summary>
    void MaskMenuKey();
}

public sealed record PeerStatus(string Id, string Name, bool Connected, string? Address, DeviceInfo? Device);
public sealed record ShareSnapshot(DeviceInfo Local, IReadOnlyList<PeerStatus> Peers, IReadOnlyList<DiscoveredDevice> Discovered,
    ScreenLayout Layout, string? ControllingName, string? ControlledByName, SwitchOptions Options, PairingOffer? Pairing);

/// <summary>
/// Connects local hooks, the switch engine and the network. Plays both roles: controller (our pointer is
/// on a peer) and controlled (a peer's pointer is here). All state is guarded by one lock that is only held
/// for short, non-blocking work; the hook thread never waits on the network or the disk.
/// </summary>
public sealed class ShareController : IDisposable
{
    public const string LayoutFile = "layout.json";
    public const string PreferencesFile = "share.json";
    /// <summary>Physical movement (px) on a controlled machine that takes control back.</summary>
    public const int ReclaimDistance = 12;
    public static readonly TimeSpan BusyGrace = TimeSpan.FromSeconds(60);
    private const int AltScan = 0x38;

    private readonly object gate = new();
    private readonly PeerNetwork network;
    private readonly IInputPlatform platform;
    private readonly SettingsStore? store;
    private readonly BackgroundSaver saver;
    private readonly string version;
    private readonly SwitchEngine engine;
    private readonly InputState injected;
    private readonly List<EngineEffect> fx = [];
    private ScreenLayout layout;
    private string? controlledBy;
    private int reclaimTravel;
    private DateTimeOffset lastActivity = DateTimeOffset.MinValue;
    private bool stateChanged;
    private string? pendingNotice;

    public event Action? Changed;
    public event Action<string>? Notice;

    public ShareController(DeviceInfo local, PeerNetwork network, IInputPlatform platform, SettingsStore? store, string version,
        ISwitchEnvironment? environment = null)
    {
        this.network = network; this.platform = platform; this.store = store; this.version = version;
        // Settings still apply in memory when a write fails.
        saver = new BackgroundSaver(store, _ => Notice?.Invoke("Ayarlar diske kaydedilemedi; bu oturumda geçerli."));
        layout = LoadLayout();
        if (layout.PlaceMissing([local]) && layout.Revision == 0) layout.Author = local.Id;
        var options = (store?.Load(PreferencesFile, () => new SwitchOptions()) ?? new SwitchOptions()).Sanitized();
        engine = new SwitchEngine(local, layout) { Options = options, Environment = environment };
        injected = new InputState(platform.Inject);
        network.LinkUp += OnLinkUp;
        network.LinkDown += OnLinkDown;
        network.FrameReceived += OnFrame;
        network.Changed += RaiseChanged;
        network.Directory.Changed += RaiseChanged;
    }

    public DeviceInfo Local { get { lock (gate) return engine.Local; } }
    public SwitchOptions Options { get { lock (gate) return engine.Options; } }

    /// <summary>True while sharing or shortly after; updates must not restart the app then.</summary>
    public bool IsBusy
    {
        get { lock (gate) return engine.ActivePeer != null || controlledBy != null || DateTimeOffset.UtcNow - lastActivity < BusyGrace; }
    }

    // ---- Local hooks (input thread). Injected events never reach these methods. ----

    public HookResult OnMouseMove(PointI pt, PointI cursor, bool localButtonsHeld, uint timeMs = 0)
    {
        HookResult result;
        lock (gate)
        {
            if (controlledBy != null)
            {
                reclaimTravel += Math.Abs(pt.X - cursor.X) + Math.Abs(pt.Y - cursor.Y);
                if (reclaimTravel >= ReclaimDistance) Reclaim();
                result = HookResult.Pass;
            }
            else result = engine.OnMouseMove(pt, cursor, localButtonsHeld, fx, timeMs);
            Flush();
        }
        AfterHook();
        return result;
    }

    public HookResult OnMouseButton(int button, bool down)
    {
        HookResult result;
        lock (gate)
        {
            if (controlledBy != null) { if (down) Reclaim(); result = HookResult.Pass; }
            else result = engine.OnButton(button, down, fx);
            Flush();
        }
        AfterHook();
        return result;
    }

    public HookResult OnMouseWheel(int delta, bool horizontal)
    {
        lock (gate)
        {
            if (controlledBy != null) return HookResult.Pass;
            var result = engine.OnWheel(delta, horizontal, fx);
            Flush();
            return result;
        }
    }

    public HookResult OnKey(int vk, int scan, bool extended, bool down, uint timeMs)
    {
        HookResult result;
        lock (gate)
        {
            // This machine's own keyboard keeps working locally while a peer controls it.
            if (controlledBy != null) { engine.TrackKey(vk, down); result = HookResult.Pass; }
            else result = engine.OnKey(vk, scan, extended, down, timeMs, fx);
            Flush();
        }
        AfterHook();
        return result;
    }

    // ---- Commands from the UI or system events ----

    public void UpdateLocalMonitors(MonitorInfo[] monitors)
    {
        DeviceInfo local;
        lock (gate)
        {
            local = engine.Local with { Monitors = monitors };
            local.Validate();
            engine.SetLocal(local, fx);
            Flush();
        }
        network.Broadcast(Frame.Of(FrameType.Hello, new HelloMessage(local, version, network.Port)));
        RaiseChanged();
    }

    /// <summary>Pointer comes home and a peer controlling us is told to stop (screen lock, sleep, panic).</summary>
    public void ReturnHome()
    {
        lock (gate)
        {
            engine.ForceLocal(fx);
            if (controlledBy != null) Reclaim();
            Flush();
        }
        AfterHook();
    }

    public void SetSwitchingEnabled(bool enabled) => UpdateOptions(o => o with { SwitchingEnabled = enabled });

    /// <summary>Applies a change to the current options atomically, so a hotkey toggle in between is not lost. Saves to disk.</summary>
    public SwitchOptions UpdateOptions(Func<SwitchOptions, SwitchOptions> change)
    {
        SwitchOptions next;
        lock (gate)
        {
            next = change(engine.Options).Sanitized();
            ApplyOptions(next);
            Flush();
        }
        AfterHook();
        RaiseChanged();
        return next;
    }

    /// <summary>While the settings page records a hotkey, existing hotkeys must not fire.</summary>
    public void SuspendHotkeys(bool suspended) { lock (gate) engine.HotkeysSuspended = suspended; }

    /// <summary>Drops a dragged device at <paramref name="proposed"/>, snapping to neighbours. Returns false if it would overlap.</summary>
    public bool MoveDevice(string deviceId, PointI proposed, int snapThreshold)
    {
        lock (gate)
        {
            var next = layout.Clone();
            if (!next.SnapAndPlace(deviceId, proposed, KnownDevices(), snapThreshold)) return false;
            if (next.SameArrangement(layout)) return true;
            Publish(next);
        }
        RaiseChanged();
        return true;
    }

    public void ResetLayout()
    {
        lock (gate)
        {
            var next = new ScreenLayout { Revision = layout.Revision };
            next.PlaceMissing(KnownDevices());
            Publish(next);
        }
        RaiseChanged();
    }

    public ShareSnapshot Snapshot()
    {
        lock (gate)
        {
            var online = engine.Peers.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var peers = network.Directory.All.Select(r => new PeerStatus(r.Id, online.GetValueOrDefault(r.Id)?.Name ?? r.Name,
                network.IsConnected(r.Id), r.Address, online.GetValueOrDefault(r.Id) ?? r.LastKnownDevice)).ToList();
            var discovered = network.Discovered.Where(d => !network.Directory.IsPaired(d.Id)).ToList();
            string? NameOf(string? id) => id == null ? null : peers.FirstOrDefault(p => p.Id == id)?.Name ?? "Diğer bilgisayar";
            return new(engine.Local, peers, discovered, layout.Clone(), NameOf(engine.ActivePeer), NameOf(controlledBy), engine.Options, network.CurrentPairing);
        }
    }

    // ---- Network (link threads) ----

    private void OnLinkUp(PeerLink link) =>
        link.TrySend(Frame.Of(FrameType.Hello, new HelloMessage(Local, version, network.Port)));

    private void OnLinkDown(PeerLink link)
    {
        lock (gate)
        {
            engine.RemovePeer(link.PeerId, fx);
            if (controlledBy == link.PeerId) { controlledBy = null; ReleaseInjected(); }
            Flush();
        }
        AfterHook();
        RaiseChanged();
    }

    private void OnFrame(PeerLink link, Frame frame)
    {
        try { Handle(link, frame); }
        catch (InvalidDataException) { link.Close(); }
        AfterHook();
    }

    private void Handle(PeerLink link, Frame frame)
    {
        var id = link.PeerId;
        switch (frame.Type)
        {
            case FrameType.Hello:
            {
                var hello = frame.As<HelloMessage>();
                if (hello.Device == null || hello.Version == null || hello.Port is < 1 or > 65535) throw new InvalidDataException("Invalid hello.");
                hello.Device.Validate();
                // The certificate is the identity: a peer cannot claim another device's id or layout slot.
                if (hello.Device.Id != id) throw new InvalidDataException("Device id does not match certificate.");
                network.Directory.AddOrUpdate(id, hello.Device.Name, new IPEndPoint(link.Address, hello.Port).ToString(), hello.Device.Monitors);
                lock (gate)
                {
                    engine.SetPeer(hello.Device, fx);
                    if (layout.PlaceMissing(KnownDevices()))
                    {
                        if (layout.Revision == 0) layout.Author = engine.Local.Id;
                        SaveLayout();
                    }
                    link.TrySend(Frame.Of(FrameType.Layout, layout));
                    Flush();
                }
                RaiseChanged();
                break;
            }
            case FrameType.Layout:
            {
                var incoming = frame.As<ScreenLayout>();
                incoming.Validate();
                lock (gate)
                {
                    if (!incoming.IsPreferredOver(layout)) return;
                    incoming.PlaceMissing(KnownDevices());
                    layout = incoming;
                    engine.Layout = layout;
                    SaveLayout();
                }
                RaiseChanged();
                break;
            }
            case FrameType.Enter:
            {
                var p = frame.AsPoint();
                lock (gate)
                {
                    // Whoever moved last owns the pointer: being entered ends our own remote session.
                    engine.ForceLocal(fx);
                    if (controlledBy != null && controlledBy != id) ReleaseInjected();
                    controlledBy = id; reclaimTravel = 0; stateChanged = true;
                    lastActivity = DateTimeOffset.UtcNow;
                    TryInject(new InputEvent(EventKind.Move, p.X, p.Y));
                    Flush();
                }
                break;
            }
            case FrameType.Input:
            {
                var input = frame.AsInput();
                lock (gate)
                {
                    if (controlledBy != id) return;
                    if (input.Kind == EventKind.Move) reclaimTravel = 0;
                    lastActivity = DateTimeOffset.UtcNow;
                    TryInject(input);
                }
                break;
            }
            case FrameType.Leave:
                lock (gate) { if (controlledBy == id) { controlledBy = null; ReleaseInjected(); stateChanged = true; } }
                break;
            case FrameType.Reclaim:
                lock (gate) { if (engine.ActivePeer == id) { engine.ForceLocal(fx); Flush(); } }
                break;
            // Late pairing traffic carries no meaning once the link is up.
            case FrameType.LegacyPairRequest or FrameType.PairAccepted or FrameType.PairStart or FrameType.PairReply or FrameType.PairFinish:
                break;
        }
    }

    // ---- Helpers (caller holds the lock unless noted) ----

    private void Reclaim()
    {
        var peer = controlledBy!;
        controlledBy = null; stateChanged = true;
        ReleaseInjected();
        network.Send(peer, Frame.Empty(FrameType.Reclaim));
    }

    private void TryInject(InputEvent input)
    {
        try { injected.Apply(input); }
        catch (Exception) when (input.Kind == EventKind.Move) { /* e.g. secure desktop; next move retries. */ }
        catch (Exception) { Notice?.Invoke("Giriş uygulanamadı. Yönetici olarak çalışan pencereler bu sürümde desteklenmez."); }
    }

    private void ReleaseInjected()
    {
        // The controller kept the key that finished its hotkey; a lone Alt release would open the menu bar here.
        if (injected.IsKeyHeld(AltScan))
            try { platform.MaskMenuKey(); } catch (Exception) { /* Cosmetic only. */ }
        try { injected.ReleaseAll(); }
        catch (Exception) { Notice?.Invoke("Bazı tuşlar bırakılamadı; basılı kalan tuşları fiziksel klavyede bırakın."); }
    }

    /// <summary>Takes effect at once; the file is written in the background.</summary>
    private void ApplyOptions(SwitchOptions next)
    {
        var wasEnabled = engine.Options.SwitchingEnabled;
        engine.Options = next;
        if (wasEnabled && !next.SwitchingEnabled) engine.ForceLocal(fx);
        saver.Schedule(PreferencesFile, next);
    }

    private void RunCommand(EngineEffect.Command command)
    {
        switch (command.Action)
        {
            case HotkeyAction.ToggleSwitching:
            {
                var next = engine.Options with { SwitchingEnabled = !engine.Options.SwitchingEnabled };
                ApplyOptions(next);
                pendingNotice = next.SwitchingEnabled ? "Kenardan geçiş açıldı." : "Kenardan geçiş kapatıldı.";
                break;
            }
            case HotkeyAction.NextDevice:
                engine.JumpToNext(fx);
                break;
            case HotkeyAction.ReturnHome:
                engine.ForceLocal(fx);
                break;
            case HotkeyAction.JumpToMonitor:
            {
                var order = layout.ReadingOrder(KnownDevices());
                if (command.Index >= order.Count) { pendingNotice = $"Düzende {(char)('A' + command.Index)} monitörü yok."; break; }
                var (device, monitor) = order[command.Index];
                if (!engine.JumpTo(device.Id, monitor.Center, fx)) pendingNotice = $"{device.Name} bağlı değil.";
                break;
            }
        }
        stateChanged = true;
    }

    private static string BlockedText(EngineEffect.Blocked b, SwitchKey key)
    {
        var text = b.Reason switch
        {
            BlockReason.Fullscreen => "Tam ekran uygulama öndeyken geçiş yapılmaz",
            BlockReason.Maximized => "Büyütülmüş pencere öndeyken geçiş yapılmaz",
            BlockReason.Program => "Bu program öndeyken geçiş yapılmaz",
            BlockReason.Corner => "Ekran köşesinde geçiş yapılmaz",
            BlockReason.Key => $"Geçmek için {key switch { SwitchKey.Shift => "Shift", SwitchKey.Ctrl => "Ctrl", _ => "sol Alt" }} tuşunu basılı tutun",
            _ => "Geçmek için kenara iki kez dokunun"
        };
        return b.DoubleTapAllowed && b.Reason != BlockReason.DoubleTap ? text + " · kenara iki kez dokunarak geçebilirsiniz." : text + ".";
    }

    private void Flush()
    {
        if (fx.Count == 0) return;
        // Snapshot first: an effect may re-enter (same thread, lock is re-entrant) and queue more effects.
        var effects = fx.ToArray();
        fx.Clear();
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case EngineEffect.WarpLocal w: platform.WarpCursor(w.Point); break;
                case EngineEffect.HideLocalCursor h: platform.SetCursorHidden(h.Hidden, h.Anchor); break;
                case EngineEffect.Enter e:
                    network.Send(e.Peer, Frame.Enter(e.Position)); stateChanged = true; lastActivity = DateTimeOffset.UtcNow; break;
                case EngineEffect.Move m: network.Send(m.Peer, Frame.Input(new(EventKind.Move, m.Position.X, m.Position.Y))); break;
                case EngineEffect.Leave l: network.Send(l.Peer, Frame.Empty(FrameType.Leave)); stateChanged = true; lastActivity = DateTimeOffset.UtcNow; break;
                case EngineEffect.Forward f: network.Send(f.Peer, Frame.Input(f.Input)); break;
                case EngineEffect.Command c: RunCommand(c); break;
                case EngineEffect.MaskMenu:
                    try { platform.MaskMenuKey(); } catch (Exception) { /* Cosmetic only. */ }
                    break;
                case EngineEffect.Blocked b: pendingNotice = BlockedText(b, engine.Options.RequiredKey); break;
            }
        }
        Flush();
    }

    /// <summary>Raises events outside the lock: Changed only when sharing state changed (not on every mouse move).</summary>
    private void AfterHook()
    {
        bool raise; string? notice;
        lock (gate) { raise = stateChanged; stateChanged = false; notice = pendingNotice; pendingNotice = null; }
        if (notice != null) Notice?.Invoke(notice);
        if (raise) RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();

    private List<DeviceInfo> KnownDevices()
    {
        var online = engine.Peers.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var record in network.Directory.All)
            if (!online.ContainsKey(record.Id) && record.LastKnownDevice is { } d) online[d.Id] = d;
        return [engine.Local, .. online.Values];
    }

    private void Publish(ScreenLayout next)
    {
        next.Revision = layout.Revision + 1;
        next.Author = engine.Local.Id;
        layout = next;
        engine.Layout = layout;
        SaveLayout();
        network.Broadcast(Frame.Of(FrameType.Layout, layout));
    }

    private ScreenLayout LoadLayout()
    {
        var loaded = store?.Load(LayoutFile, () => new ScreenLayout()) ?? new ScreenLayout();
        try { loaded.Offsets = new(loaded.Offsets ?? [], StringComparer.Ordinal); loaded.Author ??= ""; loaded.Validate(); return loaded; }
        catch (InvalidDataException) { return new ScreenLayout(); }
    }

    private void SaveLayout() => saver.Schedule(LayoutFile, layout.Clone());

    public void Dispose()
    {
        ReturnHome();
        saver.WaitIdle(TimeSpan.FromSeconds(2));
        network.LinkUp -= OnLinkUp;
        network.LinkDown -= OnLinkDown;
        network.FrameReceived -= OnFrame;
        network.Changed -= RaiseChanged;
        network.Directory.Changed -= RaiseChanged;
    }
}
