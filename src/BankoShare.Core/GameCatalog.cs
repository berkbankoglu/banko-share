using System.Text;

namespace BankoShare.Core;

/// <summary>
/// Rules for recognising games the user never named: reading Steam's text manifests and deciding which programs in
/// a game's folder (or in Windows' list of games) count as the game. Pure functions; the app supplies the files.
/// </summary>
public static class GameCatalog
{
    /// <summary>
    /// Launchers and stores: not games, even when Windows lists them as one (it does list the League client). The
    /// lock is for playing; a lobby or a store window leaves switching free.
    /// </summary>
    private static readonly HashSet<string> Launchers = new(StringComparer.Ordinal)
    {
        "leagueclient", "leagueclientux", "leagueclientuxrender", "riotclientservices", "riotclientux", "riotclientuxrender",
        "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "battle.net launcher", "agent",
        "eadesktop", "eabackgroundservice", "origin", "upc", "ubisoftconnect", "galaxyclient", "goggalaxy",
        "xboxpcapp", "gamebar", "rockstarservice", "launcher"
    };

    /// <summary>Name fragments of helper programs shipped beside games: crash reporters, installers, runtimes.</summary>
    private static readonly string[] HelperParts =
    [
        "crashreport", "crashhandler", "crashpad", "crash_report", "crashsender", "bugsplat",
        "setup", "redist", "uninst", "unins0", "prereq", "dxweb", "dotnetfx", "webhelper", "cefsharp"
    ];

    /// <summary>
    /// Whether a program (normalised name, see <see cref="SwitchOptions.NormalizeProgram"/>) found beside a game is
    /// the game rather than a launcher, store or helper.
    /// </summary>
    public static bool IsGameProgram(string name) =>
        name.Length > 0 && !Launchers.Contains(name) && !HelperParts.Any(part => name.Contains(part, StringComparison.Ordinal));

    /// <summary>
    /// Every value stored under <paramref name="key"/> (any depth, case-insensitive) in Steam's KeyValues text, as
    /// in libraryfolders.vdf ("path") and appmanifest_*.acf ("name", "installdir"). Escapes are undone.
    /// </summary>
    public static List<string> ManifestValues(string text, string key)
    {
        var found = new List<string>();
        var tokens = Tokens(text);
        for (var i = 0; i < tokens.Count; i++)
        {
            // A string followed by a string is a pair; a string followed by a brace names a block.
            if (tokens[i] is not { } k || i + 1 >= tokens.Count || tokens[i + 1] is not { } value) continue;
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) found.Add(value);
            i++;
        }
        return found;
    }

    /// <summary>Quoted strings, with null for each brace. Comments (//) are skipped.</summary>
    private static List<string?> Tokens(string text)
    {
        var tokens = new List<string?>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '{' or '}') { tokens.Add(null); continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c != '"') continue;
            var value = new StringBuilder();
            for (i++; i < text.Length && text[i] != '"'; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i++;
                    value.Append(text[i] switch { 'n' => '\n', 't' => '\t', var other => other });
                }
                else value.Append(text[i]);
            }
            tokens.Add(value.ToString());
        }
        return tokens;
    }
}
