using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BankoShare.Core;

public sealed class Pairing : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public byte[] Secret { get; } = RandomNumberGenerator.GetBytes(32);
    public string Code => $"BS1-{Convert.ToHexString(Certificate.GetCertHash(HashAlgorithmName.SHA256))}-{Convert.ToHexString(Secret)}";
    public Pairing()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=BankoShare", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel needs a user key container. Omitting PersistKeySet lets certificate
        // disposal remove the temporary key; no certificate store or PFX file is created.
        var pfx = cert.Export(X509ContentType.Pfx);
        try { Certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    public static (byte[] Fingerprint, byte[] Secret) Parse(string code)
    {
        var parts = code.Trim().Split('-');
        if (parts.Length != 3 || parts[0] != "BS1" || parts[1].Length != 64 || parts[2].Length != 64)
            throw new FormatException("Alıcıdaki eşleştirme kodunu eksiksiz girin.");
        return (Convert.FromHexString(parts[1]), Convert.FromHexString(parts[2]));
    }
    public void Dispose() { Certificate.Dispose(); CryptographicOperations.ZeroMemory(Secret); }
}
