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

    private ShareHost(DeviceIdentity identity, PeerNetwork network, ShareController controller, WindowsPlatform platform, InputHost input, string? warning,
        string settingsPath, bool testInstance, GameGuard games)
    {
        Identity = identity; Network = network; Controller = controller; Platform = platform; Input = input; Warning = warning;
        SettingsPath = settingsPath; IsTestInstance = testInstance; Games = games;
    }

    public GameGuard Games { get; }
    /// <summary>True while the input hooks are removed for a protected game.</summary>
    public bool Suspended => Input == null;

    /// <summary>
    /// Game mode. Suspending brings the pointer home, sends back a peer that controls us, tells peers we take no
    /// input, waits for that to land and then removes both low-level hooks, so nothing on this machine intercepts
    /// or injects input while the game runs. Resuming installs the hooks again. UI thread only.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        if (suspended == Suspended) return;
        if (suspended)
        {
            Controller.SetPaused(true);
            Controller.WaitForInput(TimeSpan.FromSeconds(1));
            Input?.Dispose();
            Input = null;
            return;
        }
        Input = new InputHost(Controller, Platform);
        Controller.SetPaused(false);
    }

    public string SettingsPath { get; }
    /// <summary>Started with BANKOSHARE_TEST_PORT: must not touch the installed app's startup entry.</summary>
    public bool IsTestInstance { get; }

    public DeviceIdentity Identity { get; }
    public PeerNetwork Network { get; }
    public ShareController Controller { get; }
    public WindowsPlatform Platform { get; }
    private InputHost? Input { get; set; }
    public string? Warning { get; }

    public static ShareHost Start(string version)
    {
        // Test instances run beside the installed app: their own settings, port and no discovery broadcasts.
        var testPort = int.TryParse(Environment.GetEnvironmentVariable("BANKOSHARE_TEST_PORT"), out var p) && p is > 1024 and < 65536 ? p : 0;
        var directory = testPort != 0 ? Path.Combine(Path.GetTempPath(), "BankoShare.Test-" + testPort) : SettingsDirectory;
        var store = new SettingsStore(directory);
        Diag.Start(Path.Combine(directory, "logs"));
        Diag.Write($"Banko Share {version} başlıyor · {Environment.OSVersion.VersionString}");
        DeviceIdentity identity; string? warning = null;
        try { identity = testPort != 0 ? DeviceIdentity.CreateEphemeral() : IdentityStore.LoadOrCreate(directory); }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            identity = DeviceIdentity.CreateEphemeral();
            warning = "Kalıcı cihaz kimliği oluşturulamadı; eşleştirmeler uygulama kapanınca geçersiz olur.";
        }
        var monitors = Monitors.Get();
        var platform = new WindowsPlatform(monitors);
        var name = PeerDirectory.CleanName(Environment.MachineName);
        var network = testPort != 0
            ? new PeerNetwork(identity, name + " (test)", new PeerDirectory(store), testPort, discovery: false)
            : new PeerNetwork(identity, name, new PeerDirectory(store));
        var games = new GameGuard(testInstance: testPort != 0);
        var controller = new ShareController(new DeviceInfo(identity.Id, name, monitors), network, platform, store, version, new WindowsEnvironment(games.NameOf));
        InputHost? input = null;
        try
        {
            input = new InputHost(controller, platform);
            network.Start();
        }
        catch
        {
            input?.Dispose(); controller.Dispose(); network.Dispose(); identity.Dispose(); games.Dispose();
            throw;
        }
        return new ShareHost(identity, network, controller, platform, input, warning, directory, testPort != 0, games);
    }

    public void RefreshMonitors()
    {
        var monitors = Monitors.Get();
        Platform.UpdateScreen(monitors);
        Controller.UpdateLocalMonitors(monitors);
    }

    public void Dispose()
    {
        Games.Dispose();
        Input?.Dispose();
        Controller.Dispose();
        Network.Dispose();
        Network.Directory.Flush(TimeSpan.FromSeconds(2));
        Identity.Dispose();
    }
}
