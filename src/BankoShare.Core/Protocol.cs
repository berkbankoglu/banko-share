using System.Buffers.Binary;

namespace BankoShare.Core;

public enum EventKind : byte { Heartbeat, Move, Button, Wheel, Key, ReleaseAll }

// Fixed-size, versioned frames prevent unbounded peer-controlled allocations.
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
