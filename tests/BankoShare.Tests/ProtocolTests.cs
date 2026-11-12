using System.Numerics;
using BankoShare.Core;

internal static class ProtocolTests
{
    public static async Task<int> Run()
    {
        var s = new Suite();
        s.Test("Input events round trip and reject malformed input", () =>
        {
            InputEvent[] events = [new(EventKind.Heartbeat), new(EventKind.Move, -100, 200), new(EventKind.Key, 30, 1, 1),
                new(EventKind.Button, 5, 0), new(EventKind.Wheel, -120, 1), new(EventKind.ReleaseAll)];
            foreach (var e in events) T.Check(InputEvent.Decode(e.Encode()) == e, "Round trip mismatch");
            var bytes = events[0].Encode(); bytes[0] = 2;
            T.Throws<InvalidDataException>(() => InputEvent.Decode(bytes));
            bytes[0] = 1; bytes[1] = 255;
            T.Throws<InvalidDataException>(() => InputEvent.Decode(bytes));
            bytes[1] = 0; bytes[2] = 1;
            T.Throws<InvalidDataException>(() => InputEvent.Decode(bytes));
            T.Throws<InvalidDataException>(() => InputEvent.Decode(new byte[15]));
            T.Throws<InvalidDataException>(() => new InputEvent(EventKind.Key, 0).Encode());
            T.Throws<InvalidDataException>(() => new InputEvent(EventKind.Button, 6).Encode());
            T.Throws<InvalidDataException>(() => new InputEvent(EventKind.Move, int.MinValue).Encode());
        });
        await s.TestAsync("Frames round trip in order over a stream", async () =>
        {
            var hello = new HelloMessage(Fixtures.Local, "0.3.0", 24819);
            Frame[] frames = [Frame.Empty(FrameType.Heartbeat), Frame.Of(FrameType.Hello, hello), Frame.Enter(new(-5, 7)),
                Frame.Input(new(EventKind.Key, 30, 1)), Frame.Empty(FrameType.Leave), Frame.Of(FrameType.Layout, new ScreenLayout { Revision = 3 })];
            var stream = new MemoryStream();
            foreach (var f in frames) await Frame.WriteAsync(stream, f, default);
            stream.Position = 0;
            foreach (var f in frames)
            {
                var read = await Frame.ReadAsync(stream, default);
                T.Check(read.Type == f.Type && read.Payload.SequenceEqual(f.Payload), $"Frame {f.Type} changed");
            }
            var back = (await ReadOne(Frame.Of(FrameType.Hello, hello))).As<HelloMessage>();
            T.Check(back.Device.Id == hello.Device.Id && back.Device.Monitors.Length == 3 && back.Port == 24819 && !back.Paused, "Hello content changed");
            T.Check((await ReadOne(Frame.Of(FrameType.Hello, hello with { Paused = true }))).As<HelloMessage>().Paused, "Pause flag lost");
            // 0.4.0 peers send no Paused field: it must read as not paused.
            var old = System.Text.Json.JsonSerializer.Serialize(new { Device = Fixtures.Local, Version = "0.4.0", Port = 24819 });
            T.Check(!new Frame(FrameType.Hello, System.Text.Encoding.UTF8.GetBytes(old)).As<HelloMessage>().Paused, "Old hello read as paused");
            T.Equal(new PointI(-5, 7), (await ReadOne(Frame.Enter(new(-5, 7)))).AsPoint(), "Enter position");
            var layout = new ScreenLayout { Revision = 2, Author = Fixtures.LocalId };
            layout.Offsets[Fixtures.RemoteId] = Fixtures.RemoteAboveMiddle;
            var layoutBack = (await ReadOne(Frame.Of(FrameType.Layout, layout))).As<ScreenLayout>();
            T.Check(layoutBack.SameArrangement(layout) && layoutBack.Revision == 2, "Layout content changed");
        });
        await s.TestAsync("Frames with bad version, type or length are rejected before allocation", async () =>
        {
            await T.ThrowsAsync<InvalidDataException>(() => Raw(1, (byte)FrameType.Heartbeat, 0));
            await T.ThrowsAsync<InvalidDataException>(() => Raw(Frame.Version, 200, 0));
            await T.ThrowsAsync<InvalidDataException>(() => Raw(Frame.Version, (byte)FrameType.Input, 15));
            await T.ThrowsAsync<InvalidDataException>(() => Raw(Frame.Version, (byte)FrameType.Heartbeat, 1));
            await T.ThrowsAsync<InvalidDataException>(() => Raw(Frame.Version, (byte)FrameType.Hello, 0));
            await T.ThrowsAsync<InvalidDataException>(() => Raw(Frame.Version, (byte)FrameType.Hello, Frame.MaxPayload + 1));
            T.Throws<InvalidDataException>(() => new Frame(FrameType.Hello, "{"u8.ToArray()).As<HelloMessage>());
            await T.ThrowsAsync<EndOfStreamException>(() => Frame.ReadAsync(new MemoryStream([Frame.Version, (byte)FrameType.Input, 16, 0, 1]), default));
        });
        await s.TestAsync("Pairing codes are six fresh digits and forgiving to type", async () =>
        {
            var code = PairingCode.Create();
            T.Check(System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{6}$"), "Unexpected format: " + code);
            T.Equal(code[..3] + " " + code[3..], PairingCode.Format(code), "Display format");
            T.Equal("012345", PairingCode.Parse(" 012 345 "), "Spaces");
            T.Equal("012345", PairingCode.Parse("012-345"), "Dash");
            foreach (var bad in (string[])["12345", "1234567", "12345a", "١٢٣٤٥٦", ""])
                T.Throws<FormatException>(() => PairingCode.Parse(bad));
            var many = Enumerable.Range(0, 50).Select(_ => PairingCode.Create()).ToHashSet();
            T.Check(many.Count >= 45, "Codes repeat: " + many.Count);
            await T.ThrowsAsync<InvalidDataException>(() => Raw(Frame.Version, (byte)FrameType.LegacyPairRequest, 10));
        });
        s.Test("SPAKE2 agrees only on the same code and the same two certificates", () =>
        {
            static (Spake2 C, Spake2 S) Run(string clientCode, string serverCode, string clientSeesServer = "S", string serverSeesClient = "C")
            {
                var c = Spake2.Client(clientCode, "C", clientSeesServer);
                var sv = Spake2.Server(serverCode, serverSeesClient, "S");
                c.Receive(sv.Message); sv.Receive(c.Message);
                return (c, sv);
            }
            var (c1, s1) = Run("482913", "482913");
            T.Check(c1.Verify(s1.Confirmation) && s1.Verify(c1.Confirmation), "Same code did not agree");
            T.Check(!c1.Message.SequenceEqual(Spake2.Client("482913", "C", "S").Message), "Messages repeat");
            var (c2, s2) = Run("482913", "482914");
            T.Check(!c2.Verify(s2.Confirmation) && !s2.Verify(c2.Confirmation), "Different codes agreed");
            // A relay between two TLS sessions: each side sees the relay's certificate instead of the other's.
            var (c3, s3) = Run("482913", "482913", clientSeesServer: "RELAY", serverSeesClient: "RELAY");
            T.Check(!c3.Verify(s3.Confirmation) && !s3.Verify(c3.Confirmation), "Relayed exchange agreed");
            T.Check(!c1.Verify(new byte[32]) && !c1.Verify([]), "Forged confirmation accepted");
        });
        s.Test("SPAKE2 runs in a safe-prime group and rejects elements outside it", () =>
        {
            T.Check(ProbablePrime(Spake2.P, 12) && ProbablePrime(Spake2.Q, 12), "p or q is not prime");
            T.Equal(2048L, (long)Spake2.P.GetBitLength(), "Group size");
            foreach (var e in (BigInteger[])[Spake2.G, Spake2.M, Spake2.N])
                T.Check(e > 1 && BigInteger.ModPow(e, Spake2.Q, Spake2.P) == 1, "Generator or blinding element outside the subgroup");
            T.Check(Spake2.M != Spake2.N, "M equals N");
            var server = Spake2.Server("000000", "C", "S");
            foreach (var bad in (BigInteger[])[0, 1, Spake2.P - 1, Spake2.P, Spake2.P - 4])
                T.Throws<InvalidDataException>(() => server.Receive(Fixed(bad)));
            T.Throws<InvalidDataException>(() => server.Receive(new byte[255]));
        });
        return s.Count;
    }

    private static byte[] Fixed(BigInteger value)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[Spake2.ElementBytes];
        if (raw.Length <= result.Length) raw.CopyTo(result, result.Length - raw.Length);
        return result;
    }

    /// <summary>Miller–Rabin with random bases.</summary>
    private static bool ProbablePrime(BigInteger n, int rounds)
    {
        var d = n - 1; var r = 0;
        while (d.IsEven) { d >>= 1; r++; }
        for (var i = 0; i < rounds; i++)
        {
            var a = new BigInteger(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64), isUnsigned: true) % (n - 3) + 2;
            var x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1) continue;
            var witness = true;
            for (var j = 1; j < r && witness; j++) { x = BigInteger.ModPow(x, 2, n); if (x == n - 1) witness = false; }
            if (witness) return false;
        }
        return true;
    }

    private static async Task<Frame> ReadOne(Frame frame)
    {
        var stream = new MemoryStream();
        await Frame.WriteAsync(stream, frame, default);
        stream.Position = 0;
        return await Frame.ReadAsync(stream, default);
    }

    private static Task<Frame> Raw(byte version, byte type, int length)
    {
        var bytes = new byte[4 + Math.Min(length, 64)];
        bytes[0] = version; bytes[1] = type; bytes[2] = (byte)length; bytes[3] = (byte)(length >> 8);
        return Frame.ReadAsync(new MemoryStream(bytes), default);
    }
}
