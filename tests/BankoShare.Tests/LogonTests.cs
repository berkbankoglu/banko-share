using BankoShare.Core;
using static NetworkTests;

/// <summary>
/// The logon-screen helper over real TLS on loopback: only the device itself can name the certificate that speaks
/// for it on the sign-in screen, the app steps aside while the helper is there, and input reaches the helper.
/// </summary>
internal static class LogonTests
{
    public static async Task<int> Run()
    {
        var s = new Suite();
        s.Test("A logon-screen certificate is recorded only for a paired device and never as another device", () =>
        {
            var directory = new PeerDirectory(null);
            string a = new('A', 64), b = new('B', 64), helper = new('C', 64), stranger = new('D', 64);
            T.Check(!directory.SetLogonId(a, helper), "Unpaired device got a helper");
            directory.AddOrUpdate(a, "A"); directory.AddOrUpdate(b, "B");
            T.Check(!directory.SetLogonId(a, b), "A paired device became another's helper");
            T.Check(directory.SetLogonId(a, helper), "Helper not recorded");
            T.Equal(a, directory.Resolve(helper), "Helper resolves to its device");
            T.Equal(b, directory.Resolve(b), "Device resolves to itself");
            T.Equal(stranger, directory.Resolve(stranger), "Stranger changed");
            T.Check(!directory.SetLogonId(b, helper), "Two devices share one helper");
            directory.AddOrUpdate(a, "A renamed", "192.168.1.4:24819");
            T.Equal(helper, directory.Get(a)!.LogonId, "An update dropped the helper");
            T.Check(directory.SetLogonId(a, ""), "Forgetting failed");
            T.Equal(helper, directory.Resolve(helper), "A forgotten helper still speaks for its device");
            T.Throws<ArgumentException>(() => directory.SetLogonId(a, "not-a-fingerprint"));
        });
        s.Test("The helper's read-only settings store never writes the user's files", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "banko-share-tests", Guid.NewGuid().ToString("N"));
            new SettingsStore(path) { ReadOnly = true }.Save("layout.json", new ScreenLayout());
            T.Check(!File.Exists(Path.Combine(path, "layout.json")), "Read-only store wrote");
        });
        s.Test("A logon identity frame must carry a short payload", () =>
        {
            var frame = Frame.Of(FrameType.LogonIdentity, new LogonIdentityMessage(new string('C', 64)));
            T.Equal(new string('C', 64), frame.As<LogonIdentityMessage>().Id, "Round trip");
            using var empty = new MemoryStream([Frame.Version, (byte)FrameType.LogonIdentity, 0, 0]);
            T.Throws<InvalidDataException>(() => Frame.ReadAsync(empty, CancellationToken.None).GetAwaiter().GetResult());
        });
        await s.TestAsync("The helper speaks for its device only after the device named it; the app steps aside meanwhile", async () =>
        {
            using var a = Node.Create("A").WithController(Fixtures.Bankoarts, version: "0.6.0");
            using var b = Node.Create("B").WithController(Fixtures.Desktop, version: "0.6.0");
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            await T.Eventually(() => a.Controller.PeerVersion(b.Id) == "0.6.0" && b.Controller.PeerVersion(a.Id) == "0.6.0", "hello both ways");
            using var key = DeviceIdentity.CreateEphemeral();
            // Same certificate, speaking for A; disposing the key disposes it.
            var helperIdentity = new DeviceIdentity(key.Certificate, a.Id);
            var helperDirectory = new PeerDirectory(null);
            helperDirectory.AddOrUpdate(b.Id, "B", b.Endpoint.ToString());

            a.Network.Standby = true;
            await T.Eventually(() => !b.Network.IsConnected(a.Id), "app stepped aside");
            using (var unnamed = new PeerNetwork(helperIdentity, "A", helperDirectory, a.Network.Port, discovery: false, listen: false))
            {
                unnamed.Start();
                await Task.Delay(1500);
                T.Check(!b.Network.IsConnected(a.Id), "A certificate A never named spoke for A");
            }

            a.Network.Standby = false;
            await T.Eventually(() => b.Network.IsConnected(a.Id), "app back");
            a.Controller.LogonId = key.CertificateId;
            await T.Eventually(() => b.Network.Directory.Get(a.Id)?.LogonId == key.CertificateId, "B recorded A's helper");

            a.Network.Standby = true;
            await T.Eventually(() => !b.Network.IsConnected(a.Id), "app stepped aside again");
            var platform = new FakePlatform();
            var network = new PeerNetwork(helperIdentity, "A", helperDirectory, a.Network.Port, discovery: false, listen: false);
            var controller = new ShareController(new DeviceInfo(a.Id, "A", Fixtures.Bankoarts), network, platform, null, "0.6.0") { OnLogonScreen = true };
            try
            {
                network.Start();
                await T.Eventually(() => b.Network.Links.Any(l => l.PeerId == a.Id && l.CertificateId == key.CertificateId), "helper linked as A");
                await T.Eventually(() => b.Controller.PeerOnLogonScreen(a.Id) && controller.PeerVersion(b.Id) == "0.6.0", "B sees A's sign-in screen");
                T.Check(!a.Network.IsConnected(b.Id), "The app kept a link while standing by");
                // Over its own link the helper cannot name a different helper.
                controller.LogonId = new string('E', 64);
                await Task.Delay(500);
                T.Equal(key.CertificateId, b.Network.Directory.Get(a.Id)?.LogonId, "The helper renamed itself");
                // B's pointer and keys reach the sign-in screen.
                b.Network.Send(a.Id, Frame.Enter(new PointI(100, 100)));
                b.Network.Send(a.Id, Frame.Input(new InputEvent(EventKind.Key, 0x1E, 1)));
                b.Network.Send(a.Id, Frame.Input(new InputEvent(EventKind.Key, 0x1E, 0)));
                await T.Eventually(() => platform.Injected.Count(e => e.Kind == EventKind.Key) == 2, "keys reached the sign-in screen");
                T.Check(platform.Injected.Any(e => e is { Kind: EventKind.Move, A: 100, B: 100 }), "Pointer did not arrive");
            }
            finally { controller.Dispose(); network.Dispose(); }

            a.Network.Standby = false;
            await T.Eventually(() => b.Network.Links.Any(l => l.PeerId == a.Id && l.CertificateId == a.Id) && !b.Controller.PeerOnLogonScreen(a.Id), "app back after sign-in");
            a.Controller.LogonId = "";
            await T.Eventually(() => b.Network.Directory.Get(a.Id)?.LogonId == null, "B forgot A's helper");
        });
        await s.TestAsync("Peers before 0.6 are not sent the logon identity, which they would not understand", async () =>
        {
            using var a = Node.Create("A").WithController(Fixtures.Bankoarts, version: "0.6.0");
            using var b = Node.Create("B").WithController(Fixtures.Desktop, version: "0.5.4");
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            await T.Eventually(() => a.Controller.PeerVersion(b.Id) == "0.5.4", "hello");
            a.Controller.LogonId = new string('C', 64);
            await Task.Delay(500);
            T.Check(b.Network.Directory.Get(a.Id)?.LogonId == null && a.Network.IsConnected(b.Id), "0.5 peer was sent the frame");
        });
        return s.Count;
    }
}
