using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

internal static class ReleaseSmoke
{
    public static async Task Run(string expectedVersion)
    {
        if (!Version.TryParse(expectedVersion, out _)) throw new ArgumentException("Expected numeric version.");
        const string repo = "https://github.com/berkbankoglu/banko-share";
        var root = Path.Combine(Environment.CurrentDirectory, "work", "update-smoke-" + Guid.NewGuid().ToString("N"));
        var packages = Path.Combine(root, "packages"); Directory.CreateDirectory(packages);
        var options = new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false };
        var manager = new UpdateManager(new GithubSource(repo, null, false), options,
            new TestVelopackLocator("BankoShare", "0.1.0", packages));
        var update = await manager.CheckForUpdatesAsync() ?? throw new Exception("Public update was not found.");
        if (update.TargetFullRelease.Version.ToString() != expectedVersion) throw new Exception("Unexpected release version.");
        Console.WriteLine($"PASS public GitHub release discovered: {expectedVersion}");
        await manager.DownloadUpdatesAsync(update);
        var file = Path.Combine(packages, update.TargetFullRelease.FileName);
        if (!File.Exists(file)) throw new Exception("Verified package missing.");
        Console.WriteLine("PASS public package downloaded and verified without credentials");

        var current = new UpdateManager(new GithubSource(repo, null, false), options,
            new TestVelopackLocator("BankoShare", expectedVersion, packages));
        if (await current.CheckForUpdatesAsync() != null) throw new Exception("Current version offered itself as an update.");
        Console.WriteLine("PASS current version stays current");
        var newer = new UpdateManager(new GithubSource(repo, null, false), options,
            new TestVelopackLocator("BankoShare", "99.0.0", packages));
        if (await newer.CheckForUpdatesAsync() != null) throw new Exception("Downgrade offered.");
        Console.WriteLine("PASS automatic downgrade blocked");

        var corrupt = Path.Combine(root, "corrupt-feed"); Directory.CreateDirectory(corrupt);
        using var http = new HttpClient();
        var feed = await http.GetStringAsync($"{repo}/releases/download/v{expectedVersion}/releases.win.json");
        await File.WriteAllTextAsync(Path.Combine(corrupt, "releases.win.json"), feed);
        var corruptFile = Path.Combine(corrupt, update.TargetFullRelease.FileName);
        File.Copy(file, corruptFile);
        using (var stream = new FileStream(corruptFile, FileMode.Open, FileAccess.ReadWrite))
        { var first = stream.ReadByte(); stream.Position = 0; stream.WriteByte((byte)(first ^ 255)); }
        var invalidPackages = Path.Combine(root, "invalid-packages"); Directory.CreateDirectory(invalidPackages);
        var invalid = new UpdateManager(new SimpleFileSource(new DirectoryInfo(corrupt)), options,
            new TestVelopackLocator("BankoShare", "0.1.0", invalidPackages));
        var invalidUpdate = await invalid.CheckForUpdatesAsync() ?? throw new Exception("Test feed missing.");
        try { await invalid.DownloadUpdatesAsync(invalidUpdate); throw new Exception("Corrupt package was accepted."); }
        catch (ChecksumFailedException) { Console.WriteLine("PASS corrupted package rejected by checksum verification"); }
        Console.WriteLine("Release smoke checks passed; no application installed or OS input injected.");
    }
}
