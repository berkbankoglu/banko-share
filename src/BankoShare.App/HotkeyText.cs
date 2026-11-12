using System.Globalization;
using System.Windows.Input;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>Display names for recorded hotkeys, e.g. "Ctrl+Alt+S" or "Ctrl+Shift+Ö".</summary>
internal static class HotkeyText
{
    private static readonly (KeyMods Mod, string Name)[] Names = [(KeyMods.Ctrl, "Ctrl"), (KeyMods.Alt, "Alt"), (KeyMods.Shift, "Shift")];

    public static string Describe(Hotkey? hotkey) => hotkey == null ? "Yok" : Mods(hotkey.Mods) + "+" + KeyName(hotkey.Vk);

    public static string Mods(KeyMods mods) => string.Join("+", Names.Where(n => mods.HasFlag(n.Mod)).Select(n => n.Name));

    public static string KeyName(int vk)
    {
        var key = KeyInterop.KeyFromVirtualKey(vk);
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture);
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return "Num " + (int)(key - Key.NumPad0);
        // Punctuation keys depend on the keyboard layout: show the character this layout prints.
        var ch = (char)(Native.MapVirtualKey((uint)vk, 2) & 0x7FFF);
        if (key.ToString().StartsWith("Oem", StringComparison.Ordinal) && ch > ' ') return char.ToUpper(ch, CultureInfo.CurrentCulture).ToString();
        return key switch
        {
            Key.Space => "Boşluk",
            Key.Enter => "Enter",
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Left => "Sol ok",
            Key.Right => "Sağ ok",
            Key.Up => "Yukarı ok",
            Key.Down => "Aşağı ok",
            Key.Escape => "Esc",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.None => $"0x{vk:X2}",
            _ => key.ToString()
        };
    }
}
