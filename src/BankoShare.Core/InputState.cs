namespace BankoShare.Core;

/// <summary>Tracks only successfully applied input. The sink is replaceable for tests without OS input.</summary>
public sealed class InputState(Action<InputEvent> sink)
{
    private readonly HashSet<(int Scan, int Extended)> keys = [];
    private readonly HashSet<int> buttons = [];

    public void Apply(InputEvent input)
    {
        if (input.Kind == EventKind.ReleaseAll) { ReleaseAll(); return; }
        if (input.Kind == EventKind.Heartbeat) return;
        sink(input);
        if (input.Kind == EventKind.Key)
        {
            if (input.B == 1) keys.Add((input.A, input.C));
            else keys.Remove((input.A, input.C));
        }
        if (input.Kind == EventKind.Button)
        {
            if (input.B == 1) buttons.Add(input.A);
            else buttons.Remove(input.A);
        }
    }

    public bool IsKeyHeld(int scan) => keys.Any(k => k.Scan == scan);

    public void ReleaseAll()
    {
        List<Exception> failures = [];
        foreach (var key in keys.ToArray())
            try { Apply(new(EventKind.Key, key.Scan, 0, key.Extended)); }
            catch (Exception error) { failures.Add(error); }
        foreach (var button in buttons.ToArray())
            try { Apply(new(EventKind.Button, button, 0)); }
            catch (Exception error) { failures.Add(error); }
        // Failed releases remain tracked for another attempt; one failure never skips the rest.
        if (failures.Count != 0) throw new AggregateException("Some held inputs could not be released.", failures);
    }
}
