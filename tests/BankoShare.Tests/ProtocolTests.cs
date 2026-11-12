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
            T.Check(back.Device.Id == hello.Device.Id && back.Device.Monitors.Length == 3 && back.Port == 24819, "Hello content changed");
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
        s.Test("Pairing codes are short, forgiving to type, and bound to the certificate", () =>
        {
            var code = PairingCode.Create(Fixtures.LocalId, out var secret);
            T.Check(System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9A-Z]{5}(-[0-9A-Z]{5}){3}$"), "Unexpected format: " + code);
            var (prefix, parsedSecret) = PairingCode.Parse(code.ToLowerInvariant().Replace("-", " "));
            T.Check(parsedSecret == secret && PairingCode.Matches(Fixtures.LocalId, prefix), "Parse mismatch");
            T.Check(!PairingCode.Matches(Fixtures.RemoteId, prefix), "Prefix matched another device");
            T.Check(PairingCode.Parse(new string('0', 64)[..20]).FingerprintPrefix == "0000000000000", "Zero prefix");
            T.Check(PairingCode.Matches(new string('0', 64), "0000000000000") && PairingCode.Matches(new string('F', 64), "ZZZZZZZZZZZZZ"), "Base32 bits");
            T.Check(PairingCode.Parse("OOOOO-IIIII-LLLLL-00000").FingerprintPrefix == "0000011111111", "Look-alike letters not mapped");
            T.Throws<FormatException>(() => PairingCode.Parse("ABCDE-ABCDE"));
            T.Throws<FormatException>(() => PairingCode.Parse("UUUUU-UUUUU-UUUUU-UUUUU"));
            T.Check(PairingCode.Create(Fixtures.LocalId, out _) != code, "Secret reused");
        });
        return s.Count;
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
