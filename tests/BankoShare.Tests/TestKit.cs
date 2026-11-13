using System.Diagnostics;
using BankoShare.Core;

internal sealed class Suite
{
    public int Count { get; private set; }
    public void Test(string name, Action action) { action(); Count++; Console.WriteLine($"PASS {name}"); }
    public async Task TestAsync(string name, Func<Task> action) { await action(); Count++; Console.WriteLine($"PASS {name}"); }
}

internal static class T
{
    public static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Equal<TV>(TV expected, TV actual, string what)
    { if (!EqualityComparer<TV>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}"); }
    public static void Throws<TEx>(Action action) where TEx : Exception
    { try { action(); } catch (TEx) { return; } throw new Exception($"Expected {typeof(TEx).Name}"); }
    public static async Task ThrowsAsync<TEx>(Func<Task> action) where TEx : Exception
    { try { await action(); } catch (TEx) { return; } throw new Exception($"Expected {typeof(TEx).Name}"); }
    public static async Task Eventually(Func<bool> condition, string message, int timeoutMs = 8000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMs) throw new Exception("Timed out: " + message);
            await Task.Delay(20);
        }
    }
}

/// <summary>The development setup: BANKOARTS with three monitors, DESKTOP with one monitor above the middle one.</summary>
internal static class Fixtures
{
    public static readonly string LocalId = new('A', 64), RemoteId = new('B', 64), ThirdId = new('C', 64);
    public static MonitorInfo[] Bankoarts =>
    [
        new(new(-1920, 0, 1920, 1080), false, 96),
        new(new(0, 0, 2560, 1440), true, 96),
        new(new(2560, 0, 1920, 1080), false, 96)
    ];
    public static MonitorInfo[] Desktop => [new(new(0, 0, 1920, 1080), true, 96)];
    public static DeviceInfo Local => new(LocalId, "BANKOARTS", Bankoarts);
    public static DeviceInfo Remote => new(RemoteId, "DESKTOP", Desktop);
    /// <summary>DESKTOP spans layout x 320..2240 directly above the 2560-wide middle monitor.</summary>
    public static PointI RemoteAboveMiddle => new(320, -1080);
}

internal sealed class FakePlatform : IInputPlatform
{
    private readonly object gate = new();
    private readonly List<PointI> warps = [];
    private readonly List<bool> hidden = [];
    private readonly List<InputEvent> injected = [];
    public Func<InputEvent, bool>? FailWhen { get; set; }
    /// <summary>Runs inside Inject, e.g. to block like SendInput does while Windows waits for low-level hooks.</summary>
    public Action<InputEvent>? DuringInject { get; set; }
    public void WarpCursor(PointI point) { lock (gate) warps.Add(point); }
    public void SetCursorHidden(bool hide, PointI anchor) { lock (gate) hidden.Add(hide); }
    public void Inject(InputEvent input)
    {
        DuringInject?.Invoke(input);
        if (FailWhen?.Invoke(input) == true) throw new IOException("Rejected by test platform");
        lock (gate) injected.Add(input);
    }
    private int masks;
    public void MaskMenuKey() { lock (gate) { masks++; injected.Add(new(EventKind.Heartbeat, 0xE8)); } }
    public PointI[] Warps { get { lock (gate) return [.. warps]; } }
    public bool[] Hidden { get { lock (gate) return [.. hidden]; } }
    /// <summary>Applied input in order; a menu mask appears as a Heartbeat event carrying 0xE8.</summary>
    public InputEvent[] Injected { get { lock (gate) return [.. injected]; } }
    public int Masks { get { lock (gate) return masks; } }
}

/// <summary>Scripted foreground state and physical keys, counting how often the engine asks.</summary>
internal sealed class FakeEnvironment : ISwitchEnvironment
{
    public BlockReason Foreground { get; set; }
    public HashSet<int> Down { get; } = [];
    public int ForegroundChecks { get; private set; }
    public BlockReason CheckForeground(RectI from, SwitchOptions options) { ForegroundChecks++; return Foreground; }
    public bool IsKeyDown(int vk) => Down.Contains(vk);
    /// <summary>A locked game owns the foreground window.</summary>
    public volatile bool GameFront;
    /// <summary>A locked game's window is under the pointer.</summary>
    public volatile bool GameBelow;
    public bool GameInFront() => GameFront;
    public bool GameUnderCursor() => GameBelow;
    /// <summary>A program holds the pointer inside its window.</summary>
    public volatile bool Confined;
    public int ConfinedChecks { get; private set; }
    public bool CursorConfined() { ConfinedChecks++; return Confined; }
}
