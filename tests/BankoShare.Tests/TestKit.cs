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
    public void WarpCursor(PointI point) { lock (gate) warps.Add(point); }
    public void SetCursorHidden(bool hide, PointI anchor) { lock (gate) hidden.Add(hide); }
    public void Inject(InputEvent input)
    {
        if (FailWhen?.Invoke(input) == true) throw new IOException("Rejected by test platform");
        lock (gate) injected.Add(input);
    }
    public PointI[] Warps { get { lock (gate) return [.. warps]; } }
    public bool[] Hidden { get { lock (gate) return [.. hidden]; } }
    public InputEvent[] Injected { get { lock (gate) return [.. injected]; } }
}
