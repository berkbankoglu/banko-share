using System.ComponentModel;
using System.Runtime.InteropServices;
using BankoShare.Core;

namespace BankoShare.App;

internal static class Native
{
    internal delegate nint HookProc(int code, nint message, nint data);
    internal delegate nint WndProc(nint hwnd, uint message, nint w, nint l);
    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHook { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyHook { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Union Value; }
    [StructLayout(LayoutKind.Sequential)] internal struct Msg { public nint Hwnd; public uint Message; public nint W, L; public uint Time; public Point Pt; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassEx { public uint Size, Style; public WndProc Proc; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string ClassName; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx { public uint Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }

    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WndClassEx cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern nint DefWindowProc(nint hwnd, uint message, nint w, nint l);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] internal static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, char[] name, int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint type);
}

internal static class Monitors
{
    /// <summary>Monitor bounds in physical pixels (the process is per-monitor DPI aware), left to right.</summary>
    public static MonitorInfo[] Get()
    {
        List<MonitorInfo> list = [];
        Native.EnumDisplayMonitors(0, 0, (nint handle, nint _, ref Native.Rect _, nint _) =>
        {
            var info = new Native.MonitorInfoEx { Size = (uint)Marshal.SizeOf<Native.MonitorInfoEx>() };
            if (!Native.GetMonitorInfo(handle, ref info)) return true;
            var dpi = Native.GetDpiForMonitor(handle, 0, out var x, out _) == 0 ? (int)x : 96;
            var r = info.Monitor;
            var m = new MonitorInfo(new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), (info.Flags & 1) != 0, Math.Clamp(dpi, 48, 960));
            // Skip anything a peer would reject (e.g. a zero-sized virtual display).
            if (m.Bounds.Width is >= 64 and <= 16384 && m.Bounds.Height is >= 64 and <= 16384 && list.Count < DeviceInfo.MaxMonitors) list.Add(m);
            return true;
        }, 0);
        if (list.Count == 0) throw new InvalidOperationException("Monitör bulunamadı.");
        if (!list.Any(m => m.Primary)) list[0] = list[0] with { Primary = true };
        return [.. list.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)];
    }
}

/// <summary>SendInput, SetCursorPos and the cursor-hiding window. Safe to call from any thread.</summary>
internal sealed class WindowsPlatform(MonitorInfo[] monitors) : IInputPlatform
{
    private const uint MouseMove = 0x0001, MouseAbsolute = 0x8000, MouseVirtualDesk = 0x4000;
    private RectI screen = RectI.Union(monitors.Select(m => m.Bounds));
    public nint HiderWindow { get; set; }

    public void UpdateScreen(MonitorInfo[] current) => screen = RectI.Union(current.Select(m => m.Bounds));

    public void WarpCursor(PointI point) => Native.SetCursorPos(point.X, point.Y);

    public void SetCursorHidden(bool hidden, PointI anchor)
    {
        if (HiderWindow == 0) return;
        if (hidden) Native.SetWindowPos(HiderWindow, -1, anchor.X - 32, anchor.Y - 32, 64, 64, 0x0010 | 0x0040 | 0x4000);
        else Native.ShowWindowAsync(HiderWindow, 0);
    }

    public void Inject(InputEvent input)
    {
        var native = new Native.Input();
        switch (input.Kind)
        {
            case EventKind.Move:
                var s = screen;
                native.Value.Mouse = new() { X = Normalize(input.A - s.Left, s.Width), Y = Normalize(input.B - s.Top, s.Height), Flags = MouseMove | MouseAbsolute | MouseVirtualDesk };
                break;
            case EventKind.Wheel:
                native.Value.Mouse = new() { Data = unchecked((uint)input.A), Flags = input.B == 0 ? 0x800u : 0x1000u };
                break;
            case EventKind.Button:
                uint flag = input.A switch { 1 => 0x2u, 2 => 0x8u, 3 => 0x20u, _ => 0x80u };
                native.Value.Mouse = new() { Flags = input.B == 1 ? flag : flag * 2, Data = input.A >= 4 ? (uint)(input.A - 3) : 0 };
                break;
            case EventKind.Key:
                native.Type = 1;
                native.Value.Key = new() { Scan = (ushort)input.A, Flags = 0x8u | (uint)input.C | (input.B == 0 ? 0x2u : 0) };
                break;
            default: return;
        }
        if (Native.SendInput(1, [native], Marshal.SizeOf<Native.Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Giriş uygulanamadı.");
    }

    /// <summary>
    /// A tap of the unassigned virtual key 0xE8 between Alt down and Alt up, so Windows does not treat the
    /// release as a lone Alt press and focus the menu bar.
    /// </summary>
    public void MaskMenuKey()
    {
        var down = new Native.Input { Type = 1 };
        down.Value.Key = new() { Vk = 0xE8 };
        var up = down;
        up.Value.Key.Flags = 0x2;
        Native.SendInput(2, [down, up], Marshal.SizeOf<Native.Input>());
    }

    /// <summary>Aims at the centre of the target pixel so rounding never lands on its neighbour.</summary>
    internal static int Normalize(int offset, int size) => (int)Math.Clamp((2L * offset + 1) * 65536 / (2L * Math.Max(size, 1)), 0, 65535);
}

/// <summary>
/// Foreground-window and key-state queries for the switch engine. Runs on the input thread, at most once per
/// push against an edge, so it stays a handful of quick user32 calls.
/// </summary>
internal sealed class WindowsEnvironment(GameGuard games) : ISwitchEnvironment
{
    private const uint RootWindow = 2; // GA_ROOT
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];
    private readonly char[] buffer = new char[1024];

    public bool IsKeyDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    public bool GameInFront() => games.IsGameWindow(Native.GetForegroundWindow());

    public bool GameUnderCursor() =>
        Native.GetCursorPos(out var p) && games.IsGameWindow(Native.GetAncestor(Native.WindowFromPoint(p), RootWindow));

    public BlockReason CheckForeground(RectI from, SwitchOptions options)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0 || hwnd == Native.GetShellWindow() || IsShell(hwnd)) return BlockReason.None;
        if (options.BlockedPrograms.Length > 0 && ProcessName(hwnd) is { } name && options.BlockedPrograms.Contains(name))
            return BlockReason.Program;
        if (!options.BlockFullscreen && !options.BlockMaximized) return BlockReason.None;
        // Only a window on the monitor being left counts: a video playing full screen elsewhere does not block.
        var info = new Native.MonitorInfoEx { Size = (uint)Marshal.SizeOf<Native.MonitorInfoEx>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2), ref info)) return BlockReason.None;
        var m = info.Monitor;
        if (new RectI(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top) != from) return BlockReason.None;
        var zoomed = Native.IsZoomed(hwnd);
        if (options.BlockMaximized && zoomed) return BlockReason.Maximized;
        if (options.BlockFullscreen && !zoomed && Native.GetWindowRect(hwnd, out var r)
            && r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom)
            return BlockReason.Fullscreen;
        return BlockReason.None;
    }

    private bool IsShell(nint hwnd)
    {
        var length = Native.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 && ShellClasses.Contains(new string(buffer, 0, length));
    }

    /// <summary>
    /// Executable name of the window's process, from the system process snapshot. No handle to the process is
    /// opened: anti-cheat drivers watch for other programs opening game processes.
    /// </summary>
    private string? ProcessName(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : games.NameOf((int)pid);
    }
}

/// <summary>
/// Dedicated input thread: low-level mouse and keyboard hooks plus the cursor hider, all on one message loop.
/// Windows silently drops a hook whose callback stalls, so callbacks only do short, lock-protected arithmetic
/// and queue network writes; they never wait on the UI or the network.
/// </summary>
internal sealed class InputHost : IDisposable
{
    private const uint InjectedMouse = 0x1 | 0x2, InjectedKey = 0x10;
    private const string HiderClass = "BankoShare.CursorHider";
    private readonly ShareController controller;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private Native.HookProc? mouseProc, keyProc;
    private Native.WndProc? hiderProc;
    private Exception? startError;
    private uint threadId;
    private readonly WindowsPlatform platform;
    private readonly Timer watchdog;
    private const uint WmReinstallHooks = 0x8001; // WM_APP + 1, posted to the input thread
    private long lastHookEvent = Environment.TickCount64, lastReinstall;
    private Native.Point lastCursor;

    public InputHost(ShareController controller, WindowsPlatform platform)
    {
        this.controller = controller; this.platform = platform;
        thread = new Thread(() => Run(platform)) { IsBackground = true, Name = "Banko Share input", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait();
        if (startError != null) throw startError;
        Diag.Write("Fare ve klavye kancaları kuruldu");
        watchdog = new Timer(_ => Watch(), null, 1000, 1000);
    }

    private void Run(WindowsPlatform platform)
    {
        nint mouseHook = 0, keyHook = 0, hider = 0;
        try
        {
            threadId = Native.GetCurrentThreadId();
            var module = Native.GetModuleHandle(null);
            hider = CreateHider(module);
            platform.HiderWindow = hider;
            mouseProc = OnMouse; keyProc = OnKey;
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, module, 0);
            keyHook = Native.SetWindowsHookEx(13, keyProc, module, 0);
            if (mouseHook == 0 || keyHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Fare/klavye kancası kurulamadı.");
        }
        catch (Exception e) { startError = e; }
        ready.Set();
        if (startError == null)
            while (Native.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.Hwnd == 0 && msg.Message == WmReinstallHooks)
                {
                    // Windows removes a low-level hook it once had to time out, without telling us. Put both back.
                    if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
                    if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
                    var module = Native.GetModuleHandle(null);
                    mouseHook = Native.SetWindowsHookEx(14, mouseProc!, module, 0);
                    keyHook = Native.SetWindowsHookEx(13, keyProc!, module, 0);
                    Diag.Write(mouseHook != 0 && keyHook != 0 ? "Kancalar yeniden kuruldu" : "Kancalar yeniden kurulamadı");
                    continue;
                }
                Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg);
            }
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
        platform.HiderWindow = 0;
        if (hider != 0) Native.DestroyWindow(hider);
    }

    /// <summary>
    /// A 64×64, almost fully transparent, never-activated topmost window that clears the cursor shape.
    /// The parked local cursor sits on it while a peer owns the pointer, so it is invisible.
    /// </summary>
    private nint CreateHider(nint module)
    {
        hiderProc = (hwnd, message, w, l) =>
        {
            if (message == 0x0020) { Native.SetCursor(0); return 1; } // WM_SETCURSOR: no cursor shape
            if (message == 0x0021) return 3;                          // WM_MOUSEACTIVATE: MA_NOACTIVATE
            return Native.DefWindowProc(hwnd, message, w, l);
        };
        var cls = new Native.WndClassEx { Size = (uint)Marshal.SizeOf<Native.WndClassEx>(), Proc = hiderProc, Instance = module, ClassName = HiderClass };
        Native.RegisterClassEx(ref cls);
        var hwnd = Native.CreateWindowEx(0x8 | 0x80 | 0x08000000 | 0x80000, HiderClass, "", 0x80000000, 0, 0, 64, 64, 0, 0, module, 0);
        if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "İmleç penceresi oluşturulamadı.");
        Native.SetLayeredWindowAttributes(hwnd, 0, 1, 0x2);
        return hwnd;
    }

    /// <summary>
    /// Once a second, off the input thread: if the cursor moved but no mouse event reached our hook for a while,
    /// the hook is gone (Windows drops hooks that time out) and is reinstalled. A cursor-hiding window left visible
    /// while the pointer is here is hidden, so the cursor can never stay invisible.
    /// </summary>
    private void Watch()
    {
        if (!Native.GetCursorPos(out var cursor)) return;
        var moved = cursor.X != lastCursor.X || cursor.Y != lastCursor.Y;
        lastCursor = cursor;
        var now = Environment.TickCount64;
        if (moved && now - Interlocked.Read(ref lastHookEvent) > 1500 && now - lastReinstall > 10_000 && threadId != 0)
        {
            lastReinstall = now;
            Diag.Write("İmleç hareket ediyor ama fare kancasına olay gelmiyor; kancalar yeniden kuruluyor");
            Native.PostThreadMessage(threadId, WmReinstallHooks, 0, 0);
        }
        var hider = platform.HiderWindow;
        if (hider != 0 && Native.IsWindowVisible(hider) && controller.ControllingPeer == null)
        {
            Diag.Write("Görünür kalmış imleç gizleme penceresi kapatıldı");
            Native.ShowWindowAsync(hider, 0);
        }
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        Interlocked.Exchange(ref lastHookEvent, Environment.TickCount64);
        if (code >= 0)
        {
            try
            {
                var e = Marshal.PtrToStructure<Native.MouseHook>(data);
                if ((e.Flags & InjectedMouse) == 0 && Dispatch((int)message, e) == HookResult.Block) return 1;
            }
            catch (Exception) { /* An exception must never cross into the OS hook chain. */ }
        }
        return Native.CallNextHookEx(0, code, message, data);
    }

    private HookResult Dispatch(int message, Native.MouseHook e)
    {
        int xButton = (int)(e.Data >> 16) + 3;
        return message switch
        {
            0x200 => controller.OnMouseMove(new(e.Point.X, e.Point.Y), Cursor(), ButtonsHeld(), e.Time),
            0x201 or 0x202 => controller.OnMouseButton(1, message == 0x201),
            0x204 or 0x205 => controller.OnMouseButton(2, message == 0x204),
            0x207 or 0x208 => controller.OnMouseButton(3, message == 0x207),
            0x20B or 0x20C when xButton is 4 or 5 => controller.OnMouseButton(xButton, message == 0x20B),
            0x20A => controller.OnMouseWheel(unchecked((short)(e.Data >> 16)), false),
            0x20E => controller.OnMouseWheel(unchecked((short)(e.Data >> 16)), true),
            _ => HookResult.Pass
        };
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                var e = Marshal.PtrToStructure<Native.KeyHook>(data);
                bool down = (int)message is 0x100 or 0x104;
                if ((e.Flags & InjectedKey) == 0 && controller.OnKey((int)e.Vk, (int)e.Scan, (e.Flags & 1) != 0, down, e.Time) == HookResult.Block) return 1;
            }
            catch (Exception) { }
        }
        return Native.CallNextHookEx(0, code, message, data);
    }

    private static PointI Cursor() => Native.GetCursorPos(out var p) ? new(p.X, p.Y) : default;

    private static bool ButtonsHeld()
    {
        foreach (var vk in (ReadOnlySpan<int>)[0x01, 0x02, 0x04, 0x05, 0x06])
            if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    public void Dispose()
    {
        watchdog.Dispose();
        if (threadId != 0) Native.PostThreadMessage(threadId, 0x0012, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }
}
