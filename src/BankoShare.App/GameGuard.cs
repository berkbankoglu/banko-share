using System.Diagnostics;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>
/// Watches for programs Banko Share must step aside for, without ever touching them:
/// <list type="bullet">
/// <item>a Riot Vanguard–protected game while it actually runs (VALORANT, League of Legends in a match). The Riot
/// Client alone (lobby, tray) does not count: nothing is protected there, and it may stay open all day;</item>
/// <item>another mouse/keyboard sharing program (ShareMouse, Synergy, Barrier, Input Leap, Deskflow, Mouse Without
/// Borders): two of them hooking the same mouse fight over every drag and click and freeze the pointer.</item>
/// </list>
/// It reads the system's process snapshot only; no handle to any process is opened.
/// </summary>
internal sealed class GameGuard : IDisposable
{
    private static readonly string[] ProtectedGames = ["valorant-win64-shipping", "league of legends"];
    private static readonly Dictionary<string, string> SharingApps = new(StringComparer.Ordinal)
    {
        ["sharemouse"] = "ShareMouse",
        ["synergy"] = "Synergy", ["synergys"] = "Synergy", ["synergyc"] = "Synergy", ["synergy-core"] = "Synergy",
        ["synergy-server"] = "Synergy", ["synergy-client"] = "Synergy",
        ["barrier"] = "Barrier", ["barriers"] = "Barrier", ["barrierc"] = "Barrier",
        ["input-leap"] = "Input Leap", ["input-leaps"] = "Input Leap", ["input-leapc"] = "Input Leap",
        ["deskflow"] = "Deskflow", ["deskflow-server"] = "Deskflow", ["deskflow-client"] = "Deskflow", ["deskflow-core"] = "Deskflow",
        ["powertoys.mousewithoutborders"] = "Mouse Without Borders", ["mousewithoutborders"] = "Mouse Without Borders",
        ["lan-mouse"] = "Lan Mouse"
    };
    private readonly string[] games;
    private readonly Timer timer;
    private volatile IReadOnlyDictionary<int, string> names = new Dictionary<int, string>();
    private volatile bool gameActive;
    private volatile string? otherApp;
    private int polling;

    /// <summary>Raised on a thread-pool thread when a game or another sharing program starts or stops.</summary>
    public event Action? Changed;

    /// <param name="testInstance">Test copies also react to a harmless stand-in process, so game mode can be tried without a game.</param>
    public GameGuard(bool testInstance = false)
    {
        games = testInstance ? [.. ProtectedGames, "bankoshare-test-game"] : ProtectedGames;
        timer = new Timer(_ => Poll(), null, 0, 2000);
    }

    public bool Active => gameActive;
    /// <summary>Display name of another sharing program that is running, if any.</summary>
    public string? OtherSharingApp => otherApp;

    /// <summary>Normalised executable name for a process id, from the last snapshot (at most two seconds old).</summary>
    public string? NameOf(int processId) => names.TryGetValue(processId, out var name) ? name : null;

    private void Poll()
    {
        if (Interlocked.Exchange(ref polling, 1) == 1) return;
        try
        {
            var map = new Dictionary<int, string>();
            foreach (var process in Process.GetProcesses())
                using (process)
                    try { map[process.Id] = SwitchOptions.NormalizeProgram(process.ProcessName); }
                    catch (InvalidOperationException) { /* Exited meanwhile. */ }
            names = map;
            var game = map.Values.Any(n => games.Contains(n));
            var other = map.Values.Select(n => SharingApps.GetValueOrDefault(n)).FirstOrDefault(n => n != null);
            if (game == gameActive && other == otherApp) return;
            if (game != gameActive) Diag.Write(game ? "Riot oyunu algılandı" : "Riot oyunu kapandı");
            if (other != otherApp) Diag.Write(other != null ? $"Başka paylaşım programı açık: {other}" : $"{otherApp} kapandı");
            gameActive = game; otherApp = other;
            Changed?.Invoke();
        }
        finally { Volatile.Write(ref polling, 0); }
    }

    public void Dispose() => timer.Dispose();
}
