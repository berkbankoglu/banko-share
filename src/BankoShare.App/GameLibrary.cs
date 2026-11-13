using System.IO;
using Microsoft.Win32;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>
/// Games recognised without the user naming them, from what Steam and Windows already publish:
/// <list type="bullet">
/// <item>Steam marks each running app under HKCU\Software\Valve\Steam\Apps\{id} (Running = 1). Its manifest names the
/// install folder, and the programs in that folder are the game (WARDOGS: WardogsClient-Win64-Shipping.exe). The
/// folder is listed once per launch;</item>
/// <item>Windows' own list of games (Game Bar, HKCU\System\GameConfigStore), re-read once a minute.</item>
/// </list>
/// Only the registry and file names are read: no process is opened or inspected. Used from
/// <see cref="GameGuard"/>'s poll only, never concurrently.
/// </summary>
internal sealed class GameLibrary
{
    /// <summary>Steam apps that are tools rather than games: their windows are no reason to lock.</summary>
    private static readonly HashSet<string> SteamTools = new(StringComparer.Ordinal)
    {
        "228980",  // Steamworks Common Redistributables
        "250820",  // SteamVR
        "365670",  // Blender
        "431960",  // Wallpaper Engine
        "629520",  // Soundpad
        "665300",  // Stream Avatars
        "993090",  // Lossless Scaling
        "1905180"  // OBS Studio
    };
    private const int MaxPrograms = 400;
    private const long WindowsListMs = 60_000;

    /// <summary>Steam apps running at the last poll, with what their folder held (null: nothing usable).</summary>
    private readonly Dictionary<string, SteamGame?> launched = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> windowsGames = new Dictionary<string, string>();
    private long windowsReadAt;
    private bool windowsRead;

    /// <summary>A running Steam game: its store name and the normalised names of the programs in its folder.</summary>
    public sealed record SteamGame(string Name, IReadOnlySet<string> Programs);

    /// <summary>Games Steam reports running now. Reads a few registry values; lists a game's folder once per launch.</summary>
    public List<SteamGame> RunningOnSteam()
    {
        var running = new List<SteamGame>();
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var apps = steam?.OpenSubKey("Apps");
        var now = new HashSet<string>(StringComparer.Ordinal);
        if (steam != null && apps != null)
            foreach (var id in apps.GetSubKeyNames())
            {
                if (SteamTools.Contains(id)) continue;
                using (var app = apps.OpenSubKey(id))
                    if (app?.GetValue("Running") is not 1) continue;
                now.Add(id);
                if (!launched.TryGetValue(id, out var game)) launched[id] = game = Find(steam, id);
                if (game != null) running.Add(game);
            }
        // Forget games that stopped: the next launch lists the folder again (an update may have changed it).
        foreach (var id in launched.Keys.Where(id => !now.Contains(id)).ToList()) launched.Remove(id);
        return running;
    }

    /// <summary>Programs Windows recognised as games: normalised name → name as shown. Re-read once a minute.</summary>
    public IReadOnlyDictionary<string, string> RecognisedByWindows(long nowMs)
    {
        if (windowsRead && nowMs - windowsReadAt < WindowsListMs) return windowsGames;
        windowsRead = true;
        windowsReadAt = nowMs;
        var games = new Dictionary<string, string>(StringComparer.Ordinal);
        using var children = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
        if (children != null)
            foreach (var id in children.GetSubKeyNames())
            {
                using var child = children.OpenSubKey(id);
                if (child?.GetValue("MatchedExeFullPath") is not string path || path.Length == 0) continue;
                var name = SwitchOptions.NormalizeProgram(path);
                if (GameCatalog.IsGameProgram(name)) games.TryAdd(name, Path.GetFileNameWithoutExtension(path));
            }
        return windowsGames = games;
    }

    private static SteamGame? Find(RegistryKey steam, string id)
    {
        try
        {
            if (steam.GetValue("SteamPath") is not string root || root.Length == 0) return null;
            foreach (var library in Libraries(root))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{id}.acf");
                if (!File.Exists(manifest)) continue;
                var text = File.ReadAllText(manifest);
                var dir = GameCatalog.ManifestValues(text, "installdir").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(dir)) break;
                var name = GameCatalog.ManifestValues(text, "name").FirstOrDefault() is { Length: > 0 } n ? n : dir;
                var programs = Programs(Path.Combine(library, "steamapps", "common", dir));
                Diag.Write(programs.Count > 0 ? $"Steam'de açık: {name} ({programs.Count} program)" : $"Steam'de açık: {name}, ama klasöründe program bulunamadı");
                return programs.Count > 0 ? new SteamGame(name, programs) : null;
            }
            Diag.Write($"Steam'de açık bir uygulama var ({id}) ama kurulum bilgisi bulunamadı");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Diag.Write($"Steam oyunu okunamadı ({id}): {e.GetType().Name}");
        }
        return null;
    }

    /// <summary>Steam's own folder first, then every library listed in libraryfolders.vdf.</summary>
    private static IEnumerable<string> Libraries(string root)
    {
        var listed = new List<string> { root };
        foreach (var file in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
            if (File.Exists(file))
            {
                listed.AddRange(GameCatalog.ManifestValues(File.ReadAllText(file), "path"));
                break;
            }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in listed)
            if (path.Length > 0 && seen.Add(Path.GetFullPath(path)))
                yield return Path.GetFullPath(path);
    }

    private static HashSet<string> Programs(string folder)
    {
        var programs = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder)) return programs;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(folder, "*.exe", options))
        {
            var name = SwitchOptions.NormalizeProgram(file);
            if (GameCatalog.IsGameProgram(name)) programs.Add(name);
            if (programs.Count >= MaxPrograms) break;
        }
        return programs;
    }
}
