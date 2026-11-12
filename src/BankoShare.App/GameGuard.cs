using System.Diagnostics;
using System.Runtime.InteropServices;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>
/// Notices when a Riot Vanguard–protected game runs on this machine, without ever touching the game:
/// it asks the service manager whether Vanguard's game-time service (vgc) is running and reads process names
/// from the system's process snapshot. No handle to any game process is opened, nothing is read from it.
/// <para>
/// The kernel driver vgk.sys stays loaded from boot even without a game; vgc runs while a protected game
/// (VALORANT, League of Legends) is open. The process names are a fallback in case the service is named
/// differently in a future Vanguard.
/// </para>
/// </summary>
internal sealed class GameGuard : IDisposable
{
    private static readonly string[] ProtectedGames = ["valorant-win64-shipping", "league of legends"];
    private readonly string[] watched;
    private readonly Timer timer;
    private volatile IReadOnlyDictionary<int, string> names = new Dictionary<int, string>();
    private volatile bool active;
    private int polling;

    /// <summary>Raised on a thread-pool thread when a protected game starts or stops.</summary>
    public event Action<bool>? Changed;

    /// <param name="testInstance">Test copies also react to a harmless stand-in process, so game mode can be tried without a game.</param>
    public GameGuard(bool testInstance = false)
    {
        watched = testInstance ? [.. ProtectedGames, "bankoshare-test-game"] : ProtectedGames;
        timer = new Timer(_ => Poll(), null, 0, 2000);
    }

    public bool Active => active;

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
            var now = VanguardGameServiceRunning() || map.Values.Any(n => watched.Contains(n));
            if (now == active) return;
            active = now;
            Changed?.Invoke(now);
        }
        finally { Volatile.Write(ref polling, 0); }
    }

    /// <summary>Read-only service query, the same as "sc query vgc".</summary>
    private static bool VanguardGameServiceRunning()
    {
        var manager = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
        if (manager == 0) return false;
        try
        {
            var service = OpenService(manager, "vgc", 0x0004); // SERVICE_QUERY_STATUS
            if (service == 0) return false;
            try { return QueryServiceStatus(service, out var status) && status.CurrentState is 2 or 4; } // START_PENDING, RUNNING
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    public void Dispose() => timer.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint Type, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")] private static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll")] private static extern bool QueryServiceStatus(nint service, out ServiceStatus status);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(nint handle);
}
