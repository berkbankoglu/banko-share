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

public sealed record PeerStatus(string Id, string Name, bool Connected, string? Address, DeviceInfo? Device, bool Paused = false, string? Version = null);
/// <summary>Paused: this machine has stepped aside for an anti-cheat-protected game.</summary>
public sealed record ShareSnapshot(DeviceInfo Local, IReadOnlyList<PeerStatus> Peers, IReadOnlyList<DiscoveredDevice> Discovered,
    ScreenLayout Layout, string? ControllingName, string? ControlledByName, SwitchOptions Options, PairingOffer? Pairing, bool Paused = false);

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
    private readonly ISwitchEnvironment? environment;
    /// <summary>Input applied on this machine for a peer. Touched only on <see cref="worker"/>.</summary>
    private readonly InputState injected;
    private readonly OsWorker worker;
    private readonly List<EngineEffect> fx = [];
    private ScreenLayout layout;
    private string? controlledBy;
    private int reclaimTravel;
    private DateTimeOffset lastActivity = DateTimeOffset.MinValue;
    private bool stateChanged, onInputThread, paused;
    /// <summary>Copy of the game lock option for the worker, which does not take the lock.</summary>
    private volatile bool gameLock;
    /// <summary>Peers that said they take no input right now (a protected game runs there).</summary>
    private readonly HashSet<string> pausedPeers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> peerVersions = new(StringComparer.Ordinal);
    /// <summary>Machines the pointer just went to from here; raised outside the lock.</summary>
    private readonly List<string> handoffs = [];
    private long lastSlowInputLog;
    private (string? Peer, long At) lastReclaim;
    private string? pendingNotice;

    public event Action? Changed;
    public event Action<string>? Notice;
    /// <summary>The pointer moved from this machine to the given peer (we entered it, or it took the pointer back from us).</summary>
    public event Action<string>? PointerHandedOff;
    /// <summary>A peer sent the tail of its diagnostic log: peer id, text.</summary>
    public event Action<string, string>? PeerLogReceived;

    public ShareController(DeviceInfo local, PeerNetwork network, IInputPlatform platform, SettingsStore? store, string version,
        ISwitchEnvironment? environment = null)
    {
        this.network = network; this.platform = platform; this.store = store; this.version = version; this.environment = environment;
        // Settings still apply in memory when a write fails.
        saver = new BackgroundSaver(store, _ => Notice?.Invoke("Ayarlar diske kaydedilemedi; bu oturumda geçerli."));
        layout = LoadLayout();
        if (layout.PlaceMissing([local]) && layout.Revision == 0) layout.Author = local.Id;
        var options = (store?.Load(PreferencesFile, () => new SwitchOptions()) ?? new SwitchOptions()).Sanitized();
        engine = new SwitchEngine(local, layout) { Options = options, Environment = environment };
        gameLock = options.PauseForProtectedGames;
        injected = new InputState(platform.Inject);
        worker = new OsWorker("Banko Share injector");
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

    public HookResult OnMouseMove(PointI pt, PointI cursor, bool localButtonsHeld, uint timeMs = 0) => InHook(() =>
    {
        if (controlledBy != null)
        {
            reclaimTravel += Math.Abs(pt.X - cursor.X) + Math.Abs(pt.Y - cursor.Y);
            if (reclaimTravel >= ReclaimDistance) Reclaim();
            return HookResult.Pass;
        }
        // Pointer changes still queued (say, a return home from the network) land before a new crossing starts.
        if (engine.ActivePeer == null && worker.Pending > 0 && engine.Local.MonitorAt(pt) == null) return HookResult.Pass;
        return engine.OnMouseMove(pt, cursor, localButtonsHeld, fx, timeMs);
    });

    public HookResult OnMouseButton(int button, bool down) => InHook(() =>
    {
        if (controlledBy != null) { if (down) Reclaim(); return HookResult.Pass; }
        return engine.OnButton(button, down, fx);
    });

    public HookResult OnMouseWheel(int delta, bool horizontal) => InHook(() =>
        controlledBy != null ? HookResult.Pass : engine.OnWheel(delta, horizontal, fx));

    public HookResult OnKey(int vk, int scan, bool extended, bool down, uint timeMs) => InHook(() =>
    {
        // This machine's own keyboard keeps working locally while a peer controls it.
        if (controlledBy != null) { engine.TrackKey(vk, down); return HookResult.Pass; }
        return engine.OnKey(vk, scan, extended, down, timeMs, fx);
    });

    /// <summary>
    /// Hook callbacks run on the input thread. Pointer effects they cause run right there, before the hook returns,
    /// so the next event already sees the parked cursor. Everything else goes through <see cref="worker"/>.
    /// </summary>
    private HookResult InHook(Func<HookResult> body)
    {
        HookResult result;
        lock (gate)
        {
            // The app removes its hooks while paused; this only covers events already on their way.
            if (paused) return HookResult.Pass;
            onInputThread = true;
            try { result = body(); Flush(); }
            finally { onInputThread = false; }
        }
        AfterHook();
        return result;
    }

    // ---- Commands from the UI or system events ----

    public void UpdateLocalMonitors(MonitorInfo[] monitors)
    {
        Frame hello;
        lock (gate)
        {
            var local = engine.Local with { Monitors = monitors };
            local.Validate();
            engine.SetLocal(local, fx);
            Flush();
            hello = HelloFrame();
        }
        network.Broadcast(hello);
        RaiseChanged();
    }

    public bool Paused { get { lock (gate) return paused; } }

    /// <summary>The peer our pointer is on, if any.</summary>
    public string? ControllingPeer { get { lock (gate) return engine.ActivePeer; } }

    /// <summary>The peer whose pointer is on this machine, if any.</summary>
    public string? ControlledByPeer { get { lock (gate) return controlledBy; } }

    public string? PeerVersion(string id) { lock (gate) return peerVersions.GetValueOrDefault(id); }

    /// <summary>Asks every connected 0.5+ peer for the tail of its diagnostic log (see <see cref="PeerLogReceived"/>).</summary>
    public int RequestPeerLogs()
    {
        string[] ids;
        lock (gate) ids = [.. peerVersions.Where(v => Versions.AtLeast(v.Value, 0, 5)).Select(v => v.Key)];
        foreach (var id in ids) network.Send(id, Frame.Empty(FrameType.LogRequest));
        return ids.Length;
    }

    /// <summary>
    /// Steps aside completely, e.g. while an anti-cheat-protected game runs here: the pointer comes home, a peer
    /// controlling us is sent back, incoming control is refused and peers are told not to switch here. The app
    /// also removes its input hooks for the duration, so nothing intercepts or injects input on this machine.
    /// </summary>
    public void SetPaused(bool value)
    {
        Frame hello;
        lock (gate)
        {
            if (paused == value) return;
            paused = value;
            if (value)
            {
                engine.ForceLocal(fx);
                if (controlledBy != null) Reclaim();
            }
            stateChanged = true;
            Flush();
            hello = HelloFrame();
        }
        AfterHook();
        network.Broadcast(hello);
        RaiseChanged();
    }

    /// <summary>Caller holds the lock.</summary>
    private Frame HelloFrame() => Frame.Of(FrameType.Hello, new HelloMessage(engine.Local, version, network.Port, paused));

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
                network.IsConnected(r.Id), r.Address, online.GetValueOrDefault(r.Id) ?? r.LastKnownDevice, pausedPeers.Contains(r.Id), peerVersions.GetValueOrDefault(r.Id))).ToList();
            var discovered = network.Discovered.Where(d => !network.Directory.IsPaired(d.Id)).ToList();
            string? NameOf(string? id) => id == null ? null : peers.FirstOrDefault(p => p.Id == id)?.Name ?? "Diğer bilgisayar";
            return new(engine.Local, peers, discovered, layout.Clone(), NameOf(engine.ActivePeer), NameOf(controlledBy), engine.Options, network.CurrentPairing, paused);
        }
    }

    // ---- Network (link threads) ----

    private void OnLinkUp(PeerLink link)
    {
        Frame hello;
        lock (gate) hello = HelloFrame();
        link.TrySend(hello);
    }

    private void OnLinkDown(PeerLink link)
    {
        lock (gate)
        {
            pausedPeers.Remove(link.PeerId);
            peerVersions.Remove(link.PeerId);
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
                    // A paused peer stays connected and on the layout but is no switching target until it says otherwise.
                    if (peerVersions.GetValueOrDefault(id) != hello.Version || pausedPeers.Contains(id) != hello.Paused)
                        Diag.Write($"Merhaba: {hello.Device.Name} v{hello.Version}{(hello.Paused ? ", kilitli" : "")}, {hello.Device.Monitors.Length} monitör");
                    peerVersions[id] = hello.Version;
                    // A peer that locks sends our pointer back first (Reclaim), then says why (Hello).
                    if (hello.Paused && !pausedPeers.Contains(id)
                        && (engine.ActivePeer == id || lastReclaim.Peer == id && Environment.TickCount64 - lastReclaim.At < 3000))
                        pendingNotice = $"{hello.Device.Name} kilitlendi (orada oyun önde ya da başka paylaşım programı açık); imleç bu bilgisayara döndü.";
                    if (hello.Paused) { pausedPeers.Add(id); engine.RemovePeer(id, fx); }
                    else { pausedPeers.Remove(id); engine.SetPeer(hello.Device, fx); }
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
                    // Paused (a protected game runs here): send the pointer straight back, inject nothing.
                    if (paused) { Diag.Write("Giriş reddedildi: kilitli"); network.Send(id, Frame.Empty(FrameType.Reclaim)); return; }
                    // Whoever moved last owns the pointer: being entered ends our own remote session.
                    engine.ForceLocal(fx);
                    if (controlledBy != null && controlledBy != id) ReleaseInjected();
                    controlledBy = id; reclaimTravel = 0; stateChanged = true;
                    Diag.Write($"Kontrol geldi: {NameOfPeer(id)}");
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
                lock (gate)
                {
                    if (controlledBy != id) return;
                    controlledBy = null; ReleaseInjected(); stateChanged = true;
                    // The pointer went back to the controller: what was copied here should follow it.
                    handoffs.Add(id);
                    Diag.Write($"Kontrol bitti: {NameOfPeer(id)}");
                }
                break;
            case FrameType.Reclaim:
                lock (gate)
                {
                    if (engine.ActivePeer != id) break;
                    Diag.Write($"Geri alındı: {NameOfPeer(id)} kontrolü geri aldı");
                    lastReclaim = (id, Environment.TickCount64);
                    engine.ForceLocal(fx); Flush();
                }
                break;
            // Late pairing traffic carries no meaning once the link is up.
            case FrameType.LogRequest:
            {
                var tail = System.Text.Encoding.UTF8.GetBytes(Diag.Tail(15 * 1024));
                link.TrySend(new Frame(FrameType.LogResponse, tail.Length > 0 ? tail : "(boş)"u8.ToArray()));
                break;
            }
            case FrameType.LogResponse:
                PeerLogReceived?.Invoke(id, System.Text.Encoding.UTF8.GetString(frame.Payload));
                break;
            case FrameType.LegacyPairRequest or FrameType.PairAccepted or FrameType.PairStart or FrameType.PairReply or FrameType.PairFinish or FrameType.TransferStart:
                break;
        }
    }

    // ---- Helpers (caller holds the lock unless noted) ----

    private void Reclaim(string why = "bu bilgisayarın faresi kullanıldı")
    {
        var peer = controlledBy!;
        controlledBy = null; stateChanged = true;
        Diag.Write($"Kontrol geri alındı: {why} ({NameOfPeer(peer)} durduruldu)");
        ReleaseInjected();
        network.Send(peer, Frame.Empty(FrameType.Reclaim));
        // That peer's pointer goes home: what was copied here should be there when it gets there.
        handoffs.Add(peer);
    }

    private const string GameLockText = "Oyun öndeyken geçiş kilitli · Alt+Tab ile oyundan çıkınca geçebilirsiniz.";

    /// <summary>A locked game is in front here. Two user32 queries, no waiting.</summary>
    private bool GameLockHolds() => gameLock && environment?.GameInFront() == true;

    private enum GameGate { Inject, Drop, SendBack }

    /// <summary>
    /// Worker thread. A peer's presses, moves and wheel turns never reach a locked game: nothing while it is in
    /// front, and no click or wheel turn on its window while it is behind. A click or the game coming in front
    /// sends the pointer back; a stray wheel turn is just dropped. Releases of keys and buttons pressed earlier
    /// still go through, so nothing stays held.
    /// </summary>
    private GameGate CheckGame(InputEvent input)
    {
        if (!gameLock || environment == null || input.Kind is EventKind.Heartbeat or EventKind.ReleaseAll) return GameGate.Inject;
        if (input is { Kind: EventKind.Key or EventKind.Button, B: 0 }) return GameGate.Inject;
        if (environment.GameInFront()) return GameGate.SendBack;
        if (input.Kind is not (EventKind.Button or EventKind.Wheel) || !environment.GameUnderCursor()) return GameGate.Inject;
        return input.Kind == EventKind.Button ? GameGate.SendBack : GameGate.Drop;
    }

    /// <summary>Worker thread: the peer's input would have gone into a game, so its pointer is sent back.</summary>
    private void RefuseForGame(InputEvent input)
    {
        lock (gate)
        {
            if (controlledBy == null) return;
            Reclaim("oyun penceresine giriş gönderilmez");
            pendingNotice = input.Kind == EventKind.Button
                ? "Diğer bilgisayarın faresi oyuna tıklayamaz; oyunu bu bilgisayarın kendi faresi ve klavyesiyle kullanın."
                : "Oyun öne geldi; imleç diğer bilgisayara döndü.";
        }
        AfterHook();
    }

    /// <summary>Queues input for this machine; it is applied in order on the worker, never under the lock.</summary>
    private void TryInject(InputEvent input) => worker.Post(() =>
    {
        switch (CheckGame(input))
        {
            case GameGate.Drop: return;
            case GameGate.SendBack: RefuseForGame(input); return;
        }
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { injected.Apply(input); }
        catch (Exception) when (input.Kind == EventKind.Move) { /* e.g. secure desktop; next move retries. */ }
        catch (Exception) { Notice?.Invoke("Giriş uygulanamadı. Yönetici olarak çalışan pencereler bu sürümde desteklenmez."); }
        // SendInput waits for every low-level hook in the session; a slow one (ours or another program's) shows here.
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (ms > 40 && Environment.TickCount64 - Interlocked.Read(ref lastSlowInputLog) > 1000)
        {
            Interlocked.Exchange(ref lastSlowInputLog, Environment.TickCount64);
            Diag.Write($"Yavaş giriş: {input.Kind} {ms:F0} ms sürdü (bir fare/klavye kancası yavaş yanıt veriyor olabilir)");
        }
    });

    private void ReleaseInjected() => worker.Post(() =>
    {
        // The controller kept the key that finished its hotkey; a lone Alt release would open the menu bar here.
        if (injected.IsKeyHeld(AltScan))
            try { platform.MaskMenuKey(); } catch (Exception) { /* Cosmetic only. */ }
        try { injected.ReleaseAll(); }
        catch (Exception) { Notice?.Invoke("Bazı tuşlar bırakılamadı; basılı kalan tuşları fiziksel klavyede bırakın."); }
    });

    /// <summary>Pointer changes on this machine: at once on the input thread, otherwise in order on the worker.</summary>
    private void Os(Action action)
    {
        if (!onInputThread) { worker.Post(action); return; }
        try { action(); } catch (Exception) { /* A failed warp or hide must not break the hook. */ }
    }

    /// <summary>Takes effect at once; the file is written in the background.</summary>
    private void ApplyOptions(SwitchOptions next)
    {
        var wasEnabled = engine.Options.SwitchingEnabled;
        engine.Options = next;
        gameLock = next.PauseForProtectedGames;
        if (wasEnabled && !next.SwitchingEnabled) engine.ForceLocal(fx);
        saver.Schedule(PreferencesFile, next);
    }

    private void RunCommand(EngineEffect.Command command)
    {
        switch (command.Action)
        {
            // Jumping would move the pointer out of, or inside, the game in front.
            case HotkeyAction.NextDevice or HotkeyAction.JumpToMonitor when engine.ActivePeer == null && GameLockHolds():
                pendingNotice = GameLockText;
                break;
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
                if (!engine.JumpTo(device.Id, monitor.Center, fx))
                    pendingNotice = pausedPeers.Contains(device.Id) ? $"{device.Name} şu an kilitli; orada oyun önde ya da başka paylaşım programı açık." : $"{device.Name} bağlı değil.";
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
            BlockReason.Game => GameLockText[..^1],
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
                case EngineEffect.WarpLocal w: Os(() => platform.WarpCursor(w.Point)); break;
                case EngineEffect.HideLocalCursor h: Os(() => platform.SetCursorHidden(h.Hidden, h.Anchor)); break;
                case EngineEffect.Enter e:
                    handoffs.Add(e.Peer);
                    Diag.Write($"Geçiş: {NameOfPeer(e.Peer)} ({e.Position.X},{e.Position.Y})");
                    network.Send(e.Peer, Frame.Enter(e.Position)); stateChanged = true; lastActivity = DateTimeOffset.UtcNow; break;
                case EngineEffect.Move m: network.Send(m.Peer, Frame.Input(new(EventKind.Move, m.Position.X, m.Position.Y))); break;
                case EngineEffect.Leave l:
                    Diag.Write($"Dönüş: {NameOfPeer(l.Peer)}");
                    network.Send(l.Peer, Frame.Empty(FrameType.Leave)); stateChanged = true; lastActivity = DateTimeOffset.UtcNow; break;
                case EngineEffect.Forward f: network.Send(f.Peer, Frame.Input(f.Input)); break;
                case EngineEffect.Command c: RunCommand(c); break;
                case EngineEffect.MaskMenu: Os(platform.MaskMenuKey); break;
                case EngineEffect.Blocked b: pendingNotice = BlockedText(b, engine.Options.RequiredKey); break;
            }
        }
        Flush();
    }

    /// <summary>Raises events outside the lock: Changed only when sharing state changed (not on every mouse move).</summary>
    private void AfterHook()
    {
        bool raise; string? notice; string[] handedOff;
        lock (gate)
        {
            raise = stateChanged; stateChanged = false; notice = pendingNotice; pendingNotice = null;
            handedOff = handoffs.Count == 0 ? [] : [.. handoffs];
            handoffs.Clear();
        }
        if (notice != null) Notice?.Invoke(notice);
        foreach (var peer in handedOff) PointerHandedOff?.Invoke(peer);
        if (raise) RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();

    private string NameOfPeer(string id) => network.Directory.Get(id)?.Name ?? id[..8];

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

    /// <summary>For tests and shutdown: waits until queued input and pointer changes have been applied.</summary>
    public bool WaitForInput(TimeSpan timeout) => worker.WaitIdle(timeout);

    public void Dispose()
    {
        ReturnHome();
        // Held keys on this machine are released and the pointer is shown before the process goes away.
        worker.WaitIdle(TimeSpan.FromSeconds(2));
        worker.Dispose();
        saver.WaitIdle(TimeSpan.FromSeconds(2));
        network.LinkUp -= OnLinkUp;
        network.LinkDown -= OnLinkDown;
        network.FrameReceived -= OnFrame;
        network.Changed -= RaiseChanged;
        network.Directory.Changed -= RaiseChanged;
    }
}
