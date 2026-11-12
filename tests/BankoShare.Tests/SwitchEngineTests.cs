using BankoShare.Core;
using E = BankoShare.Core.EngineEffect;

/// <summary>Edge switching on the real development layout: DESKTOP (one monitor) above BANKOARTS' middle monitor.</summary>
internal static class SwitchEngineTests
{
    private static readonly PointI Anchor = new(1280, 720); // centre of the 2560×1440 primary monitor

    public static int Run()
    {
        var s = new Suite();
        s.Test("Pushing up through the middle monitor enters DESKTOP at the matching point", () =>
        {
            var (engine, fx) = Setup();
            T.Equal(HookResult.Block, engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx), "Crossing move must be blocked");
            T.Check(fx.SequenceEqual<E>([new E.HideLocalCursor(true, Anchor), new E.WarpLocal(Anchor), new E.Enter(Fixtures.RemoteId, new(680, 1074))]),
                "Unexpected effects: " + string.Join(", ", fx));
            T.Equal(Fixtures.RemoteId, engine.ActivePeer, "Active peer");
        });
        s.Test("Edges without a neighbour, and moves between local monitors, stay local", () =>
        {
            var (engine, fx) = Setup();
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(100, -5), new(100, 0), false, fx), "Left of DESKTOP");
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(2400, -5), new(2400, 0), false, fx), "Right of DESKTOP");
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(-1, 500), new(0, 500), false, fx), "Middle to left monitor");
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(-1925, 500), new(-1920, 500), false, fx), "Outer left edge");
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(-1000, -5), new(-1000, 0), false, fx), "Top of left monitor");
            T.Check(fx.Count == 0 && engine.ActivePeer == null, "Switched without a neighbour");
        });
        s.Test("Remote movement follows local deltas and returns home through the same edge", () =>
        {
            var (engine, fx) = Setup();
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx); fx.Clear();
            T.Equal(HookResult.Block, engine.OnMouseMove(Anchor + new PointI(10, -20), Anchor, false, fx), "Remote move must be blocked");
            T.Check(fx.SequenceEqual<E>([new E.Move(Fixtures.RemoteId, new(690, 1054))]), "Move: " + string.Join(", ", fx)); fx.Clear();
            engine.OnMouseMove(Anchor, Anchor, false, fx);
            T.Check(fx.Count == 0, "Zero delta sent a move");
            engine.OnMouseMove(Anchor + new PointI(0, 40), Anchor, false, fx);
            T.Check(fx.SequenceEqual<E>([new E.Leave(Fixtures.RemoteId), new E.WarpLocal(new(1010, 14)), new E.HideLocalCursor(false, Anchor)]),
                "Return: " + string.Join(", ", fx));
            T.Check(engine.ActivePeer == null, "Still remote");
        });
        s.Test("Remote pointer is clamped at edges that lead nowhere", () =>
        {
            var (engine, fx) = Setup();
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx); fx.Clear();
            engine.OnMouseMove(Anchor + new PointI(-2000, 0), Anchor, false, fx);
            T.Check(fx.SequenceEqual<E>([new E.Move(Fixtures.RemoteId, new(0, 1074))]), "Left clamp: " + string.Join(", ", fx)); fx.Clear();
            engine.OnMouseMove(Anchor + new PointI(0, -5000), Anchor, false, fx);
            T.Check(fx.SequenceEqual<E>([new E.Move(Fixtures.RemoteId, new(0, 0))]), "Top clamp: " + string.Join(", ", fx)); fx.Clear();
            engine.OnMouseMove(Anchor + new PointI(-50, -50), Anchor, false, fx);
            T.Check(fx.Count == 0 && engine.ActivePeer == Fixtures.RemoteId, "Corner push changed state");
            // Bottom-left corner of DESKTOP is above layout x=320, which is on the middle monitor.
            engine.OnMouseMove(Anchor + new PointI(0, 1085), Anchor, false, fx);
            T.Check(fx.Contains(new E.WarpLocal(new(320, 5))) && engine.ActivePeer == null, "Return: " + string.Join(", ", fx));
        });
        s.Test("No switch while a local button is held or switching is disabled", () =>
        {
            var (engine, fx) = Setup();
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(1000, -6), new(1000, 0), true, fx), "Button held");
            engine.Enabled = false;
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx), "Disabled");
            T.Check(fx.Count == 0, "Effects while blocked");
        });
        s.Test("Buttons, wheel and keys go to the active peer only", () =>
        {
            var (engine, fx) = Setup();
            T.Equal(HookResult.Pass, engine.OnButton(1, true, fx), "Local click");
            T.Equal(HookResult.Pass, engine.OnWheel(120, false, fx), "Local wheel");
            T.Equal(HookResult.Pass, engine.OnKey(0x41, 30, false, true, 0, fx), "Local key");
            engine.OnKey(0x41, 30, false, false, 0, fx);
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx); fx.Clear();
            engine.OnButton(4, true, fx); engine.OnWheel(-240, true, fx); engine.OnKey(0x41, 30, false, true, 0, fx); engine.OnKey(0x2E, 83, true, true, 0, fx);
            T.Check(fx.SequenceEqual<E>([
                new E.Forward(Fixtures.RemoteId, new(EventKind.Button, 4, 1)),
                new E.Forward(Fixtures.RemoteId, new(EventKind.Wheel, -240, 1)),
                new E.Forward(Fixtures.RemoteId, new(EventKind.Key, 30, 1, 0)),
                new E.Forward(Fixtures.RemoteId, new(EventKind.Key, 83, 1, 1))]), "Forwarded: " + string.Join(", ", fx));
        });
        s.Test("A key held before leaving keeps repeating and releases on this machine", () =>
        {
            var (engine, fx) = Setup();
            engine.OnKey(0xA2, 29, false, true, 0, fx);
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx); fx.Clear();
            T.Equal(HookResult.Pass, engine.OnKey(0xA2, 29, false, true, 10, fx), "Repeat of local key");
            T.Equal(HookResult.Pass, engine.OnKey(0xA2, 29, false, false, 20, fx), "Release of local key");
            T.Equal(HookResult.Block, engine.OnKey(0xA2, 29, false, true, 30, fx), "New press goes remote");
            T.Check(fx.SequenceEqual<E>([new E.Forward(Fixtures.RemoteId, new(EventKind.Key, 29, 1, 0))]), "Forwarded: " + string.Join(", ", fx));
        });
        s.Test("Double Esc brings the pointer home; holding Esc does not", () =>
        {
            var (engine, fx) = Setup();
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx); fx.Clear();
            engine.OnKey(0x1B, 1, false, true, 1000, fx); engine.OnKey(0x1B, 1, false, true, 1030, fx); engine.OnKey(0x1B, 1, false, true, 1060, fx);
            T.Check(engine.ActivePeer != null, "Auto-repeat triggered panic");
            engine.OnKey(0x1B, 1, false, false, 1100, fx);
            engine.OnKey(0x1B, 1, false, true, 2000, fx); engine.OnKey(0x1B, 1, false, false, 2050, fx);
            T.Check(engine.ActivePeer != null, "Slow taps triggered panic"); fx.Clear();
            T.Equal(HookResult.Block, engine.OnKey(0x1B, 1, false, true, 2300, fx), "Panic key");
            T.Check(engine.ActivePeer == null && fx.SequenceEqual<E>([new E.Leave(Fixtures.RemoteId), new E.WarpLocal(new(1000, 0)), new E.HideLocalCursor(false, Anchor)]),
                "Panic: " + string.Join(", ", fx));
        });
        s.Test("Losing the active peer returns the pointer to where it left", () =>
        {
            var (engine, fx) = Setup();
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx);
            engine.OnMouseMove(Anchor + new PointI(300, 0), Anchor, false, fx); fx.Clear();
            engine.RemovePeer(Fixtures.RemoteId, fx);
            T.Check(engine.ActivePeer == null && fx.SequenceEqual<E>([new E.WarpLocal(new(1000, 0)), new E.HideLocalCursor(false, Anchor)]),
                "Disconnect: " + string.Join(", ", fx));
            fx.Clear();
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx), "Disconnected peer is still a target");
        });
        s.Test("A device to the right of the rightmost monitor is reached from that monitor only", () =>
        {
            var layout = new ScreenLayout();
            layout.Offsets[Fixtures.LocalId] = default; layout.Offsets[Fixtures.RemoteId] = new(4480, 0);
            var engine = new SwitchEngine(Fixtures.Local, layout); List<E> fx = [];
            engine.SetPeer(Fixtures.Remote, fx);
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(2565, 500), new(2559, 500), false, fx), "Middle to right monitor");
            T.Equal(HookResult.Block, engine.OnMouseMove(new(4485, 500), new(4479, 500), false, fx), "Right edge");
            T.Check(fx.Contains(new E.Enter(Fixtures.RemoteId, new(5, 500))), "Entry: " + string.Join(", ", fx));
        });
        return s.Count;
    }

    private static (SwitchEngine, List<E>) Setup()
    {
        var layout = new ScreenLayout();
        layout.Offsets[Fixtures.LocalId] = default;
        layout.Offsets[Fixtures.RemoteId] = Fixtures.RemoteAboveMiddle;
        var engine = new SwitchEngine(Fixtures.Local, layout);
        List<E> fx = [];
        engine.SetPeer(Fixtures.Remote, fx);
        return (engine, fx);
    }
}
