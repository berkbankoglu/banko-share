using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace BankoShare.Core;

/// <summary>
/// SPAKE2 (Abdalla and Pointcheval, 2005; RFC 9382 gives the elliptic-curve form) in the prime-order subgroup
/// of the RFC 3526 2048-bit MODP group, where p = 2q + 1 is a safe prime.
/// <para>
/// Both sides prove they know the same short code without revealing it. A listener learns nothing, and an
/// active attacker can test exactly one guess per exchange, so a 6-digit code with a five-attempt limit is safe.
/// </para>
/// <para>
/// The transcript includes both TLS certificate fingerprints as each side saw them. Someone relaying the
/// exchange between two TLS sessions therefore produces different keys on each side, and pairing fails
/// instead of pinning the relay's certificate.
/// </para>
/// </summary>
public sealed class Spake2
{
    public const int ElementBytes = 256;

    public static readonly BigInteger P = BigInteger.Parse("00" +
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
        "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
        "15728E5A8AACAA68FFFFFFFFFFFFFFFF", NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public static readonly BigInteger Q = (P - 1) / 2;
    /// <summary>4 = 2² is a quadratic residue, so it generates the subgroup of prime order q.</summary>
    public static readonly BigInteger G = 4;
    /// <summary>Blinding elements with no known discrete logarithm: hashes squared into the subgroup.</summary>
    public static readonly BigInteger M = HashToGroup("BankoShare SPAKE2 M");
    public static readonly BigInteger N = HashToGroup("BankoShare SPAKE2 N");

    private readonly bool isClient;
    private readonly string clientId, serverId;
    private readonly BigInteger w, secret;
    private byte[]? ownConfirmation, theirConfirmation;

    /// <summary>The value to send: g^secret blinded by the code.</summary>
    public byte[] Message { get; }

    private Spake2(bool isClient, string code, string clientId, string serverId)
    {
        this.isClient = isClient; this.clientId = clientId; this.serverId = serverId;
        w = PasswordScalar(code);
        secret = RandomScalar();
        Message = Encode(BigInteger.ModPow(G, secret, P) * BigInteger.ModPow(isClient ? M : N, w, P) % P);
    }

    /// <summary>The side that types the code. Ids are certificate fingerprints as this side saw them.</summary>
    public static Spake2 Client(string code, string clientId, string serverId) => new(true, code, clientId, serverId);
    /// <summary>The side that shows the code.</summary>
    public static Spake2 Server(string code, string clientId, string serverId) => new(false, code, clientId, serverId);

    /// <summary>Proof for the other side that we derived the same key. Available after <see cref="Receive"/>.</summary>
    public byte[] Confirmation => ownConfirmation ?? throw new InvalidOperationException("Receive the other message first.");

    /// <summary>Derives the shared key from the other side's message. Throws <see cref="InvalidDataException"/> for an invalid element.</summary>
    public void Receive(byte[] message)
    {
        var other = Decode(message);
        // Remove the other side's blinding: N^(q-w) is the inverse of N^w in a group of order q.
        var shared = BigInteger.ModPow(other * BigInteger.ModPow(isClient ? N : M, Q - w, P) % P, secret, P);
        byte[] x = isClient ? Message : message, y = isClient ? message : Message;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[4];
        foreach (var part in (byte[][])[Encoding.ASCII.GetBytes("BankoShare SPAKE2 v1"), Encoding.ASCII.GetBytes(clientId),
                     Encoding.ASCII.GetBytes(serverId), x, y, Encode(shared), w.ToByteArray(isUnsigned: true, isBigEndian: true)])
        {
            BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
            hash.AppendData(length);
            hash.AppendData(part);
        }
        var key = hash.GetHashAndReset();
        var client = HMACSHA256.HashData(key, "client"u8);
        var server = HMACSHA256.HashData(key, "server"u8);
        CryptographicOperations.ZeroMemory(key);
        (ownConfirmation, theirConfirmation) = isClient ? (client, server) : (server, client);
    }

    /// <summary>True only if the other side used the same code and saw the same two certificates.</summary>
    public bool Verify(byte[] confirmation) =>
        theirConfirmation != null && CryptographicOperations.FixedTimeEquals(confirmation, theirConfirmation);

    private static BigInteger PasswordScalar(string code) =>
        new BigInteger(SHA512.HashData(Encoding.ASCII.GetBytes("BankoShare pairing code v1:" + code)), isUnsigned: true, isBigEndian: true) % Q;

    private static BigInteger RandomScalar()
    {
        while (true)
        {
            var s = new BigInteger(RandomNumberGenerator.GetBytes(ElementBytes + 16), isUnsigned: true, isBigEndian: true) % Q;
            if (!s.IsZero) return s;
        }
    }

    private static BigInteger HashToGroup(string label)
    {
        var bytes = new byte[ElementBytes + 64];
        for (var i = 0; i < bytes.Length / 64; i++) SHA512.HashData(Encoding.ASCII.GetBytes($"{label} {i}")).CopyTo(bytes, i * 64);
        return BigInteger.ModPow(new BigInteger(bytes, isUnsigned: true, isBigEndian: true) % P, 2, P);
    }

    internal static byte[] Encode(BigInteger value)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[ElementBytes];
        raw.CopyTo(result, ElementBytes - raw.Length);
        return result;
    }

    /// <summary>Peer-supplied: must be a subgroup element other than the trivial ones.</summary>
    internal static BigInteger Decode(byte[] bytes)
    {
        if (bytes.Length != ElementBytes) throw new InvalidDataException("Invalid SPAKE2 element.");
        var e = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        if (e <= 1 || e >= P - 1 || BigInteger.ModPow(e, Q, P) != 1) throw new InvalidDataException("Invalid SPAKE2 element.");
        return e;
    }
}
