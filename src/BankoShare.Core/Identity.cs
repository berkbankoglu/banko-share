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
/// One-time 6-digit pairing code, fresh every time it is shown. A short code is safe only because it is
/// never sent or used directly: it is the password of a SPAKE2 exchange (<see cref="Spake2"/>), so each
/// connection attempt can test a single guess and a few wrong attempts burn the code.
/// </summary>
public static class PairingCode
{
    public const int Digits = 6;

    public static string Create() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"482913" → "482 913", easier to read aloud and type.</summary>
    public static string Format(string code) => code[..3] + " " + code[3..];

    /// <summary>Accepts spaces and dashes. Throws <see cref="FormatException"/> with a message for the user.</summary>
    public static string Parse(string text)
    {
        var digits = new string([.. text.Where(c => !char.IsWhiteSpace(c) && c != '-')]);
        if (digits.Length != Digits || !digits.All(char.IsAsciiDigit)) throw new FormatException("Eşleştirme kodu 6 rakam olmalı.");
        return digits;
    }
}
