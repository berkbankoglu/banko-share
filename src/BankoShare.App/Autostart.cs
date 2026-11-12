using Microsoft.Win32;

namespace BankoShare.App;

/// <summary>One-time choices: whether the startup default was applied and whether the tray hint was shown.</summary>
internal sealed record AppPreferences(bool AutostartConfigured = false, bool TrayNoticeShown = false)
{
    public const string FileName = "app.json";
}

/// <summary>Dark caption bar matching the Noir window (Windows 10 20H1 and later; ignored elsewhere).</summary>
internal static class DarkTitleBar
{
    public static void Apply(System.Windows.Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var on = 1;
        DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        var caption = 0x001A1A1A;                               // COLORREF 0x00BBGGRR: #1A1A1A
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR, Windows 11
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

/// <summary>Starts Banko Share in the notification area when the user signs in to Windows (per-user Run key).</summary>
internal static class Autostart
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BankoShare";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    /// <summary>
    /// The installed path (…\BankoShare\current\BankoShare.App.exe) stays the same across updates.
    /// Only installed copies should call this; a development build's path would go stale.
    /// </summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        else key.DeleteValue(ValueName, false);
    }
}
