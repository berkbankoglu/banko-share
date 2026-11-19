using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using BankoShare.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BankoShare.App;

/// <summary>
/// Banko Share on the sign-in and lock screens. The app runs in the user's session after
/// sign-in and cannot reach those screens, so an optional Windows service, installed once with administrator
/// approval, starts at boot and keeps a helper running as SYSTEM in the console session. The helper works only
/// while a secure desktop is the input desktop: it connects to the paired computers for this device and applies
/// their pointer and keys there, while the app steps aside. On the normal desktop it does nothing: no hooks, no
/// links, no input, and it never looks at other processes.
/// </summary>
internal static class LogonScreen
{
    public const string ServiceName = "BankoShareLogon";
    /// <summary>Raise when the service or helper changes in a way installed copies must pick up; the app then offers an update.</summary>
    public const int Revision = 1;
    public const string ServiceArgument = "--logon-service", HelperArgument = "--logon-helper", InstallArgument = "--logon-install", UninstallArgument = "--logon-uninstall";
    /// <summary>Set by the helper while it speaks for this device; the app holds no links meanwhile.</summary>
    public const string ActiveEventName = @"Global\BankoShare.LogonHelper.Active";
    public const string StopEventName = @"Global\BankoShare.LogonHelper.Stop";

    /// <summary>Program Files: only administrators and SYSTEM can write there, so the service never runs code a user could replace.</summary>
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BankoShare");
    public static string DataDirectory => Path.Combine(Root, "data");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string CertificatePath => Path.Combine(DataDirectory, "logon-identity.cer");
    public static string ParametersKey => $@"SYSTEM\CurrentControlSet\Services\{ServiceName}\Parameters";
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Modes that run instead of the app, before the single-instance check and the updater. Null: start the app.</summary>
    public static int? TryRun(string[] args)
    {
        if (args.Length == 0) return null;
        switch (args[0])
        {
            case ServiceArgument: ServiceBase.Run(new LogonService()); return 0;
            case HelperArgument: return LogonHelper.Run();
            case InstallArgument when args.Length == 3: return LogonSetup.Install(args[1], args[2]);
            case UninstallArgument: return LogonSetup.Uninstall();
            default: return null;
        }
    }

    public sealed record State(bool Installed, bool Running, int Revision, string? Version, string? HelperId)
    {
        public bool Outdated => Installed && Revision < LogonScreen.Revision;
    }

    /// <summary>Service state, as any user may read it. Cheap: a service query, a registry key and a small file.</summary>
    public static State Read()
    {
        bool installed = false, running = false;
        try
        {
            using var service = new ServiceController(ServiceName);
            running = service.Status == ServiceControllerStatus.Running;
            installed = true;
        }
        catch (InvalidOperationException) { /* Not installed. */ }
        int revision = 0; string? version = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ParametersKey);
            revision = key?.GetValue("Revision") as int? ?? 0;
            version = key?.GetValue("Version") as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return new(installed, running, revision, version, installed ? IdentityStore.ReadId(CertificatePath) : null);
    }

    /// <summary>True while the helper speaks for this device. The event exists only while the helper runs.</summary>
    public static bool HelperActive()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(ActiveEventName, out var handle)) return false;
            using (handle) return handle.WaitOne(0);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException) { return false; }
    }

    /// <summary>Runs this program elevated to install or remove the service. Null: the Windows prompt was declined.</summary>
    public static async Task<int?> RunElevatedAsync(string arguments)
    {
        Process? process;
        try { process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return null; } // ERROR_CANCELLED
        if (process == null) return null;
        using (process)
        {
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }

    /// <summary>A quoted command-line argument. A trailing backslash would escape the closing quote, so it goes.</summary>
    public static string Quote(string value) => "\"" + value.TrimEnd('\\', '/') + "\"";
}

/// <summary>Install and removal. Runs elevated, started by the app after the user approves the Windows prompt.</summary>
internal static class LogonSetup
{
    public const int Ok = 0, BadArguments = 2, Failed = 3;
    private const string ExecutableName = "BankoShare.App.exe";

    public static int Install(string settingsDirectory, string installRoot)
    {
        Diag.Start(LogonScreen.LogDirectory, "setup.log");
        try
        {
            if (!Path.IsPathFullyQualified(settingsDirectory) || !Directory.Exists(settingsDirectory) || installRoot.Length > 0 && !Path.IsPathFullyQualified(installRoot))
            {
                Diag.Write("Kurulum: geçersiz klasör");
                return BadArguments;
            }
            var version = LogonScreen.Version;
            var target = Path.Combine(LogonScreen.Root, "app-" + version);
            Diag.Write($"Kurulum başlıyor: {version} → {target}");
            // The running service may be using the very files that are about to be replaced.
            StopService();
            var source = AppContext.BaseDirectory;
            if (!string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                CopyDirectory(source, target);
            Directory.CreateDirectory(LogonScreen.LogDirectory);
            ConfigureService(Path.Combine(target, ExecutableName));
            using (var key = Registry.LocalMachine.CreateSubKey(LogonScreen.ParametersKey))
            {
                key.SetValue("SettingsDirectory", settingsDirectory);
                key.SetValue("InstallRoot", installRoot);
                key.SetValue("Version", version);
                key.SetValue("Revision", LogonScreen.Revision, RegistryValueKind.DWord);
            }
            using (var service = new ServiceController(LogonScreen.ServiceName))
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            }
            foreach (var old in Directory.EnumerateDirectories(LogonScreen.Root, "app-*"))
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(old, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DeleteAtReboot(old); }
            Diag.Write("Kurulum tamamlandı");
            return Ok;
        }
        catch (Exception e)
        {
            Diag.Write("Kurulum başarısız: " + e);
            return Failed;
        }
        finally { Diag.WaitIdle(TimeSpan.FromSeconds(2)); }
    }

    public static int Uninstall()
    {
        // The log folder itself is removed, so this one goes to the temporary folder.
        Diag.Start(Path.Combine(Path.GetTempPath(), "BankoShare"), "logon-setup.log");
        try
        {
            StopService();
            // Windows removes the service, with its Parameters key, once it has stopped.
            DeleteServiceEntry();
            try { IdentityStore.DeleteLogonKey(); } catch (CryptographicException e) { Diag.Write("Şifre ekranı anahtarı silinemedi: " + e.Message); }
            for (var attempt = 0; Directory.Exists(LogonScreen.Root); attempt++)
            {
                try { Directory.Delete(LogonScreen.Root, true); }
                catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 10) { Thread.Sleep(500); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DeleteAtReboot(LogonScreen.Root); break; }
            }
            Diag.Write("Şifre ekranı desteği kaldırıldı");
            return Ok;
        }
        catch (Exception e)
        {
            Diag.Write("Kaldırma başarısız: " + e);
            return Failed;
        }
        finally { Diag.WaitIdle(TimeSpan.FromSeconds(2)); }
    }

    private static void StopService()
    {
        try
        {
            using var service = new ServiceController(LogonScreen.ServiceName);
            if (service.Status == ServiceControllerStatus.Stopped) return;
            if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            // The process may still be on its way out and hold its files for a moment.
            Thread.Sleep(500);
        }
        catch (InvalidOperationException) { /* Not installed. */ }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            for (var attempt = 0; ; attempt++)
            {
                try { File.Copy(file, destination, true); break; }
                catch (IOException) when (attempt < 10) { Thread.Sleep(300); }
            }
        }
    }

    private static void ConfigureService(string executable)
    {
        const uint AllAccess = 0xF003F, ServiceAllAccess = 0xF01FF, OwnProcess = 0x10, AutoStart = 2, ErrorNormal = 1, NoChange = 0xFFFFFFFF;
        const string Display = "Banko Share şifre ekranı";
        var path = $"\"{executable}\" {LogonScreen.ServiceArgument}";
        var manager = LogonNative.OpenSCManager(null, null, AllAccess);
        if (manager == 0) throw new Win32Exception();
        try
        {
            var service = LogonNative.OpenService(manager, LogonScreen.ServiceName, ServiceAllAccess);
            if (service == 0)
            {
                service = LogonNative.CreateService(manager, LogonScreen.ServiceName, Display, ServiceAllAccess, OwnProcess, AutoStart, ErrorNormal, path, null, 0, null, null, null);
                if (service == 0) throw new Win32Exception();
            }
            else if (!LogonNative.ChangeServiceConfig(service, OwnProcess, AutoStart, NoChange, path, null, 0, null, null, null, Display))
            {
                LogonNative.CloseServiceHandle(service);
                throw new Win32Exception();
            }
            try
            {
                var description = new LogonNative.ServiceDescription
                {
                    Description = "Banko Share'in şifre ve kilit ekranında çalışmasını sağlar: oturum açmadan önce de eşleşmiş bilgisayarın fare ve klavyesi bu bilgisayarda kullanılabilir. Masaüstünde hiçbir şey yapmaz."
                };
                LogonNative.ChangeServiceConfig2(service, 1, ref description); // SERVICE_CONFIG_DESCRIPTION
                // Restart after a crash: 5 s, three times a day at most.
                var actions = Marshal.AllocHGlobal(Marshal.SizeOf<LogonNative.ScAction>() * 3);
                try
                {
                    for (var i = 0; i < 3; i++)
                        Marshal.StructureToPtr(new LogonNative.ScAction { Type = 1, Delay = 5000 }, actions + i * Marshal.SizeOf<LogonNative.ScAction>(), false);
                    var failure = new LogonNative.FailureActions { ResetPeriod = 86400, ActionCount = 3, Actions = actions };
                    LogonNative.ChangeServiceConfig2(service, 2, ref failure); // SERVICE_CONFIG_FAILURE_ACTIONS
                }
                finally { Marshal.FreeHGlobal(actions); }
            }
            finally { LogonNative.CloseServiceHandle(service); }
        }
        finally { LogonNative.CloseServiceHandle(manager); }
    }

    /// <summary>Marks the service for deletion; Windows removes it once it has stopped.</summary>
    public static void DeleteServiceEntry()
    {
        const uint Delete = 0x10000;
        var manager = LogonNative.OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
        if (manager == 0) throw new Win32Exception();
        try
        {
            var service = LogonNative.OpenService(manager, LogonScreen.ServiceName, Delete);
            if (service == 0) return;
            try { if (!LogonNative.DeleteService(service) && Marshal.GetLastWin32Error() != 1072) throw new Win32Exception(); } // ERROR_SERVICE_MARKED_FOR_DELETE
            finally { LogonNative.CloseServiceHandle(service); }
        }
        finally { LogonNative.CloseServiceHandle(manager); }
    }

    /// <summary>Files in use (the running service's own) go at the next restart.</summary>
    public static void DeleteAtReboot(string root)
    {
        const uint DelayUntilReboot = 4;
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) LogonNative.MoveFileEx(file, null, DelayUntilReboot);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            LogonNative.MoveFileEx(directory, null, DelayUntilReboot);
        LogonNative.MoveFileEx(root, null, DelayUntilReboot);
    }
}

/// <summary>
/// The Windows service. Runs as LocalSystem in session 0 and keeps one helper running in the console session,
/// on the secure desktop's window station, restarting it if it exits. Removes itself once Banko Share has been
/// uninstalled.
/// </summary>
internal sealed class LogonService : ServiceBase
{
    private readonly AutoResetEvent wake = new(false);
    private readonly CancellationTokenSource stopping = new();
    private Thread? supervisor;

    public LogonService()
    {
        ServiceName = LogonScreen.ServiceName;
        CanHandleSessionChangeEvent = true; CanShutdown = true; CanStop = true; AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        Diag.Start(LogonScreen.LogDirectory, "service.log");
        supervisor = new Thread(Supervise) { IsBackground = true, Name = "Banko Share logon supervisor" };
        supervisor.Start();
    }

    protected override void OnSessionChange(SessionChangeDescription change)
    {
        Diag.Write($"Oturum olayı: {change.Reason} (oturum {change.SessionId})");
        wake.Set();
    }

    protected override void OnStop() => Halt();
    protected override void OnShutdown() => Halt();

    private void Halt()
    {
        stopping.Cancel();
        wake.Set();
        supervisor?.Join(TimeSpan.FromSeconds(15));
        Diag.WaitIdle(TimeSpan.FromSeconds(2));
    }

    private void Supervise()
    {
        Diag.Write($"Şifre ekranı hizmeti {LogonScreen.Version} başladı");
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, LogonScreen.StopEventName);
        HelperProcess? helper = null;
        var failures = 0; long nextLaunch = 0, missingSince = 0;
        while (!stopping.IsCancellationRequested)
        {
            if (Uninstalled(ref missingSince))
            {
                helper?.Stop(stop); helper?.Dispose();
                RemoveSelf();
                return;
            }
            var session = LogonNative.WTSGetActiveConsoleSessionId();
            if (helper != null && (helper.HasExited || helper.Session != session))
            {
                if (!helper.HasExited) helper.Stop(stop);
                var lived = helper.Lifetime;
                Diag.Write($"Yardımcı kapandı: oturum {helper.Session}, çıkış kodu {helper.ExitCode}, {lived.TotalSeconds:F0} sn çalıştı");
                // Quick exits back off, so a helper that cannot run does not spin.
                failures = lived < TimeSpan.FromSeconds(30) ? failures + 1 : 0;
                nextLaunch = Environment.TickCount64 + (failures == 0 ? 0 : Math.Min(60_000, 1000L << Math.Min(failures, 6)));
                helper.Dispose();
                helper = null;
            }
            if (helper == null && session is not (0 or uint.MaxValue) && Environment.TickCount64 >= nextLaunch)
            {
                stop.Reset();
                try
                {
                    helper = HelperProcess.Launch(session);
                    Diag.Write($"Yardımcı başlatıldı: oturum {session}");
                }
                catch (Win32Exception e)
                {
                    failures++;
                    nextLaunch = Environment.TickCount64 + Math.Min(60_000, 1000L << Math.Min(failures, 6));
                    Diag.Write($"Yardımcı başlatılamadı (oturum {session}): {e.Message}");
                }
            }
            wake.WaitOne(2000);
        }
        if (helper != null) { helper.Stop(stop); helper.Dispose(); }
        Diag.Write("Şifre ekranı hizmeti durdu");
    }

    /// <summary>
    /// Banko Share was uninstalled when its folder is gone while the folder above it is there (a profile that is
    /// not available yet hides both), for five minutes in a row.
    /// </summary>
    private static bool Uninstalled(ref long missingSince)
    {
        string? root;
        try { using var key = Registry.LocalMachine.OpenSubKey(LogonScreen.ParametersKey); root = key?.GetValue("InstallRoot") as string; }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        var parent = string.IsNullOrEmpty(root) ? null : Path.GetDirectoryName(root);
        if (parent == null || Directory.Exists(root) || !Directory.Exists(parent)) { missingSince = 0; return false; }
        if (missingSince == 0) { missingSince = Environment.TickCount64; return false; }
        return Environment.TickCount64 - missingSince > 5 * 60_000;
    }

    private void RemoveSelf()
    {
        Diag.Write("Banko Share bu bilgisayardan kaldırılmış; şifre ekranı hizmeti kendini kaldırıyor");
        try { LogonSetup.DeleteServiceEntry(); } catch (Win32Exception e) { Diag.Write("Hizmet silinemedi: " + e.Message); }
        try { IdentityStore.DeleteLogonKey(); } catch (CryptographicException) { }
        LogonSetup.DeleteAtReboot(LogonScreen.Root);
        Diag.WaitIdle(TimeSpan.FromSeconds(2));
        ThreadPool.QueueUserWorkItem(_ => Stop());
    }
}

/// <summary>The helper process: this program, as SYSTEM, in the given session, started on the secure desktop.</summary>
internal sealed class HelperProcess : IDisposable
{
    private readonly nint process;
    private readonly long started = Environment.TickCount64;
    private long exitedAt;
    public uint Session { get; }

    private HelperProcess(nint process, uint session) { this.process = process; Session = session; }

    public static HelperProcess Launch(uint session)
    {
        const uint Duplicate = 0x0002, Query = 0x0008, AssignPrimary = 0x0001, AdjustDefault = 0x0080, AdjustSessionId = 0x0100;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path.");
        if (!LogonNative.OpenProcessToken(LogonNative.GetCurrentProcess(), Duplicate | Query | AssignPrimary | AdjustDefault | AdjustSessionId, out var token))
            throw new Win32Exception();
        try
        {
            // SecurityImpersonation, TokenPrimary.
            if (!LogonNative.DuplicateTokenEx(token, 0x02000000, 0, 2, 1, out var primary)) throw new Win32Exception();
            try
            {
                if (!LogonNative.SetTokenInformation(primary, 12, ref session, sizeof(uint))) throw new Win32Exception(); // TokenSessionId
                var startup = new LogonNative.StartupInfo { Size = Marshal.SizeOf<LogonNative.StartupInfo>(), Desktop = @"winsta0\winlogon" };
                var commandLine = new StringBuilder($"\"{executable}\" {LogonScreen.HelperArgument}");
                if (!LogonNative.CreateProcessAsUser(primary, executable, commandLine, 0, 0, false, 0x08000000, 0, Path.GetDirectoryName(executable), ref startup, out var info))
                    throw new Win32Exception();
                LogonNative.CloseHandle(info.Thread);
                return new HelperProcess(info.Process, session);
            }
            finally { LogonNative.CloseHandle(primary); }
        }
        finally { LogonNative.CloseHandle(token); }
    }

    public bool HasExited
    {
        get
        {
            if (LogonNative.WaitForSingleObject(process, 0) != 0) return false;
            if (exitedAt == 0) exitedAt = Environment.TickCount64;
            return true;
        }
    }

    public TimeSpan Lifetime => TimeSpan.FromMilliseconds((exitedAt == 0 ? Environment.TickCount64 : exitedAt) - started);
    public int ExitCode => LogonNative.GetExitCodeProcess(process, out var code) ? (int)code : -1;

    /// <summary>Asks the helper to finish (it releases held keys and closes its links), then ends it if it does not.</summary>
    public void Stop(EventWaitHandle stop)
    {
        stop.Set();
        if (LogonNative.WaitForSingleObject(process, 5000) == 0) { _ = HasExited; return; }
        Diag.Write("Yardımcı zamanında kapanmadı; sonlandırılıyor");
        LogonNative.TerminateProcess(process, 1);
        LogonNative.WaitForSingleObject(process, 2000);
        _ = HasExited;
    }

    public void Dispose() => LogonNative.CloseHandle(process);
}

/// <summary>
/// The helper. Watches which desktop takes input. On the sign-in or lock screen it raises the active
/// signal, gives the app a moment to close its links, and then connects to this device's paired computers as this
/// device: their pointer and keys are applied here. Back on the normal desktop it closes everything and clears the
/// signal, so the app takes over again. It reads the user's Banko Share settings and never writes them.
/// </summary>
internal static class LogonHelper
{
    /// <summary>How long the app gets to close its links before the helper opens its own.</summary>
    private const int HandOverMs = 600;

    public static int Run()
    {
        Diag.Start(LogonScreen.LogDirectory, "helper.log");
        using var stop = OpenStop();
        using var active = HelperSignal.Create();
        Diag.Write($"Şifre ekranı yardımcısı {LogonScreen.Version} başladı (oturum {Process.GetCurrentProcess().SessionId})");
        // The app reads the certificate to tell paired computers about it, before the first lock.
        try { IdentityStore.LoadOrCreateLogon(LogonScreen.CertificatePath, null).Dispose(); }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException) { Diag.Write("Şifre ekranı anahtarı hazırlanamadı: " + e.Message); }
        HelperSession? session = null;
        string? desktop = null, problem = null;
        long armedAt = 0, retryAt = 0;
        var sessionId = (uint)Process.GetCurrentProcess().SessionId;
        try
        {
            while (!stop.WaitOne(250))
            {
                var now = Environment.TickCount64;
                var current = InputDesktop.Name();
                var signIn = current != null && !current.Equals("Default", StringComparison.OrdinalIgnoreCase) && SignInScreen(sessionId);
                var seen = $"{current ?? "okunamadı"}{(signIn ? ", şifre ekranı" : "")}";
                if (seen != desktop) { Diag.Write("Giriş masaüstü: " + seen); desktop = seen; }
                if (!signIn)
                {
                    if (session != null) { session.Dispose(); session = null; Diag.Write("Masaüstüne dönüldü: bağlantılar uygulamaya bırakıldı"); }
                    active.Reset();
                    armedAt = 0; retryAt = 0;
                    continue;
                }
                if (session == null && armedAt == 0 && now >= retryAt)
                {
                    session = HelperSession.Create(out var why);
                    if (session == null)
                    {
                        if (why != problem) Diag.Write("Şifre ekranında bağlanılamıyor: " + why);
                        problem = why;
                        retryAt = now + 10_000;
                        continue;
                    }
                    problem = null;
                    active.Set();
                    armedAt = now;
                }
                if (session is { Started: false } && now - armedAt >= HandOverMs) session.Start();
            }
        }
        finally
        {
            session?.Dispose();
            active.Reset();
            Diag.Write("Şifre ekranı yardımcısı kapandı");
            Diag.WaitIdle(TimeSpan.FromSeconds(2));
        }
        return 0;
    }

    /// <summary>
    /// Nobody is signed in to the session, or it is locked. A UAC prompt uses the same secure desktop in an
    /// unlocked session, and it is left alone on purpose: anything able to add a paired computer to the user's
    /// settings could otherwise approve its own elevation from there. Unknown state counts as not the sign-in screen.
    /// </summary>
    private static bool SignInScreen(uint session)
    {
        const int SessionInfoEx = 25, Locked = 0;
        if (!LogonNative.WTSQuerySessionInformation(0, session, SessionInfoEx, out var buffer, out var size)) return false;
        try
        {
            // WTSINFOEXW: Level, then (8-aligned) WTSINFOEX_LEVEL1_W: SessionId, SessionState, SessionFlags, WinStationName[33], UserName[21]…
            if (size < 128 || Marshal.ReadInt32(buffer) != 1) return false;
            var flags = Marshal.ReadInt32(buffer, 16);
            var user = Marshal.PtrToStringUni(buffer + 86, 20).TrimEnd('\0');
            return user.Length == 0 || flags == Locked;
        }
        finally { LogonNative.WTSFreeMemory(buffer); }
    }

    private static EventWaitHandle OpenStop()
    {
        try { return EventWaitHandle.OpenExisting(LogonScreen.StopEventName); }
        catch (WaitHandleCannotBeOpenedException) { return new EventWaitHandle(false, EventResetMode.ManualReset); } // started by hand
    }
}

/// <summary>One stretch on the secure desktop: links to the paired computers and input applied for them.</summary>
internal sealed class HelperSession : IDisposable
{
    private readonly DeviceIdentity identity;
    private readonly PeerNetwork network;
    private readonly ShareController controller;

    private HelperSession(DeviceIdentity identity, PeerNetwork network, ShareController controller)
    {
        this.identity = identity; this.network = network; this.controller = controller;
    }

    public bool Started { get; private set; }

    public static HelperSession? Create(out string? problem)
    {
        problem = null;
        string? settings;
        try { using var key = Registry.LocalMachine.OpenSubKey(LogonScreen.ParametersKey); settings = key?.GetValue("SettingsDirectory") as string; }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { settings = null; }
        if (string.IsNullOrEmpty(settings) || !Directory.Exists(settings)) { problem = "Banko Share ayarları bulunamadı"; return null; }
        var deviceId = IdentityStore.ReadId(Path.Combine(settings, IdentityStore.CertificateFile));
        if (deviceId == null) { problem = "bu bilgisayarın Banko Share kimliği yok; uygulamayı bir kez açın"; return null; }
        // The user's settings, read only: pairings, layout and options stay the app's to change.
        var store = new SettingsStore(settings) { ReadOnly = true };
        var directory = new PeerDirectory(store);
        if (directory.All.Count == 0) { problem = "eşleşmiş bilgisayar yok"; return null; }
        DeviceIdentity identity;
        MonitorInfo[] monitors;
        try
        {
            monitors = Monitors.Get();
            identity = IdentityStore.LoadOrCreateLogon(LogonScreen.CertificatePath, deviceId);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problem = e.Message;
            return null;
        }
        var name = PeerDirectory.CleanName(Environment.MachineName);
        // Outbound only: the app owns the listening port, and the paired computer's app accepts us.
        var network = new PeerNetwork(identity, name, directory, PeerNetwork.DefaultPort, discovery: false, listen: false);
        var controller = new ShareController(new DeviceInfo(identity.Id, name, monitors), network, new InputDesktopPlatform(new WindowsPlatform(monitors)),
            store, LogonScreen.Version) { OnLogonScreen = true };
        return new HelperSession(identity, network, controller);
    }

    public void Start()
    {
        Started = true;
        network.Start();
        Diag.Write($"Şifre ekranı açık: {string.Join(", ", network.Directory.All.Select(p => p.Name))} bilgisayarına bağlanılıyor");
    }

    public void Dispose()
    {
        // Held keys are released and the pointer handed back before the links close.
        controller.Dispose();
        network.Dispose();
        identity.Dispose();
    }
}

/// <summary>
/// Input for the secure desktop. A thread applies input only to its own desktop, and the input desktop changes
/// (sign-in, lock, back), so the injecting thread follows it before each call. The thread has no windows or
/// hooks, which switching requires. No pointer hiding: the helper never takes this machine's own pointer.
/// </summary>
internal sealed class InputDesktopPlatform(WindowsPlatform inner) : IInputPlatform
{
    [ThreadStatic] private static nint attached;
    [ThreadStatic] private static string? attachedName;
    [ThreadStatic] private static long checkedAt;

    public void WarpCursor(PointI point) { Follow(); inner.WarpCursor(point); }
    public void SetCursorHidden(bool hidden, PointI anchor) { }
    public void Inject(InputEvent input) { Follow(); inner.Inject(input); }
    public void MaskMenuKey() { Follow(); inner.MaskMenuKey(); }

    private static void Follow()
    {
        var now = Environment.TickCount64;
        if (attached != 0 && now - checkedAt < 100) return;
        checkedAt = now;
        var desktop = LogonNative.OpenInputDesktop(0, false, 0x10000000); // GENERIC_ALL
        if (desktop == 0) return;
        var name = InputDesktop.NameOf(desktop);
        if (attached != 0 && name == attachedName) { LogonNative.CloseDesktop(desktop); return; }
        if (!LogonNative.SetThreadDesktop(desktop)) { LogonNative.CloseDesktop(desktop); return; }
        if (attached != 0) LogonNative.CloseDesktop(attached);
        attached = desktop; attachedName = name;
    }
}

internal static class InputDesktop
{
    /// <summary>"Default", "Winlogon" (sign-in, lock, UAC, Ctrl+Alt+Del)… Null when it cannot be opened, as for the app on a secure desktop.</summary>
    public static string? Name()
    {
        var desktop = LogonNative.OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS
        if (desktop == 0) return null;
        try { return NameOf(desktop); }
        finally { LogonNative.CloseDesktop(desktop); }
    }

    public static string? NameOf(nint desktop)
    {
        var buffer = new char[256];
        if (!LogonNative.GetUserObjectInformation(desktop, 2, buffer, buffer.Length * sizeof(char), out _)) return null; // UOI_NAME
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}

/// <summary>The helper's "I speak for this device" event: SYSTEM owns it; signed-in users may open and wait on it.</summary>
internal static class HelperSignal
{
    public static EventWaitHandle Create()
    {
        if (!LogonNative.ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;SY)(A;;0x00100002;;;AU)", 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            var attributes = new LogonNative.SecurityAttributes { Length = Marshal.SizeOf<LogonNative.SecurityAttributes>(), Descriptor = descriptor };
            var handle = LogonNative.CreateEvent(ref attributes, true, false, LogonScreen.ActiveEventName);
            if (handle == 0) throw new Win32Exception();
            var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
            signal.SafeWaitHandle.Dispose();
            signal.SafeWaitHandle = new SafeWaitHandle(handle, true);
            return signal;
        }
        finally { LogonNative.LocalFree(descriptor); }
    }
}

internal static class LogonNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, CountCharsX, CountCharsY, FillAttribute, Flags;
        public short ShowWindow, Reserved2Size; public nint Reserved2, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation { public nint Process, Thread; public int ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] internal struct SecurityAttributes { public int Length; public nint Descriptor; public bool Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ServiceDescription { public string? Description; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FailureActions { public int ResetPeriod; public string? RebootMessage, Command; public int ActionCount; public nint Actions; }
    [StructLayout(LayoutKind.Sequential)] internal struct ScAction { public int Type, Delay; }

    [DllImport("kernel32.dll")] internal static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("kernel32.dll")] internal static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(nint process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetExitCodeProcess(nint process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern bool MoveFileEx(string existing, string? replacement, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateEvent(ref SecurityAttributes attributes, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll")] internal static extern nint LocalFree(nint memory);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool DuplicateTokenEx(nint token, uint access, nint attributes, int impersonationLevel, int tokenType, out nint duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool SetTokenInformation(nint token, int infoClass, ref uint info, int length);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessAsUser(nint token, string? application, StringBuilder commandLine, nint processAttributes, nint threadAttributes,
        bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfo startup, out ProcessInformation info);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateService(nint manager, string name, string display, uint access, uint type, uint start, uint error, string path,
        string? group, nint tag, string? dependencies, string? account, string? password);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool ChangeServiceConfig(nint service, uint type, uint start, uint error, string? path, string? group, nint tag,
        string? dependencies, string? account, string? password, string? display);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern bool ChangeServiceConfig2(nint service, uint level, ref ServiceDescription info);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern bool ChangeServiceConfig2(nint service, uint level, ref FailureActions info);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool DeleteService(nint service);
    [DllImport("advapi32.dll")] internal static extern bool CloseServiceHandle(nint handle);
    // Unicode: the ANSI form returns WTSINFOEXA, whose name fields are half as wide.
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool WTSQuerySessionInformation(nint server, uint session, int infoClass, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")] internal static extern void WTSFreeMemory(nint memory);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] internal static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetThreadDesktop(nint desktop);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool GetUserObjectInformation(nint handle, int index, [Out] char[] info, int length, out int needed);
}
