using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using BankoShare.Core;

/// <summary>Real TLS over loopback. No discovery broadcasts and no OS input.</summary>
internal static class NetworkTests
{
    public static async Task<int> Run()
    {
        var s = new Suite();
        await s.TestAsync("Pairing with the short code connects both devices and pins each other", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B");
            var code = b.Network.OpenPairing().Code;
            T.Equal(b.Id, await a.Network.PairAsync(b.Endpoint, code, Timeout()), "Paired id");
            await T.Eventually(() => a.Network.IsConnected(b.Id) && b.Network.IsConnected(a.Id), "both linked");
            T.Check(a.Network.Directory.IsPaired(b.Id) && b.Network.Directory.IsPaired(a.Id), "Not remembered");
            T.Check(b.Network.CurrentPairing == null, "Code still usable after pairing");
            List<Frame> received = []; b.Network.FrameReceived += (_, f) => { lock (received) received.Add(f); };
            for (var i = 0; i < 200; i++) a.Network.Send(b.Id, Frame.Input(new(EventKind.Key, 1 + i % 200, i % 2)));
            await T.Eventually(() => { lock (received) return received.Count == 200; }, "200 frames");
            lock (received) T.Check(received.Select(f => f.AsInput().A).SequenceEqual(Enumerable.Range(0, 200).Select(i => 1 + i % 200)), "Order changed");
        });
        await s.TestAsync("A wrong secret is rejected and five wrong guesses burn the code", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B");
            var code = b.Network.OpenPairing().Code;
            var wrong = code[..^1] + (code[^1] == '0' ? '1' : '0');
            for (var i = 0; i < PeerNetwork.MaxPairingAttempts; i++)
                await T.ThrowsAsync<AuthenticationException>(() => a.Network.PairAsync(b.Endpoint, wrong, Timeout()));
            T.Check(!b.Network.Directory.IsPaired(a.Id) && !a.Network.Directory.IsPaired(b.Id), "Wrong code paired");
            T.Check(b.Network.CurrentPairing == null, "Code survived brute force");
            await T.ThrowsAsync<AuthenticationException>(() => a.Network.PairAsync(b.Endpoint, code, Timeout()));
        });
        await s.TestAsync("A relay in the middle cannot pair even when it passes on the right code", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B"); using var relay = DeviceIdentity.CreateEphemeral();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            // Terminates A's TLS with its own certificate, opens its own TLS to B and copies every frame both ways.
            var relaying = Task.Run(async () =>
            {
                using var inbound = await listener.AcceptTcpClientAsync();
                var (fromA, _) = await PeerLink.AcceptAsync(inbound, relay, Timeout());
                var (outbound, toB, _) = await PeerLink.ConnectAsync(b.Endpoint, relay, _ => true, Timeout());
                using (outbound)
                {
                    async Task Pump(Stream from, Stream to)
                    {
                        try { while (true) await Frame.WriteAsync(to, await Frame.ReadAsync(from, Timeout()), Timeout()); }
                        catch (Exception) { from.Dispose(); to.Dispose(); }
                    }
                    await Task.WhenAll(Pump(fromA, toB), Pump(toB, fromA));
                }
            });
            var code = b.Network.OpenPairing().Code;
            await T.ThrowsAsync<AuthenticationException>(() => a.Network.PairAsync(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port), code, Timeout()));
            await Task.WhenAny(relaying, Task.Delay(5000));
            listener.Stop();
            T.Check(a.Network.Directory.All.Count == 0 && b.Network.Directory.All.Count == 0, "Someone pinned the relay");
            T.Check(b.Network.CurrentPairing != null, "One relayed attempt must not burn the code");
            await a.Network.PairAsync(b.Endpoint, code, Timeout());
            T.Check(a.Network.Directory.IsPaired(b.Id) && b.Network.Directory.IsPaired(a.Id), "Direct pairing with the same code failed");
        });
        await s.TestAsync("Unpaired certificates cannot open a session", async () =>
        {
            using var b = Node.Create("B"); using var stranger = DeviceIdentity.CreateEphemeral();
            var (client, ssl, _) = await PeerLink.ConnectAsync(b.Endpoint, stranger, _ => true, Timeout());
            using (client) using (ssl)
            {
                await Frame.WriteAsync(ssl, Frame.Empty(FrameType.Heartbeat), Timeout());
                await T.ThrowsAsync<IOException>(async () => { while (true) await Frame.ReadAsync(ssl, Timeout()); });
            }
            T.Check(!b.Network.IsConnected(stranger.Id), "Stranger linked");
        });
        await s.TestAsync("A dropped link reconnects; a silent peer is cut after the read timeout", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B");
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            await T.Eventually(() => b.Network.IsConnected(a.Id), "linked");
            var downs = 0; b.Network.LinkDown += _ => Interlocked.Increment(ref downs);
            a.Network.Links.Single().Close();
            await T.Eventually(() => downs == 1, "drop noticed");
            await T.Eventually(() => a.Network.IsConnected(b.Id) && b.Network.IsConnected(a.Id), "reconnected");
            a.Network.Dispose();
            await T.Eventually(() => downs == 2, "closed peer noticed");
            // Paired certificate, but it never sends another byte after the opening frame.
            var (client, ssl, _) = await PeerLink.ConnectAsync(b.Endpoint, a.Identity, id => id == b.Id, Timeout());
            using (client) using (ssl)
            {
                await Frame.WriteAsync(ssl, Frame.Empty(FrameType.Heartbeat), Timeout());
                await T.Eventually(() => b.Network.IsConnected(a.Id), "silent link accepted");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await T.Eventually(() => downs == 3, "silent peer cut", 6000);
                T.Check(watch.Elapsed < TimeSpan.FromSeconds(5), "Read timeout too slow");
            }
        });
        await s.TestAsync("Simultaneous reconnects settle on one link in the same direction on both sides", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B");
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            b.Network.Directory.AddOrUpdate(a.Id, address: a.Endpoint.ToString());
            for (var round = 0; round < 3; round++)
            {
                foreach (var l in a.Network.Links.Concat(b.Network.Links)) l.Close();
                await Task.Delay(1500);
                await T.Eventually(() => a.Network.Links.Count == 1 && b.Network.Links.Count == 1 && !a.Network.Links[0].IsClosed && !b.Network.Links[0].IsClosed
                    && a.Network.Links[0].Initiator != b.Network.Links[0].Initiator, "single consistent link");
            }
        });
        await s.TestAsync("Controllers exchange monitors and converge on one layout", async () =>
        {
            using var pair = await ControllerPair.Create();
            await T.Eventually(() => pair.A.Controller.Snapshot().Peers.Single().Device?.Monitors.Length == 1
                && pair.B.Controller.Snapshot().Peers.Single().Device?.Monitors.Length == 3, "monitors exchanged");
            await T.Eventually(() => pair.A.Controller.Snapshot().Layout.SameArrangement(pair.B.Controller.Snapshot().Layout), "same layout");
            pair.Arrange();
            await T.Eventually(() => pair.B.Controller.Snapshot().Layout.OffsetOf(pair.B.Id) - pair.B.Controller.Snapshot().Layout.OffsetOf(pair.A.Id)
                == Fixtures.RemoteAboveMiddle, "edited layout reached the peer");
            await T.Eventually(() => pair.A.Store.Load<ScreenLayout?>(ShareController.LayoutFile, () => null)?.Revision >= 1, "layout saved");
        });
        await s.TestAsync("The pointer crosses to the peer, types there, and the peer's own mouse takes it back", async () =>
        {
            using var pair = await ControllerPair.Create();
            pair.Arrange();
            await T.Eventually(() => pair.B.Controller.Snapshot().Layout.SameArrangement(pair.A.Controller.Snapshot().Layout), "layout synced");
            var a = pair.A.Controller; var anchor = new PointI(1280, 720);
            T.Equal(HookResult.Block, a.OnMouseMove(new(1000, -6), new(1000, 0), false), "Crossing");
            await T.Eventually(() => pair.B.Platform.Injected.Contains(new(EventKind.Move, 680, 1074)), "entered at matching point");
            T.Check(pair.A.Platform.Hidden.SequenceEqual([true]) && pair.A.Platform.Warps.SequenceEqual([anchor]), "Local pointer not parked");
            for (var i = 1; i <= 50; i++) a.OnMouseMove(anchor + new PointI(2, 0), anchor, false);
            a.OnKey(0xA2, 29, false, true, 0); a.OnKey(0x43, 46, false, true, 0);
            await T.Eventually(() => pair.B.Platform.Injected.Contains(new(EventKind.Key, 46, 1)), "keys arrived");
            T.Equal(new InputEvent(EventKind.Move, 780, 1074), pair.B.Platform.Injected.Last(e => e.Kind == EventKind.Move), "Final remote position");
            T.Check(pair.B.Controller.Snapshot().ControlledByName == "A" && a.Snapshot().ControllingName == "B", "Status names");
            T.Check(pair.A.Controller.IsBusy && pair.B.Controller.IsBusy, "Updates could restart mid-session");
            // Someone at DESKTOP grabs its own mouse.
            T.Equal(HookResult.Pass, pair.B.Controller.OnMouseMove(new(800, 1000), new(780, 1000), false), "Physical move on peer");
            await T.Eventually(() => a.Snapshot().ControllingName == null, "controller released");
            T.Check(pair.A.Platform.Warps.Last() == new PointI(1000, 0) && pair.A.Platform.Hidden.Last() == false, "Not returned to the exit point");
            var released = pair.B.Platform.Injected.Where(e => e.Kind == EventKind.Key && e.B == 0).Select(e => e.A).ToHashSet();
            T.Check(released.SetEquals([29, 46]), "Held keys not released on the peer");
        });
        await s.TestAsync("Losing the peer mid-session brings the pointer home", async () =>
        {
            using var pair = await ControllerPair.Create();
            pair.Arrange();
            await T.Eventually(() => pair.B.Controller.Snapshot().Layout.SameArrangement(pair.A.Controller.Snapshot().Layout), "layout synced");
            pair.A.Controller.OnMouseMove(new(1000, -6), new(1000, 0), false);
            await T.Eventually(() => pair.B.Controller.Snapshot().ControlledByName != null, "entered");
            pair.B.Network.Dispose();
            await T.Eventually(() => pair.A.Controller.Snapshot().ControllingName == null, "returned home");
            T.Check(pair.A.Platform.Warps.Last() == new PointI(1000, 0) && pair.A.Platform.Hidden.Last() == false, "Pointer stranded");
            T.Equal(HookResult.Pass, pair.A.Controller.OnMouseMove(new(1000, -6), new(1000, 0), false), "Offline peer still a target");
        });
        await s.TestAsync("Jump hotkeys follow the layout letters, and the peer releases a held Alt without opening a menu", async () =>
        {
            using var pair = await ControllerPair.Create();
            pair.Arrange();
            await T.Eventually(() => pair.B.Controller.Snapshot().Layout.SameArrangement(pair.A.Controller.Snapshot().Layout), "layout synced");
            var a = pair.A.Controller;
            a.UpdateOptions(o => o with { JumpMods = KeyMods.Ctrl | KeyMods.Alt });
            a.OnKey(0xA2, 29, false, true, 0); a.OnKey(0xA4, 56, false, true, 0);
            T.Equal(HookResult.Block, a.OnKey(0x31, 2, false, true, 0), "Ctrl+Alt+1");
            await T.Eventually(() => pair.B.Platform.Injected.Contains(new(EventKind.Move, 960, 540)), "jumped to the centre of A (DESKTOP)");
            T.Check(pair.A.Platform.Masks == 1, "Releasing the local Alt would open a menu");
            T.Equal(HookResult.Block, a.OnKey(0x31, 2, false, false, 0), "Hotkey release must stay swallowed");
            T.Equal(HookResult.Pass, a.OnKey(0xA4, 56, false, false, 0), "Alt pressed here is released here");
            T.Equal(HookResult.Pass, a.OnKey(0xA2, 29, false, false, 0), "Ctrl pressed here is released here");
            // Pressed while DESKTOP has the pointer, so they go there; 3 = C, the middle monitor.
            a.OnKey(0xA2, 29, false, true, 0); a.OnKey(0xA4, 56, false, true, 0);
            await T.Eventually(() => pair.B.Platform.Injected.Contains(new(EventKind.Key, 56, 1)), "Alt reached DESKTOP");
            T.Equal(HookResult.Block, a.OnKey(0x33, 4, false, true, 0), "Ctrl+Alt+3");
            await T.Eventually(() => a.Snapshot().ControllingName == null && pair.B.Controller.Snapshot().ControlledByName == null, "came home");
            T.Equal(new PointI(1280, 720), pair.A.Platform.Warps.Last(), "Centre of monitor C");
            var injected = pair.B.Platform.Injected.ToList();
            int mask = injected.IndexOf(new(EventKind.Heartbeat, 0xE8)), altUp = injected.IndexOf(new(EventKind.Key, 56, 0));
            T.Check(mask >= 0 && mask < altUp, $"Mask {mask} must come before the Alt release {altUp}");
            T.Check(!pair.B.Platform.Injected.Any(e => e.Kind == EventKind.Key && e.A is 2 or 4), "Digit leaked to DESKTOP");
        });
        await s.TestAsync("The toggle hotkey and blocked pushes report a notice, and options are saved", async () =>
        {
            using var pair = await ControllerPair.Create();
            pair.Arrange();
            await T.Eventually(() => pair.B.Controller.Snapshot().Layout.SameArrangement(pair.A.Controller.Snapshot().Layout), "layout synced");
            var a = pair.A.Controller;
            List<string> notices = [];
            a.Notice += n => { lock (notices) notices.Add(n); };
            a.UpdateOptions(o => o with { RequiredKey = SwitchKey.Shift, ToggleHotkey = new(KeyMods.Ctrl | KeyMods.Shift, 0x53) });
            T.Equal(HookResult.Pass, a.OnMouseMove(new(1000, -6), new(1000, 0), false), "Crossed without Shift");
            lock (notices) T.Check(notices.SequenceEqual(["Geçmek için Shift tuşunu basılı tutun."]), "Notices: " + string.Join(" | ", notices));
            a.OnKey(0xA2, 29, false, true, 0); a.OnKey(0xA0, 42, false, true, 0);
            T.Equal(HookResult.Block, a.OnKey(0x53, 31, false, true, 0), "Ctrl+Shift+S");
            T.Check(!a.Options.SwitchingEnabled, "Toggle did not switch edge switching off");
            lock (notices) T.Check(notices.Last() == "Kenardan geçiş kapatıldı.", "Notices: " + string.Join(" | ", notices));
            await T.Eventually(() => pair.A.Store.Load<SwitchOptions?>(ShareController.PreferencesFile, () => null) is { SwitchingEnabled: false, RequiredKey: SwitchKey.Shift },
                "options saved in the background");
            lock (notices) T.Check(!notices.Any(n => n.Contains("kaydedilemedi")), "Save failed: " + string.Join(" | ", notices));
            T.Equal(HookResult.Pass, a.OnMouseMove(new(1000, -7), new(1000, 0), false), "Crossed while switching is off");
        });
        return s.Count;
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    internal sealed class Node : IDisposable
    {
        public required string Name { get; init; }
        public required DeviceIdentity Identity { get; init; }
        public required PeerNetwork Network { get; init; }
        public required SettingsStore Store { get; init; }
        public string Id => Identity.Id;
        public IPEndPoint Endpoint => new(IPAddress.Loopback, Network.Port);
        public FakePlatform Platform { get; } = new();
        public ShareController Controller { get; private set; } = null!;
        private bool disposed;

        public static Node Create(string name)
        {
            var identity = DeviceIdentity.CreateEphemeral();
            var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "banko-share-tests", Guid.NewGuid().ToString("N")));
            var network = new PeerNetwork(identity, name, new PeerDirectory(store), port: 0, discovery: false);
            network.Start();
            return new Node { Name = name, Identity = identity, Network = network, Store = store };
        }

        public Node WithController(MonitorInfo[] monitors)
        {
            Controller = new ShareController(new DeviceInfo(Id, Name, monitors), Network, Platform, Store, "test");
            return this;
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Controller?.Dispose(); Network.Dispose(); Network.Directory.Flush(TimeSpan.FromSeconds(2)); Identity.Dispose();
            try { Directory.Delete(Store.DirectoryPath, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A = BANKOARTS (three monitors), B = DESKTOP (one monitor). Created before pairing so Hello carries monitors.</summary>
    internal sealed class ControllerPair : IDisposable
    {
        public required Node A { get; init; }
        public required Node B { get; init; }

        public static async Task<ControllerPair> Create()
        {
            var a = Node.Create("A").WithController(Fixtures.Bankoarts);
            var b = Node.Create("B").WithController(Fixtures.Desktop);
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            await T.Eventually(() => a.Controller.Snapshot().Peers.Any(p => p.Connected && p.Device != null)
                && b.Controller.Snapshot().Peers.Any(p => p.Connected && p.Device != null), "controllers see each other");
            return new ControllerPair { A = a, B = b };
        }

        /// <summary>Drags DESKTOP to sit above BANKOARTS' middle monitor, as on the real desk.</summary>
        public void Arrange()
        {
            var origin = A.Controller.Snapshot().Layout.OffsetOf(A.Id);
            T.Check(A.Controller.MoveDevice(B.Id, origin + Fixtures.RemoteAboveMiddle, 40), "Layout edit refused");
        }

        public void Dispose() { A.Dispose(); B.Dispose(); }
    }
}
