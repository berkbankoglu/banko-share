using System.Numerics;
using System.Text.Json.Serialization;

namespace BankoShare.Core;

[Flags] public enum KeyMods { None = 0, Ctrl = 1, Alt = 2, Shift = 4 }

/// <summary>A key held to allow switching.</summary>
public enum SwitchKey { None, Shift, Ctrl, Alt }

public enum DoubleTapMode { Off, OverrideBlocks, Always }

/// <summary>
/// Why a push against an edge did not switch. Game and Confined belong to the game lock and can never be
/// overridden: Confined is the program in front holding the pointer inside its window, as games do while playing.
/// </summary>
public enum BlockReason { None, Fullscreen, Maximized, Program, Corner, Key, DoubleTap, Game, Confined }

/// <summary>When the game lock holds sharing on this machine.</summary>
public enum GameLockMode
{
    /// <summary>Only while the game's window is in front: Alt+Tab out of the game and switching works again.</summary>
    InFront,
    /// <summary>For as long as the game runs, in front or not.</summary>
    WhileRunning
}

public enum HotkeyAction { ToggleSwitching, NextDevice, ReturnHome, JumpToMonitor }

/// <summary>
/// Modifiers plus a virtual key. Alt means the left Alt key only: on layouts with AltGr (Turkish Q among them)
/// the right Alt types characters and arrives together with a synthetic left Ctrl.
/// </summary>
public sealed record Hotkey(KeyMods Mods, int Vk)
{
    /// <summary>At least two modifiers, so a hotkey never takes over a plain application shortcut.</summary>
    [JsonIgnore]
    public bool IsValid => Vk is > 0 and < 0xFF && !VirtualKeys.IsModifier(Vk) && (Mods & ~VirtualKeys.AllMods) == 0
        && BitOperations.PopCount((uint)Mods) >= 2;
}

public static class VirtualKeys
{
    public const int LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5;
    public const KeyMods AllMods = KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift;

    public static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= LShift and <= RAlt;

    /// <summary>Top-row and numeric-keypad digits 1–9 as a zero-based index, otherwise -1.</summary>
    public static int DigitIndex(int vk) => vk is >= 0x31 and <= 0x39 ? vk - 0x31 : vk is >= 0x61 and <= 0x69 ? vk - 0x61 : -1;

    /// <summary>Right Alt is AltGr, and it brings a synthetic left Ctrl, so neither counts while it is down.</summary>
    public static KeyMods ModsFrom(Func<int, bool> isDown)
    {
        var mods = KeyMods.None;
        var altGr = isDown(RAlt);
        if (!altGr && (isDown(LCtrl) || isDown(RCtrl))) mods |= KeyMods.Ctrl;
        if (isDown(LAlt)) mods |= KeyMods.Alt;
        if (isDown(LShift) || isDown(RShift)) mods |= KeyMods.Shift;
        return mods;
    }
}

/// <summary>
/// How this machine switches when its own mouse drives the pointer. Each machine keeps its own options;
/// they are not synchronised. New options default to the 0.3 behaviour.
/// </summary>
public sealed record SwitchOptions
{
    public const int MaxPrograms = 64;

    /// <summary>Edge switching. Hotkeys still work when it is off. Name kept for 0.3 settings files.</summary>
    public bool SwitchingEnabled { get; init; } = true;
    /// <summary>A whole edge maps proportionally onto the whole facing edge.</summary>
    public bool EqualizeEdges { get; init; }
    /// <summary>Leaving the outer edge of the arrangement enters from the far side.</summary>
    public bool Wrap { get; init; }
    public bool BlockFullscreen { get; init; }
    public bool BlockMaximized { get; init; }
    public bool BlockCorners { get; init; }
    public SwitchKey RequiredKey { get; init; }
    public DoubleTapMode DoubleTap { get; init; }
    /// <summary>Lower-case executable names without extension.</summary>
    public string[] BlockedPrograms { get; init; } = [];
    public Hotkey? ToggleHotkey { get; init; }
    public Hotkey? NextDeviceHotkey { get; init; }
    public Hotkey? HomeHotkey { get; init; }
    /// <summary>Modifiers that, with a digit 1–9, jump to monitor A–I. None turns it off.</summary>
    public KeyMods JumpMods { get; init; }
    /// <summary>
    /// The game lock. While it holds (see <see cref="GameLock"/>), this machine removes its input hooks, injects
    /// nothing and tells peers not to switch here; a peer's input is never sent into a game window. On by default:
    /// input tools must stay out of anti-cheat-protected games. Name kept for 0.4 settings files.
    /// </summary>
    public bool PauseForProtectedGames { get; init; } = true;
    public GameLockMode GameLock { get; init; }
    /// <summary>Games locked besides VALORANT and League of Legends: lower-case executable names without extension.</summary>
    public string[] ExtraGames { get; init; } = [];
    /// <summary>Text and images copied here follow the pointer to the other machine, and are accepted from it.</summary>
    public bool ShareClipboard { get; init; } = true;
    /// <summary>Copied files and folders follow the pointer too (sent over a separate connection).</summary>
    public bool ShareFiles { get; init; } = true;

    /// <summary>Settings come from disk and the UI: drop anything out of range rather than fail.</summary>
    public SwitchOptions Sanitized() => this with
    {
        RequiredKey = Enum.IsDefined(RequiredKey) ? RequiredKey : SwitchKey.None,
        DoubleTap = Enum.IsDefined(DoubleTap) ? DoubleTap : DoubleTapMode.Off,
        BlockedPrograms = NormalizePrograms(BlockedPrograms),
        GameLock = Enum.IsDefined(GameLock) ? GameLock : GameLockMode.InFront,
        ExtraGames = NormalizePrograms(ExtraGames),
        ToggleHotkey = ToggleHotkey?.IsValid == true ? ToggleHotkey : null,
        NextDeviceHotkey = NextDeviceHotkey?.IsValid == true ? NextDeviceHotkey : null,
        HomeHotkey = HomeHotkey?.IsValid == true ? HomeHotkey : null,
        JumpMods = new Hotkey(JumpMods, 0x31).IsValid ? JumpMods : KeyMods.None
    };

    public static string[] NormalizePrograms(IEnumerable<string?>? names) =>
    [
        .. (names ?? []).Select(NormalizeProgram).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxPrograms)
    ];

    /// <summary>"C:\Games\Game.EXE" and "game" both become "game".</summary>
    public static string NormalizeProgram(string? name)
    {
        var n = (name ?? "").Trim().Trim('"');
        var slash = n.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) n = n[(slash + 1)..];
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        n = new string(n.Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
        return n.ToLowerInvariant();
    }
}

/// <summary>Operating-system conditions the switch engine asks about. Called on the input thread; must be fast.</summary>
public interface ISwitchEnvironment
{
    /// <summary>Foreground-window conditions that block leaving <paramref name="from"/> on this machine.</summary>
    BlockReason CheckForeground(RectI from, SwitchOptions options);
    /// <summary>Physical key state as Windows sees it (keys blocked by our hook are not included).</summary>
    bool IsKeyDown(int vk);
    /// <summary>A locked game owns the foreground window: never leave this machine, never inject into it.</summary>
    bool GameInFront();
    /// <summary>A locked game owns the window under the pointer: a click or wheel there would reach the game.</summary>
    bool GameUnderCursor();
    /// <summary>
    /// A program holds the pointer inside part of the screen (ClipCursor), as games do while playing even in a window.
    /// The hooks still see the mouse push past that boundary, so this is asked before leaving.
    /// </summary>
    bool CursorConfined();
}

/// <summary>
/// Whether a locked game is in front, steadied for the hooks: it locks at once, unlocks only once another window
/// has stayed in front for <paramref name="unlockDelayMs"/>, and keeps its state while the Alt+Tab switcher is open.
/// </summary>
public sealed class GameFocus(long unlockDelayMs = 300)
{
    private long since = -1;
    public bool InFront { get; private set; }

    /// <param name="gameInFront">Null while the user is choosing a window (task switcher, no foreground window).</param>
    /// <returns>True when <see cref="InFront"/> changed.</returns>
    public bool Update(bool? gameInFront, long nowMs)
    {
        if (gameInFront != false) since = -1;
        if (gameInFront == null) return false;
        if (gameInFront.Value) { if (InFront) return false; return InFront = true; }
        if (!InFront) return false;
        if (since < 0) since = nowMs;
        if (nowMs - since < unlockDelayMs) return false;
        InFront = false; since = -1;
        return true;
    }

    /// <summary>The game exited: nothing left to wait for.</summary>
    public bool Reset() { since = -1; if (!InFront) return false; InFront = false; return true; }
}
