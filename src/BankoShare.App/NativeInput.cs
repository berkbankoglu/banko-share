using System.ComponentModel;
using System.Runtime.InteropServices;
using BankoShare.Core;

namespace BankoShare.App;

internal static class Native
{
    internal delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHook { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyHook { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct Union
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Union Value; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
}

internal sealed class InputInjector
{
    private readonly HashSet<(int Scan, int Extended)> keys = [];
    private readonly HashSet<int> buttons = [];
    public void Apply(InputEvent input)
    {
        if (input.Kind == EventKind.ReleaseAll) { ReleaseAll(); return; }
        if (input.Kind == EventKind.Heartbeat) return;
        var native = new Native.Input();
        switch (input.Kind)
        {
            case EventKind.Move:
                native.Value.Mouse = new() { X = input.A, Y = input.B, Flags = 1 }; break;
            case EventKind.Wheel:
                native.Value.Mouse = new() { Data = unchecked((uint)input.A), Flags = input.B == 0 ? 0x800u : 0x1000u }; break;
            case EventKind.Button:
                uint flag = input.A switch { 1 => 2u, 2 => 8u, 3 => 32u, _ => 128u };
                native.Value.Mouse = new() { Flags = input.B == 1 ? flag : flag * 2, Data = input.A >= 4 ? (uint)(input.A - 3) : 0 }; break;
            case EventKind.Key:
                native.Type = 1;
                native.Value.Key = new() { Scan = (ushort)input.A, Flags = 8u | (uint)input.C | (input.B == 0 ? 2u : 0) }; break;
        }
        if (Native.SendInput(1, [native], Marshal.SizeOf<Native.Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Giriş uygulanamadı. Hedef uygulamanın yetki düzeyini kontrol edin.");
        if (input.Kind == EventKind.Key)
        { if (input.B == 1) keys.Add((input.A, input.C)); else keys.Remove((input.A, input.C)); }
        if (input.Kind == EventKind.Button)
        { if (input.B == 1) buttons.Add(input.A); else buttons.Remove(input.A); }
    }
    public void ReleaseAll()
    {
        Exception? failure = null;
        foreach (var key in keys.ToArray())
            try { Apply(new(EventKind.Key, key.Scan, 0, key.Extended)); } catch (Exception e) { failure = e; }
        foreach (var button in buttons.ToArray())
            try { Apply(new(EventKind.Button, button, 0)); } catch (Exception e) { failure = e; }
        if (failure != null) throw failure;
    }
}

// The dispatcher owns the hook message loop. Callbacks only enqueue; they never wait for the network.
internal sealed class InputCapture : IDisposable
{
    private readonly Native.HookProc mouseProc, keyProc;
    private readonly Func<InputEvent, bool> send;
    private readonly Action toggle;
    private nint mouseHook, keyHook;
    private Native.Point anchor;
    private bool f12Down;
    public bool Active { get; private set; }
    public InputCapture(Func<InputEvent, bool> send, Action toggle)
    {
        this.send = send; this.toggle = toggle;
        mouseProc = Mouse; keyProc = Key;
        mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
        keyHook = Native.SetWindowsHookEx(13, keyProc, Native.GetModuleHandle(null), 0);
        if (mouseHook == 0 || keyHook == 0) { var error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error); }
    }
    public void SetActive(bool active)
    {
        Active = active;
        if (active) Native.GetCursorPos(out anchor);
        else send(new(EventKind.ReleaseAll));
    }
    private nint Mouse(int code, nint message, nint data)
    {
        if (code < 0 || !Active) return Native.CallNextHookEx(mouseHook, code, message, data);
        var e = Marshal.PtrToStructure<Native.MouseHook>(data);
        if ((e.Flags & 1) != 0) return Native.CallNextHookEx(mouseHook, code, message, data);
        InputEvent? input = (int)message switch
        {
            0x200 => new(EventKind.Move, Math.Clamp(e.Point.X - anchor.X, -32767, 32767), Math.Clamp(e.Point.Y - anchor.Y, -32767, 32767)),
            0x201 => new(EventKind.Button, 1, 1), 0x202 => new(EventKind.Button, 1, 0),
            0x204 => new(EventKind.Button, 2, 1), 0x205 => new(EventKind.Button, 2, 0),
            0x207 => new(EventKind.Button, 3, 1), 0x208 => new(EventKind.Button, 3, 0),
            0x20B => new(EventKind.Button, (int)(e.Data >> 16) + 3, 1),
            0x20C => new(EventKind.Button, (int)(e.Data >> 16) + 3, 0),
            0x20A => new(EventKind.Wheel, unchecked((short)(e.Data >> 16))),
            0x20E => new(EventKind.Wheel, unchecked((short)(e.Data >> 16)), 1), _ => null
        };
        if (input.HasValue && !send(input.Value)) Active = false;
        return Active ? 1 : Native.CallNextHookEx(mouseHook, code, message, data);
    }
    private nint Key(int code, nint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(keyHook, code, message, data);
        var e = Marshal.PtrToStructure<Native.KeyHook>(data);
        if ((e.Flags & 0x10) != 0) return Native.CallNextHookEx(keyHook, code, message, data);
        bool down = (int)message is 0x100 or 0x104;
        if (e.Vk == 0x7B)
        {
            if (down && !f12Down) toggle();
            f12Down = down; return 1;
        }
        if (!Active || e.Scan is 0 or > 255) return Native.CallNextHookEx(keyHook, code, message, data);
        if (!send(new(EventKind.Key, (int)e.Scan, down ? 1 : 0, (int)(e.Flags & 1)))) Active = false;
        return Active ? 1 : Native.CallNextHookEx(keyHook, code, message, data);
    }
    public void Dispose()
    {
        Active = false;
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
        mouseHook = keyHook = 0;
    }
}
