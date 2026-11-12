using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BankoShare.Core;
using Node = NetworkTests.Node;

/// <summary>Clipboard hand-offs over real loopback TLS, the file collector and the hand-off trigger.</summary>
internal static class ClipboardTests
{
    public static async Task<int> Run()
    {
        var s = new Suite();
        await s.TestAsync("Text, HTML, RTF, an image and a folder tree arrive intact over a separate connection", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B");
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            var source = Temp(); var target = Temp();
            try
            {
                Directory.CreateDirectory(Path.Combine(source, "Proje", "alt klasör", "boş"));
                var big = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 17);
                File.WriteAllBytes(Path.Combine(source, "Proje", "alt klasör", "büyük.bin"), big);
                File.WriteAllText(Path.Combine(source, "Proje", "not.txt"), "merhaba");
                File.WriteAllText(Path.Combine(source, "tek.txt"), "tek dosya");
                var files = ClipboardFiles.Collect([Path.Combine(source, "Proje"), Path.Combine(source, "tek.txt")], out var problem)
                    ?? throw new Exception(problem);
                var image = RandomNumberGenerator.GetBytes(4096);
                var content = new ClipboardContent
                {
                    Text = "Merhaba ğüşıöç", Html = "Version:0.9\r\n<b>kalın</b>", Rtf = @"{\rtf1 x}", ImageLength = image.Length,
                    Roots = files.Roots, Files = files.Files, Folders = files.Folders
                };
                ReceivedClipboard? received = null;
                long lastProgress = 0;
                b.Network.TransferHandler = async (peer, stream, token) =>
                {
                    T.Equal(a.Id, peer, "Sender identity");
                    received = await ClipboardTransfer.ReceiveAsync(stream, _ => target, new SyncProgress(p => lastProgress = p), token);
                };
                var accepted = await a.Network.TransferAsync(b.Id, (stream, token) =>
                    ClipboardTransfer.SendAsync(stream, content, image, files.Sources, null, token), Timeout());
                T.Check(accepted, "Receiver did not confirm");
                await T.Eventually(() => received != null, "received");
                var r = received!;
                T.Check(r.Content.Text == content.Text && r.Content.Html == content.Html && r.Content.Rtf == content.Rtf, "Text formats changed");
                T.Check(r.Image!.SequenceEqual(image), "Image changed");
                T.Check(r.RootPaths.Select(Path.GetFileName).SequenceEqual(["Proje", "tek.txt"]), "Roots: " + string.Join(", ", r.RootPaths));
                T.Check(File.ReadAllBytes(Path.Combine(target, "Proje", "alt klasör", "büyük.bin")).SequenceEqual(big), "Large file changed");
                T.Equal("merhaba", File.ReadAllText(Path.Combine(target, "Proje", "not.txt")), "Small file");
                T.Equal("tek dosya", File.ReadAllText(Path.Combine(target, "tek.txt")), "Single file");
                T.Check(Directory.Exists(Path.Combine(target, "Proje", "alt klasör", "boş")), "Empty folder missing");
                T.Equal(content.TotalBytes, lastProgress, "Progress did not reach the total");
            }
            finally { Delete(source); Delete(target); }
        });
        await s.TestAsync("A refusal sends nothing, and unpaired devices cannot open a transfer", async () =>
        {
            using var a = Node.Create("A"); using var b = Node.Create("B"); using var stranger = DeviceIdentity.CreateEphemeral();
            await a.Network.PairAsync(b.Endpoint, b.Network.OpenPairing().Code, Timeout());
            var asked = 0;
            b.Network.TransferHandler = async (_, stream, token) =>
                await ClipboardTransfer.ReceiveAsync(stream, _ => { asked++; return null; }, null, token);
            var accepted = await a.Network.TransferAsync(b.Id, (stream, token) =>
                ClipboardTransfer.SendAsync(stream, new ClipboardContent { Text = "x" }, null, [], null, token), Timeout());
            T.Check(!accepted && asked == 1, "Refusal not reported");
            // A certificate b never paired with: the connection is dropped before any handler runs.
            var (client, ssl, _) = await PeerLink.ConnectAsync(b.Endpoint, stranger, _ => true, Timeout());
            using (client) using (ssl)
            {
                await Frame.WriteAsync(ssl, Frame.Of(FrameType.TransferStart, new TransferStartMessage("clipboard")), Timeout());
                await T.ThrowsAsync<IOException>(async () => await ClipboardTransfer.SendAsync(ssl, new ClipboardContent { Text = "x" }, null, [], null, Timeout()));
            }
            T.Equal(1, asked, "Stranger reached the handler");
        });
        await s.TestAsync("Paths that could leave the receive folder are refused before anything is written", async () =>
        {
            var target = Temp();
            try
            {
                foreach (var evil in (string[])["../kaçış.txt", "Proje/../../kaçış.txt", "C:/Windows/x", "Proje/CON", "Proje/a:b", "Başka/x.txt", "Proje//x"])
                {
                    var header = new ClipboardContent { Roots = ["Proje"], Files = [new(evil, 1)] };
                    var stream = new MemoryStream();
                    var json = JsonSerializer.SerializeToUtf8Bytes(header);
                    var length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
                    stream.Write(length); stream.Write(json); stream.WriteByte(65);
                    stream.Position = 0;
                    var accepted = false;
                    await T.ThrowsAsync<InvalidDataException>(() => ClipboardTransfer.ReceiveAsync(stream, _ => { accepted = true; return target; }, null, Timeout()));
                    T.Check(!accepted, "Accepted " + evil);
                }
                T.Check(!Directory.Exists(Path.Combine(Path.GetDirectoryName(target)!, "kaçış.txt")) && Directory.GetFileSystemEntries(target).Length == 0, "Something was written");
                T.Throws<InvalidDataException>(() => new ClipboardContent { Roots = ["a", "A"] }.Validate());
                T.Throws<InvalidDataException>(() => new ClipboardContent { ImageLength = ClipboardContent.MaxImageBytes + 1 }.Validate());
            }
            finally { Delete(target); }
        });
        s.Test("The collector keeps same-named items apart and enforces its limits", () =>
        {
            var root = Temp();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "a")); Directory.CreateDirectory(Path.Combine(root, "b"));
                File.WriteAllText(Path.Combine(root, "a", "rapor.txt"), "1"); File.WriteAllText(Path.Combine(root, "b", "rapor.txt"), "22");
                var both = ClipboardFiles.Collect([Path.Combine(root, "a", "rapor.txt"), Path.Combine(root, "b", "rapor.txt")], out _)!;
                T.Check(both.Roots.SequenceEqual(["rapor.txt", "rapor (2).txt"]), "Roots: " + string.Join(", ", both.Roots));
                T.Check(both.Files.Select(f => f.Length).SequenceEqual([1L, 2L]) && both.Sources.Length == 2, "Files");
                T.Check(ClipboardFiles.Collect([Path.Combine(root, "a"), Path.Combine(root, "b")], out var many, maxEntries: 3) == null && many != null, "Entry limit");
                T.Check(ClipboardFiles.Collect([Path.Combine(root, "a")], out var huge, maxBytes: 0) == null && huge != null, "Size limit");
                T.Check(ClipboardFiles.Collect([Path.Combine(root, "yok")], out var missing) == null && missing != null, "Missing item");
            }
            finally { Delete(root); }
        });
        await s.TestAsync("The clipboard follows the pointer: entering hands off to the peer, coming back hands off to us", async () =>
        {
            using var pair = await NetworkTests.ControllerPair.Create();
            pair.Arrange();
            await T.Eventually(() => pair.B.Controller.Snapshot().Layout.SameArrangement(pair.A.Controller.Snapshot().Layout), "layout synced");
            List<string> fromA = [], fromB = [];
            pair.A.Controller.PointerHandedOff += p => { lock (fromA) fromA.Add(p); };
            pair.B.Controller.PointerHandedOff += p => { lock (fromB) fromB.Add(p); };
            var anchor = new PointI(1280, 720);
            pair.A.Controller.OnMouseMove(new(1000, -6), new(1000, 0), false);
            lock (fromA) T.Check(fromA.SequenceEqual([pair.B.Id]), "A did not hand off when entering B");
            await T.Eventually(() => pair.B.Controller.Snapshot().ControlledByName != null, "entered");
            pair.A.Controller.OnMouseMove(anchor + new PointI(0, 20), anchor, false);
            await T.Eventually(() => { lock (fromB) return fromB.SequenceEqual([pair.A.Id]); }, "B handed off when the pointer went back");
            T.Check(pair.A.Controller.Snapshot().Peers.Single().Version == "test", "Peer version not shown");
        });
        await s.TestAsync("A peer's diagnostic log can be fetched over the link", async () =>
        {
            using var pair = await NetworkTests.ControllerPair.Create();
            Diag.Write("tanılama denemesi " + Guid.NewGuid().ToString("N")[..6]);
            string? text = null;
            pair.A.Controller.PeerLogReceived += (_, t) => text = t;
            // Test peers report version "test", which is not 0.5+, so the request goes out by hand.
            pair.A.Network.Send(pair.B.Id, Frame.Empty(FrameType.LogRequest));
            await T.Eventually(() => text != null, "log arrived");
            T.Check(text!.Contains("tanılama denemesi"), "Log tail missing: " + text);
            T.Check(Versions.AtLeast("0.5.0+abc", 0, 5) && !Versions.AtLeast("0.4.2", 0, 5) && !Versions.AtLeast("test", 0, 5), "Version compare");
        });
        return s.Count;
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "banko-share-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Progress that reports synchronously (System.Progress posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<long> report) : IProgress<long> { public void Report(long value) => report(value); }
}
