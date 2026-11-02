using System.IO;
using System.Reflection;
using System.Text.Json;
using BankoShare.Core;
using Velopack;
using Velopack.Sources;

namespace BankoShare.App;

internal sealed class GithubUpdates : IUpdateBackend
{
    internal const string Repository = "https://github.com/berkbankoglu/banko-share";
    private readonly UpdateManager manager = new(new GithubSource(Repository, null, false),
        new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false });
    private UpdateInfo? available;
    public bool IsInstalled => manager.IsInstalled;
    public string CurrentVersion => manager.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    public async Task<string?> FindUpdateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        available = await manager.CheckForUpdatesAsync();
        token.ThrowIfCancellationRequested();
        return available?.TargetFullRelease.Version.ToString();
    }
    public async Task DownloadAsync(Action<int> progress, CancellationToken token)
    {
        if (available == null) throw new InvalidOperationException("No update selected.");
        // Progress callbacks originate on downloader workers; marshal them to the caller's context.
        var report = new Progress<int>(progress);
        await manager.DownloadUpdatesAsync(available, p => ((IProgress<int>)report).Report(p), token);
    }
    public void ApplyAndRestart()
    {
        if (available == null) throw new InvalidOperationException("No update selected.");
        manager.ApplyUpdatesAndRestart(available.TargetFullRelease);
    }
}

internal sealed record UpdatePreferences(bool Automatic = true)
{
    // Settings live outside the version directory, which Velopack replaces on upgrade.
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankoShare.Settings");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "updates.json");
    public static UpdatePreferences Load()
    {
        try { return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, FilePath, true);
    }
}
