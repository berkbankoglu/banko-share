using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;

namespace BankoShare.Core;

public enum EventKind : byte { Heartbeat, Move, Button, Wheel, Key, ReleaseAll }

/// <summary>Fixed-size input event. Move carries an absolute position in the receiver's desktop coordinates.</summary>
public readonly record struct InputEvent(EventKind Kind, int A = 0, int B = 0, int C = 0)
{
    public const int Size = 16;
    public byte[] Encode()
    {
        Validate();
        var bytes = new byte[Size];
        bytes[0] = 1; bytes[1] = (byte)Kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), A);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), B);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), C);
        return bytes;
    }
    public static InputEvent Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size || bytes[0] != 1 || bytes[2] != 0 || bytes[3] != 0)
            throw new InvalidDataException("Unsupported input frame.");
        var result = new InputEvent((EventKind)bytes[1], BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]));
        result.Validate(); return result;
    }
    private void Validate()
    {
        var valid = Kind switch
        {
            EventKind.Heartbeat or EventKind.ReleaseAll => A == 0 && B == 0 && C == 0,
            EventKind.Move => A is >= -32767 and <= 32767 && B is >= -32767 and <= 32767 && C == 0,
            EventKind.Button => A is >= 1 and <= 5 && B is 0 or 1 && C == 0,
            EventKind.Wheel => A is >= -32767 and <= 32767 && B is 0 or 1 && C == 0,
            EventKind.Key => A is >= 1 and <= 255 && B is 0 or 1 && C is 0 or 1,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Invalid input event.");
    }
}

/// <summary>LegacyPairRequest is the 0.3 pairing request; it is refused.</summary>
public enum FrameType : byte { Heartbeat, Hello, Layout, Enter, Input, Leave, Reclaim, LegacyPairRequest, PairAccepted, PairStart, PairReply, PairFinish }

/// <summary>Port is the sender's listening port; an inbound connection only reveals an ephemeral one.</summary>
public sealed record HelloMessage(DeviceInfo Device, string Version, int Port);
/// <summary>SPAKE2 messages; byte fields are Base64.</summary>
public sealed record PairStartMessage(string Element, string Name);
public sealed record PairReplyMessage(string Element, string Confirmation);
public sealed record PairFinishMessage(string Confirmation);

/// <summary>
/// Wire frame: version byte, type byte, little-endian payload length, payload.
/// Every type has a fixed or bounded length, so a peer can never make us allocate more than 16 KB.
/// </summary>
public readonly record struct Frame(FrameType Type, byte[] Payload)
{
    public const byte Version = 2;
    public const int HeaderSize = 4;
    public const int MaxPayload = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 16 };

    public static Frame Empty(FrameType type) => new(type, []);
    public static Frame Input(InputEvent e) => new(FrameType.Input, e.Encode());
    public static Frame Enter(PointI p) => new(FrameType.Enter, new InputEvent(EventKind.Move, p.X, p.Y).Encode());
    public static Frame Of<T>(FrameType type, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length is 0 or > MaxPayload) throw new InvalidDataException("Message too large.");
        return new(type, bytes);
    }

    public InputEvent AsInput() => InputEvent.Decode(Payload);
    public PointI AsPoint() { var e = AsInput(); if (e.Kind != EventKind.Move) throw new InvalidDataException("Expected position."); return new(e.A, e.B); }
    public T As<T>() where T : class
    {
        try { return JsonSerializer.Deserialize<T>(Payload, Json) ?? throw new InvalidDataException("Empty message."); }
        catch (JsonException e) { throw new InvalidDataException("Malformed message.", e); }
    }

    /// <summary>Consecutive absolute moves can be merged: only the newest position matters.</summary>
    public bool IsMove => Type == FrameType.Input && Payload.Length == InputEvent.Size && Payload[1] == (byte)EventKind.Move;

    public void WriteTo(IBufferWriter<byte> writer)
    {
        var span = writer.GetSpan(HeaderSize + Payload.Length);
        span[0] = Version; span[1] = (byte)Type;
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)Payload.Length);
        Payload.CopyTo(span[HeaderSize..]);
        writer.Advance(HeaderSize + Payload.Length);
    }

    public static async Task WriteAsync(Stream stream, Frame frame, CancellationToken token)
    {
        var buffer = new ArrayBufferWriter<byte>(HeaderSize + frame.Payload.Length);
        frame.WriteTo(buffer);
        await stream.WriteAsync(buffer.WrittenMemory, token);
        await stream.FlushAsync(token);
    }

    public static async Task<Frame> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, token);
        var type = (FrameType)header[1];
        int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        if (header[0] != Version || !LengthAllowed(type, length)) throw new InvalidDataException("Unsupported frame.");
        var payload = length == 0 ? [] : new byte[length];
        if (length != 0) await stream.ReadExactlyAsync(payload, token);
        return new(type, payload);
    }

    private static bool LengthAllowed(FrameType type, int length) => type switch
    {
        FrameType.Heartbeat or FrameType.Leave or FrameType.Reclaim or FrameType.PairAccepted => length == 0,
        FrameType.Enter or FrameType.Input => length == InputEvent.Size,
        FrameType.Hello or FrameType.Layout => length is > 0 and <= MaxPayload,
        FrameType.PairStart or FrameType.PairReply or FrameType.PairFinish => length is > 0 and <= 1024,
        _ => false
    };
}
