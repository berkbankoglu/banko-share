using BankoShare.Core;

internal static class InputStateTests
{
    public static int Run()
    {
        var count = 0;
        void Test(string name, Action action) { action(); count++; Console.WriteLine($"PASS {name}"); }
        Test("Disconnect releases all held keys and buttons, including extended keys", () =>
        {
            List<InputEvent> received = []; var state = new InputState(received.Add);
            state.Apply(new(EventKind.Key, 29, 1, 0)); state.Apply(new(EventKind.Key, 29, 1, 1));
            state.Apply(new(EventKind.Button, 1, 1)); state.Apply(new(EventKind.Button, 5, 1));
            received.Clear(); state.ReleaseAll();
            Check(received.Count == 4 && received.All(e => e.B == 0), "Not all inputs released");
            Check(received.Contains(new(EventKind.Key, 29, 0, 1)), "Extended modifier lost");
            received.Clear(); state.ReleaseAll(); Check(received.Count == 0, "Released inputs repeated");
        });
        Test("Key repeat requires one release and already released keys are not repeated", () =>
        {
            List<InputEvent> received = []; var state = new InputState(received.Add);
            for (var i = 0; i < 20; i++) state.Apply(new(EventKind.Key, 30, 1));
            state.Apply(new(EventKind.Key, 31, 1)); state.Apply(new(EventKind.Key, 31, 0));
            received.Clear(); state.Apply(new(EventKind.ReleaseAll));
            Check(received.SequenceEqual(new[] { new InputEvent(EventKind.Key, 30, 0) }), "Repeat or key-up tracking failed");
        });
        Test("Failed releases do not skip other inputs and can be retried", () =>
        {
            List<InputEvent> received = []; bool fail = true;
            var state = new InputState(e =>
            {
                if (fail && e.Kind == EventKind.Key && e.B == 0) throw new IOException("Test sink failure");
                received.Add(e);
            });
            state.Apply(new(EventKind.Key, 29, 1)); state.Apply(new(EventKind.Button, 1, 1)); received.Clear();
            try { state.ReleaseAll(); throw new Exception("Expected release failure"); }
            catch (AggregateException) { }
            Check(received.SequenceEqual(new[] { new InputEvent(EventKind.Button, 1, 0) }), "Button release was skipped");
            fail = false; received.Clear(); state.ReleaseAll();
            Check(received.SequenceEqual(new[] { new InputEvent(EventKind.Key, 29, 0) }), "Failed release was not retained");
        });
        Test("Failed key-down is not recorded; heartbeat never reaches input sink", () =>
        {
            var calls = 0; var state = new InputState(_ => { calls++; throw new IOException("Rejected input"); });
            try { state.Apply(new(EventKind.Key, 30, 1)); } catch (IOException) { }
            state.Apply(new(EventKind.Heartbeat)); state.ReleaseAll();
            Check(calls == 1, "Unapplied key was tracked or heartbeat injected");
        });
        return count;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
