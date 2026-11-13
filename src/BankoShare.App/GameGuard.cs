using System.Diagnostics;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>
/// Watches for programs Banko Share must step aside for, without ever touching them:
/// <list type="bullet">
/// <item>a locked game: VALORANT and League of Legends in a match (Riot Vanguard), games the user adds, any game
/// Steam reports running and programs Windows lists as games (see <see cref="GameLibrary"/>). The Riot Client
/// alone (lobby, tray) does not count: nothing is protected there, and it may stay open all day. While a game
/// runs, the foreground window is checked ten times a second, so the lock can follow Alt+Tab;</item>
/// <item>another mouse/keyboard sharing program (ShareMouse, Synergy, Barrier, Input Leap, Deskflow, Mouse Without
/// Borders): two of them hooking the same mouse fight over every drag and click and freeze the pointer.</item>
/// </list>
/// It reads the system's process snapshot and the foreground window's process id only; no handle to any process
/// is opened.
/// </summary>
internal sealed class GameGuard : IDisposable
{
    private static readonly Dictionary<string, string> LockedGames = new(StringComparer.Ordinal)
    {
        ["valorant-win64-shipping"] = "VALORANT",
        ["league of legends"] = "League of Legends"
    };
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
    /// <summary>Alt+Tab and Win+Tab hosts, and the brief window Windows shows while switching: the user is still choosing.</summary>
    private static readonly HashSet<string> SwitcherClasses = ["MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd", "ForegroundStaging"];
    private const string TestGame = "bankoshare-test-game";

    private readonly bool testInstance;
    private readonly Timer snapshotTimer, focusTimer;
    private readonly object focusGate = new();
    private readonly GameFocus focus = new();
    private readonly GameLibrary library = new();
    private readonly char[] classBuffer = new char[64];
    private bool libraryFailed;
    private volatile IReadOnlyDictionary<int, string> names = new Dictionary<int, string>();
    /// <summary>Running locked games: process id → display name.</summary>
    private volatile IReadOnlyDictionary<int, string> gamePids = new Dictionary<int, string>();
    private volatile IReadOnlySet<string> games;
    private volatile bool enabled = true, inFront, focusPolling;
    private volatile string? runningGame, otherApp;
    private int polling;

    /// <summary>Raised on a thread-pool thread when a game starts, stops, comes in front or goes behind, or another sharing program starts or stops.</summary>
    public event Action? Changed;

    /// <param name="testInstance">Test copies also lock for a harmless stand-in process, so the lock can be tried without a game.</param>
    public GameGuard(bool testInstance = false)
    {
        this.testInstance = testInstance;
        games = GameSet([]);
        focusTimer = new Timer(_ => CheckFocus(), null, Timeout.Infinite, Timeout.Infinite);
        snapshotTimer = new Timer(_ => Poll(), null, 0, 2000);
    }

    /// <summary>Display name of a running locked game, if any (null while the game lock is off).</summary>
    public string? RunningGame => runningGame;
    /// <summary>A running locked game owns the foreground window (steadied, see <see cref="GameFocus"/>).</summary>
    public bool InFront => inFront;
    /// <summary>Display name of another sharing program that is running, if any.</summary>
    public string? OtherSharingApp => otherApp;

    /// <summary>Normalised executable name for a process id, from the last snapshot (at most two seconds old).</summary>
    public string? NameOf(int processId) => names.TryGetValue(processId, out var name) ? name : null;

    /// <summary>The window belongs to a running locked game. Any thread; two cheap lookups.</summary>
    public bool IsGameWindow(nint hwnd)
    {
        if (hwnd == 0 || gamePids.Count == 0) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && gamePids.ContainsKey((int)pid);
    }

    /// <summary>Applies the user's game lock settings. Cheap when nothing changed.</summary>
    public void Configure(bool lockGames, IEnumerable<string> extraGames)
    {
        var set = GameSet(extraGames);
        if (lockGames == enabled && set.SetEquals(games)) return;
        enabled = lockGames; games = set;
        ThreadPool.QueueUserWorkItem(_ => Poll());
    }

    private HashSet<string> GameSet(IEnumerable<string> extra)
    {
        var set = new HashSet<string>(LockedGames.Keys.Concat(extra), StringComparer.Ordinal);
        if (testInstance) set.Add(TestGame);
        return set;
    }

    private static string DisplayName(string name) => LockedGames.GetValueOrDefault(name) ?? (name == TestGame ? "test oyunu" : name + ".exe");

    private void Poll()
    {
        if (Interlocked.Exchange(ref polling, 1) == 1) return;
        var changed = false;
        try
        {
            var map = new Dictionary<int, string>();
            foreach (var process in Process.GetProcesses())
                using (process)
                    try { map[process.Id] = SwitchOptions.NormalizeProgram(process.ProcessName); }
                    catch (InvalidOperationException) { /* Exited meanwhile. */ }
            names = map;
            var found = new Dictionary<int, string>();
            if (enabled)
            {
                var (set, steam, windows) = (games, SteamGames(), WindowsGames());
                foreach (var (pid, name) in map)
                    if (Identify(name, set, steam, windows) is { } display) found[pid] = display;
            }
            gamePids = found;
            var game = found.Values.Order(StringComparer.Ordinal).FirstOrDefault();
            var other = map.Values.Select(n => SharingApps.GetValueOrDefault(n)).FirstOrDefault(n => n != null);
            if (game != runningGame) Diag.Write(game != null ? $"Oyun açık: {game}" : $"Oyun kapandı: {runningGame}");
            if (other != otherApp) Diag.Write(other != null ? $"Başka paylaşım programı açık: {other}" : $"{otherApp} kapandı");
            changed = game != runningGame || other != otherApp;
            runningGame = game; otherApp = other;
            // The foreground is only watched while a game runs.
            if (found.Count > 0 != focusPolling)
            {
                focusPolling = found.Count > 0;
                focusTimer.Change(focusPolling ? 0 : Timeout.Infinite, focusPolling ? 100 : Timeout.Infinite);
                if (!focusPolling) lock (focusGate) changed |= SetInFront(focus.Reset());
            }
        }
        catch (Exception e) when (e is ObjectDisposedException) { return; }
        finally { Volatile.Write(ref polling, 0); }
        if (changed) Changed?.Invoke();
    }

    /// <summary>The game a program belongs to: named by the user or built in first, then Steam's, then Windows' list.</summary>
    private static string? Identify(string name, IReadOnlySet<string> chosen, List<GameLibrary.SteamGame> steam, IReadOnlyDictionary<string, string> windows)
    {
        if (chosen.Contains(name)) return DisplayName(name);
        foreach (var game in steam)
            if (game.Programs.Contains(name)) return game.Name;
        return windows.GetValueOrDefault(name);
    }

    // Game detection must never take the app down: a timer callback that throws ends the process.
    private List<GameLibrary.SteamGame> SteamGames()
    {
        try { return library.RunningOnSteam(); }
        catch (Exception e) { LibraryFailed("Steam", e); return []; }
    }

    private IReadOnlyDictionary<string, string> WindowsGames()
    {
        try { return library.RecognisedByWindows(Environment.TickCount64); }
        catch (Exception e) { LibraryFailed("Windows oyun listesi", e); return new Dictionary<string, string>(); }
    }

    private void LibraryFailed(string source, Exception e)
    {
        if (libraryFailed) return;
        libraryFailed = true;
        Diag.Write($"{source} okunamadı: {e.GetType().Name}: {e.Message}");
    }

    private void CheckFocus()
    {
        bool changed;
        lock (focusGate)
        {
            var hwnd = Native.GetForegroundWindow();
            // A game minimising itself on Alt+Tab can stay the foreground window until the next one takes over.
            bool? front = hwnd == 0 || IsSwitcher(hwnd) ? null : IsGameWindow(hwnd) && !Native.IsIconic(hwnd);
            changed = SetInFront(focus.Update(front, Environment.TickCount64));
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Caller holds <see cref="focusGate"/>.</summary>
    private bool SetInFront(bool changed)
    {
        if (!changed) return false;
        inFront = focus.InFront;
        Diag.Write(inFront ? "Oyun öne geldi: kilitli" : "Oyun arkada: kilit açık");
        return true;
    }

    private bool IsSwitcher(nint hwnd)
    {
        var length = Native.GetClassName(hwnd, classBuffer, classBuffer.Length);
        return length > 0 && SwitcherClasses.Contains(new string(classBuffer, 0, length));
    }

    public void Dispose()
    {
        snapshotTimer.Dispose();
        focusTimer.Dispose();
    }
}
