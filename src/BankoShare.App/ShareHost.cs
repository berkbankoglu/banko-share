using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>Device identity whose RSA key lives in the user's Windows key store and cannot be exported.</summary>
internal static class IdentityStore
{
    private const string KeyName = "BankoShare.DeviceIdentity.v1";

    public static DeviceIdentity LoadOrCreate(string directory)
    {
        Directory.CreateDirectory(directory);
        var certificatePath = Path.Combine(directory, "identity.cer");
        using var key = CngKey.Exists(KeyName) ? CngKey.Open(KeyName) : CngKey.Create(CngAlgorithm.Rsa, KeyName, new CngKeyCreationParameters
        {
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.AllUsages,
            Parameters = { new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None) }
        });
        using var rsa = new RSACng(key);
        if (File.Exists(certificatePath))
        {
            using var stored = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            using var publicKey = stored.GetRSAPublicKey();
            if (publicKey != null && publicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(rsa.ExportSubjectPublicKeyInfo()))
                return new DeviceIdentity(stored.CopyWithPrivateKey(rsa));
        }
        // Only the public certificate is written to disk.
        var created = DeviceIdentity.Request(rsa).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        File.WriteAllBytes(certificatePath, created.RawData);
        return new DeviceIdentity(created);
    }
}

/// <summary>Builds and owns the running sharing stack for this window.</summary>
internal sealed class ShareHost : IDisposable
{
    public static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankoShare.Settings");

    private ShareHost(DeviceIdentity identity, PeerNetwork network, ShareController controller, WindowsPlatform platform, InputHost input, string? warning)
    { Identity = identity; Network = network; Controller = controller; Platform = platform; Input = input; Warning = warning; }

    public DeviceIdentity Identity { get; }
    public PeerNetwork Network { get; }
    public ShareController Controller { get; }
    public WindowsPlatform Platform { get; }
    private InputHost Input { get; }
    public string? Warning { get; }

    public static ShareHost Start(string version)
    {
        var store = new SettingsStore(SettingsDirectory);
        DeviceIdentity identity; string? warning = null;
        try { identity = IdentityStore.LoadOrCreate(SettingsDirectory); }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            identity = DeviceIdentity.CreateEphemeral();
            warning = "Kalıcı cihaz kimliği oluşturulamadı; eşleştirmeler uygulama kapanınca geçersiz olur.";
        }
        var monitors = Monitors.Get();
        var platform = new WindowsPlatform(monitors);
        var name = PeerDirectory.CleanName(Environment.MachineName);
        var network = new PeerNetwork(identity, name, new PeerDirectory(store));
        var controller = new ShareController(new DeviceInfo(identity.Id, name, monitors), network, platform, store, version, new WindowsEnvironment());
        InputHost? input = null;
        try
        {
            input = new InputHost(controller, platform);
            network.Start();
        }
        catch
        {
            input?.Dispose(); controller.Dispose(); network.Dispose(); identity.Dispose();
            throw;
        }
        return new ShareHost(identity, network, controller, platform, input, warning);
    }

    public void RefreshMonitors()
    {
        var monitors = Monitors.Get();
        Platform.UpdateScreen(monitors);
        Controller.UpdateLocalMonitors(monitors);
    }

    public void Dispose()
    {
        Input.Dispose();
        Controller.Dispose();
        Network.Dispose();
        Network.Directory.Flush(TimeSpan.FromSeconds(2));
        Identity.Dispose();
    }
}
