using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BankoShare.Core;

/// <summary>A device's TLS certificate with private key. The certificate fingerprint is the device id.</summary>
public sealed class DeviceIdentity(X509Certificate2 certificate) : IDisposable
{
    public X509Certificate2 Certificate { get; } = certificate.HasPrivateKey ? certificate : throw new ArgumentException("Private key required.");
    public string Id { get; } = Fingerprint(certificate);

    public static string Fingerprint(X509Certificate certificate) => Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

    public static CertificateRequest Request(RSA key)
    {
        var request = new CertificateRequest("CN=BankoShare", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        // Both roles: every device accepts connections and connects out.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));
        return request;
    }

    /// <summary>
    /// In-memory identity for tests. SChannel needs a key container, so the certificate is round-tripped
    /// through PKCS#12 without PersistKeySet; disposal removes the temporary key.
    /// </summary>
    public static DeviceIdentity CreateEphemeral()
    {
        using var key = RSA.Create(2048);
        using var cert = Request(key).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = cert.Export(X509ContentType.Pfx);
        try { return new(X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet)); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    public void Dispose() => Certificate.Dispose();
}

/// <summary>
/// One-time pairing code shown by the device being added: 13 Crockford base32 characters of its
/// certificate fingerprint (65 bits) followed by a 7-character secret (35 bits).
/// The connecting side checks the fingerprint prefix before it reveals the secret, so a
/// man-in-the-middle never learns it; online guesses are limited to a few attempts per code.
/// </summary>
public static class PairingCode
{
    public const int FingerprintChars = 13, SecretChars = 7, Length = FingerprintChars + SecretChars;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Create(string deviceId, out string secret)
    {
        secret = string.Concat(Enumerable.Range(0, SecretChars).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]));
        var raw = FingerprintPrefix(deviceId) + secret;
        return string.Join('-', Enumerable.Range(0, 4).Select(i => raw.Substring(i * 5, 5)));
    }

    public static (string FingerprintPrefix, string Secret) Parse(string code)
    {
        var chars = new List<char>(Length);
        foreach (var raw in code.ToUpperInvariant())
        {
            if (raw is '-' or ' ' or '\t') continue;
            var c = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            if (!Alphabet.Contains(c)) throw new FormatException("Kodda geçersiz karakter var.");
            chars.Add(c);
        }
        if (chars.Count != Length) throw new FormatException("Eşleştirme kodu 20 karakter olmalı.");
        var s = new string([.. chars]);
        return (s[..FingerprintChars], s[FingerprintChars..]);
    }

    public static bool Matches(string deviceId, string fingerprintPrefix) => FingerprintPrefix(deviceId) == fingerprintPrefix;

    private static string FingerprintPrefix(string deviceId)
    {
        var bytes = Convert.FromHexString(deviceId);
        var result = new char[FingerprintChars];
        for (var i = 0; i < FingerprintChars; i++)
        {
            int bit = i * 5, value = 0;
            for (var b = 0; b < 5; b++, bit++) value = (value << 1) | ((bytes[bit / 8] >> (7 - bit % 8)) & 1);
            result[i] = Alphabet[value];
        }
        return new string(result);
    }
}
