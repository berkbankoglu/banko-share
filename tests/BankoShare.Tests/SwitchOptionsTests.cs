using System.Text.Json;
using BankoShare.Core;
using E = BankoShare.Core.EngineEffect;

/// <summary>Phase 2 switching options on the development layout: DESKTOP above BANKOARTS' middle monitor.</summary>
internal static class SwitchOptionsTests
{
    private static readonly PointI Anchor = new(1280, 720);
    private const int LCtrl = 0xA2, RAlt = 0xA5, LAlt = 0xA4, LShift = 0xA0;

    public static int Run()
    {
        var s = new Suite();
        s.Test("Equalized edges cross anywhere along the edge and map back to the same point", () =>
        {
            var (engine, fx, _) = Setup(new SwitchOptions { EqualizeEdges = true });
            // x=100 is left of DESKTOP (layout 320..2240): 100/2560 of the way maps to 75/1920.
            T.Equal(HookResult.Block, engine.OnMouseMove(new(100, -6), new(100, 0), false, fx), "Outside DESKTOP's span");
            T.Check(fx.Contains(new E.Enter(Fixtures.RemoteId, new(75, 1074))), "Entry: " + string.Join(", ", fx)); fx.Clear();
            engine.OnMouseMove(Anchor + new PointI(0, 10), Anchor, false, fx);
            T.Check(engine.ActivePeer == null && fx.Contains(new E.WarpLocal(new(100, 4))), "Round trip: " + string.Join(", ", fx)); fx.Clear();
            engine.OnMouseMove(new(2500, -6), new(2500, 0), false, fx);
            T.Check(fx.Contains(new E.Enter(Fixtures.RemoteId, new(1875, 1074))), "Right part: " + string.Join(", ", fx));
        });
        s.Test("Wrapping enters from the far end of the row or column", () =>
        {
            var (engine, fx, _) = Setup(new SwitchOptions { Wrap = true });
            T.Equal(HookResult.Block, engine.OnMouseMove(new(-1925, 500), new(-1920, 500), false, fx), "Outer left edge");
            T.Check(fx.SequenceEqual<E>([new E.WarpLocal(new(4479, 500))]) && engine.ActivePeer == null, "Local wrap: " + string.Join(", ", fx)); fx.Clear();
            engine.OnMouseMove(new(1000, 1445), new(1000, 1439), false, fx);
            T.Check(fx.Contains(new E.Enter(Fixtures.RemoteId, new(680, 0))), "Bottom of C to top of DESKTOP: " + string.Join(", ", fx));
            var (plain, pfx, _) = Setup(new SwitchOptions());
            T.Equal(HookResult.Pass, plain.OnMouseMove(new(-1925, 500), new(-1920, 500), false, pfx), "Wrapped while off");
        });
        s.Test("Corners block until the push slides out of the corner zone", () =>
        {
            var layout = new ScreenLayout();
            layout.Offsets[Fixtures.LocalId] = default; layout.Offsets[Fixtures.RemoteId] = new(4480, 0);
            var engine = new SwitchEngine(Fixtures.Local, layout) { Options = new SwitchOptions { BlockCorners = true } }; List<E> fx = [];
            engine.SetPeer(Fixtures.Remote, fx);
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(4485, 10), new(4479, 10), false, fx), "Top-right corner");
            T.Check(fx.SequenceEqual<E>([new E.Blocked(BlockReason.Corner, false)]), "Notice: " + string.Join(", ", fx)); fx.Clear();
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(4486, 30), new(4479, 30), false, fx), "Still in the corner");
            T.Check(fx.Count == 0, "Repeated notice within one push");
            T.Equal(HookResult.Block, engine.OnMouseMove(new(4486, 60), new(4479, 60), false, fx), "Out of the corner");
            T.Check(fx.Contains(new E.Enter(Fixtures.RemoteId, new(6, 60))), "Entry: " + string.Join(", ", fx));
        });
        s.Test("A required key must be held; Windows' key state decides while the pointer is here", () =>
        {
            var (engine, fx, _) = Setup(new SwitchOptions { RequiredKey = SwitchKey.Ctrl });
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx), "Without Ctrl");
            T.Check(fx.SequenceEqual<E>([new E.Blocked(BlockReason.Key, false)]), "Notice: " + string.Join(", ", fx)); fx.Clear();
            engine.OnKey(LCtrl, 29, false, true, 0, fx);
            T.Equal(HookResult.Block, engine.OnMouseMove(new(1000, -7), new(1000, 0), false, fx), "With Ctrl, same push");
            T.Equal(HookResult.Pass, engine.OnKey(LCtrl, 29, false, false, 0, fx), "Ctrl held before leaving is released here");
            var env = new FakeEnvironment();
            var (osEngine, ofx, _) = Setup(new SwitchOptions { RequiredKey = SwitchKey.Ctrl }, env);
            env.Down.Add(LCtrl);
            T.Equal(HookResult.Block, osEngine.OnMouseMove(new(1000, -6), new(1000, 0), false, ofx), "Ctrl reported by Windows");
            var (altGr, afx, aenv) = Setup(new SwitchOptions { RequiredKey = SwitchKey.Ctrl }, new FakeEnvironment());
            aenv!.Down.UnionWith([LCtrl, RAlt]);
            T.Equal(HookResult.Pass, altGr.OnMouseMove(new(1000, -6), new(1000, 0), false, afx), "AltGr's synthetic Ctrl counted");
        });
        s.Test("Foreground blocks are checked once per push and a quick double tap overrides them", () =>
        {
            var env = new FakeEnvironment { Foreground = BlockReason.Fullscreen };
            var options = new SwitchOptions { BlockFullscreen = true, DoubleTap = DoubleTapMode.OverrideBlocks };
            var (engine, fx, _) = Setup(options, env);
            T.Equal(HookResult.Pass, engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx, 1000), "First tap");
            engine.OnMouseMove(new(1000, -9), new(1000, 0), false, fx, 1010);
            T.Check(fx.SequenceEqual<E>([new E.Blocked(BlockReason.Fullscreen, true)]) && env.ForegroundChecks == 1, $"Checks {env.ForegroundChecks}: " + string.Join(", ", fx));
            engine.OnMouseMove(new(1000, 10), new(1000, 0), false, fx, 1100);
            T.Equal(HookResult.Block, engine.OnMouseMove(new(1000, -6), new(1000, 10), false, fx, 1300), "Second tap");
            T.Check(engine.ActivePeer == Fixtures.RemoteId && env.ForegroundChecks == 2, "Double tap did not switch");

            var (slow, sfx, senv) = Setup(options, new FakeEnvironment { Foreground = BlockReason.Fullscreen });
            slow.OnMouseMove(new(1000, -6), new(1000, 0), false, sfx, 1000);
            slow.OnMouseMove(new(1000, 10), new(1000, 0), false, sfx, 1100);
            T.Equal(HookResult.Pass, slow.OnMouseMove(new(1000, -6), new(1000, 10), false, sfx, 1600), "Taps too far apart");
            var (shallow, hfx, _) = Setup(options, new FakeEnvironment { Foreground = BlockReason.Fullscreen });
            shallow.OnMouseMove(new(1000, -6), new(1000, 0), false, hfx, 1000);
            shallow.OnMouseMove(new(1000, 3), new(1000, 0), false, hfx, 1100);
            T.Equal(HookResult.Pass, shallow.OnMouseMove(new(1000, -6), new(1000, 3), false, hfx, 1200), "Sliding along the edge counted as a tap");
            var (off, ofx, _) = Setup(options with { DoubleTap = DoubleTapMode.Off }, new FakeEnvironment { Foreground = BlockReason.Fullscreen });
            off.OnMouseMove(new(1000, -6), new(1000, 0), false, ofx, 1000);
            off.OnMouseMove(new(1000, 10), new(1000, 0), false, ofx, 1100);
            T.Equal(HookResult.Pass, off.OnMouseMove(new(1000, -6), new(1000, 10), false, ofx, 1300), "Double tap while off");
            T.Check(ofx.SequenceEqual<E>([new E.Blocked(BlockReason.Fullscreen, false), new E.Blocked(BlockReason.Fullscreen, false)]), "Off notices: " + string.Join(", ", ofx));
        });
        s.Test("Always-double-tap applies in both directions; foreground checks never run for the peer's screen", () =>
        {
            var env = new FakeEnvironment { Foreground = BlockReason.Fullscreen };
            var (engine, fx, _) = Setup(new SwitchOptions { DoubleTap = DoubleTapMode.Always }, env);
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx, 1000);
            T.Check(fx.SequenceEqual<E>([new E.Blocked(BlockReason.DoubleTap, true)]) && engine.ActivePeer == null, "Single tap: " + string.Join(", ", fx));
            T.Check(env.ForegroundChecks == 0, "Foreground checked although no window condition is on");
            engine.OnMouseMove(new(1000, 20), new(1000, 0), false, fx, 1100);
            engine.OnMouseMove(new(1000, -6), new(1000, 20), false, fx, 1200);
            T.Check(engine.ActivePeer == Fixtures.RemoteId, "Double tap did not switch"); fx.Clear();
            engine.OnMouseMove(Anchor + new PointI(0, 10), Anchor, false, fx, 2000);
            T.Check(engine.ActivePeer != null && fx.SequenceEqual<E>([new E.Blocked(BlockReason.DoubleTap, true), new E.Move(Fixtures.RemoteId, new(680, 1079))]),
                "Single tap home: " + string.Join(", ", fx));
            engine.OnMouseMove(Anchor + new PointI(0, -10), Anchor, false, fx, 2100);
            engine.OnMouseMove(Anchor + new PointI(0, 20), Anchor, false, fx, 2200);
            T.Check(engine.ActivePeer == null && fx.Contains(new E.WarpLocal(new(1000, 9))), "Double tap home: " + string.Join(", ", fx));
        });
        s.Test("Hotkeys fire once, swallow their key, mask a local Alt and ignore AltGr", () =>
        {
            var options = new SwitchOptions { JumpMods = KeyMods.Ctrl | KeyMods.Alt, ToggleHotkey = new(KeyMods.Ctrl | KeyMods.Shift, 0x53) };
            var (engine, fx, _) = Setup(options);
            engine.OnKey(LCtrl, 29, false, true, 0, fx); engine.OnKey(LAlt, 56, false, true, 0, fx);
            T.Equal(HookResult.Block, engine.OnKey(0x32, 3, false, true, 0, fx), "Ctrl+Alt+2");
            T.Check(fx.SequenceEqual<E>([new E.MaskMenu(), new E.Command(HotkeyAction.JumpToMonitor, 1)]), "Jump: " + string.Join(", ", fx)); fx.Clear();
            T.Equal(HookResult.Block, engine.OnKey(0x32, 3, false, true, 30, fx), "Auto-repeat");
            T.Equal(HookResult.Block, engine.OnKey(0x32, 3, false, false, 60, fx), "Release");
            T.Check(fx.Count == 0, "Repeat fired again");
            engine.OnKey(LAlt, 56, false, false, 0, fx); engine.OnKey(LCtrl, 29, false, false, 0, fx);
            // AltGr+2 on a Turkish keyboard: synthetic left Ctrl (scan 0x21D) plus right Alt.
            engine.OnKey(LCtrl, 0x21D, false, true, 0, fx); engine.OnKey(RAlt, 56, true, true, 0, fx);
            T.Equal(HookResult.Pass, engine.OnKey(0x32, 3, false, true, 0, fx), "AltGr+2 must type");
            engine.OnKey(0x32, 3, false, false, 0, fx); engine.OnKey(RAlt, 56, true, false, 0, fx); engine.OnKey(LCtrl, 0x21D, false, false, 0, fx);
            engine.HotkeysSuspended = true;
            engine.OnKey(LCtrl, 29, false, true, 0, fx); engine.OnKey(LShift, 42, false, true, 0, fx);
            T.Equal(HookResult.Pass, engine.OnKey(0x53, 31, false, true, 0, fx), "Suspended hotkey fired");
            engine.OnKey(0x53, 31, false, false, 0, fx);
            engine.HotkeysSuspended = false;
            T.Check(fx.Count == 0, "Effects: " + string.Join(", ", fx));
            // Remote: modifiers go to the peer, the hotkey's own key does not, and no local mask is needed.
            engine.OnKey(LShift, 42, false, false, 0, fx); engine.OnKey(LCtrl, 29, false, false, 0, fx);
            engine.OnMouseMove(new(1000, -6), new(1000, 0), false, fx); fx.Clear();
            engine.OnKey(LCtrl, 29, false, true, 0, fx); engine.OnKey(LShift, 42, false, true, 0, fx);
            T.Equal(HookResult.Block, engine.OnKey(0x53, 31, false, true, 0, fx), "Ctrl+Shift+S while remote");
            T.Check(fx.SequenceEqual<E>([new E.Forward(Fixtures.RemoteId, new(EventKind.Key, 29, 1)), new E.Forward(Fixtures.RemoteId, new(EventKind.Key, 42, 1)),
                new E.Command(HotkeyAction.ToggleSwitching)]), "Remote: " + string.Join(", ", fx));
        });
        s.Test("Jumps move the pointer between devices and cycle in desk order", () =>
        {
            var (engine, fx, _) = Setup(new SwitchOptions());
            T.Check(engine.JumpTo(Fixtures.RemoteId, new(960, 540), fx), "Jump refused");
            T.Check(fx.SequenceEqual<E>([new E.HideLocalCursor(true, Anchor), new E.WarpLocal(Anchor), new E.Enter(Fixtures.RemoteId, new(960, 540))]),
                "To DESKTOP: " + string.Join(", ", fx)); fx.Clear();
            engine.JumpTo(Fixtures.LocalId, new(1280, 720), fx);
            T.Check(engine.ActivePeer == null && fx.SequenceEqual<E>([new E.Leave(Fixtures.RemoteId), new E.WarpLocal(Anchor), new E.HideLocalCursor(false, Anchor)]),
                "Home: " + string.Join(", ", fx)); fx.Clear();
            engine.JumpToNext(fx);
            T.Check(engine.ActivePeer == Fixtures.RemoteId && fx.Contains(new E.Enter(Fixtures.RemoteId, new(960, 540))), "Next: " + string.Join(", ", fx)); fx.Clear();
            engine.JumpToNext(fx);
            T.Check(engine.ActivePeer == null && fx.Contains(new E.WarpLocal(Anchor)), "Next wraps home: " + string.Join(", ", fx));
            T.Check(!engine.JumpTo(Fixtures.ThirdId, default, fx), "Jumped to a device that is not connected");
        });
        s.Test("Monitor letters follow desk reading order", () =>
        {
            var layout = new ScreenLayout();
            layout.Offsets[Fixtures.LocalId] = default; layout.Offsets[Fixtures.RemoteId] = Fixtures.RemoteAboveMiddle;
            var order = layout.ReadingOrder([Fixtures.Local, Fixtures.Remote]).Select(o => (o.Device.Name, o.Monitor.Left)).ToList();
            T.Check(order.SequenceEqual([("DESKTOP", 0), ("BANKOARTS", -1920), ("BANKOARTS", 0), ("BANKOARTS", 2560)]), "Order: " + string.Join(", ", order));
        });
        s.Test("Options load 0.3 files, survive a round trip and drop invalid values", () =>
        {
            var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "banko-share-tests", Guid.NewGuid().ToString("N")));
            try
            {
                Directory.CreateDirectory(store.DirectoryPath);
                File.WriteAllText(Path.Combine(store.DirectoryPath, ShareController.PreferencesFile), "{\"SwitchingEnabled\": false}");
                var old = store.Load(ShareController.PreferencesFile, () => new SwitchOptions()).Sanitized();
                T.Check(!old.SwitchingEnabled && !old.EqualizeEdges && !old.Wrap && old.DoubleTap == DoubleTapMode.Off && old.BlockedPrograms.Length == 0
                    && old.JumpMods == KeyMods.None && old.ToggleHotkey == null, "0.3 file");
                var full = new SwitchOptions
                {
                    EqualizeEdges = true, Wrap = true, BlockCorners = true, RequiredKey = SwitchKey.Shift, DoubleTap = DoubleTapMode.Always,
                    BlockedPrograms = ["game"], ToggleHotkey = new(KeyMods.Ctrl | KeyMods.Alt, 0x53), JumpMods = KeyMods.Ctrl | KeyMods.Shift
                };
                store.Save(ShareController.PreferencesFile, full);
                var loaded = store.Load(ShareController.PreferencesFile, () => new SwitchOptions()).Sanitized();
                T.Equal(JsonSerializer.Serialize(full), JsonSerializer.Serialize(loaded), "Round trip");
            }
            finally { try { Directory.Delete(store.DirectoryPath, true); } catch (IOException) { } }
            var messy = new SwitchOptions
            {
                BlockedPrograms = [@"C:\Games\Game.EXE", " game ", "OBS64.exe", "\"steam.exe\"", "", null!],
                ToggleHotkey = new(KeyMods.Ctrl, 0x53), HomeHotkey = new(KeyMods.Ctrl | KeyMods.Alt, LCtrl), NextDeviceHotkey = new((KeyMods)64 | KeyMods.Ctrl, 0x41),
                JumpMods = KeyMods.Shift, RequiredKey = (SwitchKey)42, DoubleTap = (DoubleTapMode)9
            }.Sanitized();
            T.Check(messy.BlockedPrograms.SequenceEqual(["game", "obs64", "steam"]), "Programs: " + string.Join(", ", messy.BlockedPrograms));
            T.Check(messy.ToggleHotkey == null && messy.HomeHotkey == null && messy.NextDeviceHotkey == null && messy.JumpMods == KeyMods.None
                && messy.RequiredKey == SwitchKey.None && messy.DoubleTap == DoubleTapMode.Off, "Invalid values kept");
        });
        s.Test("Settings writes wait out a briefly locked file, and background writes keep only the newest value", () =>
        {
            var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "banko-share-tests", Guid.NewGuid().ToString("N")));
            try
            {
                store.Save("x.json", 1);
                // Like a virus scanner: open without delete sharing, so replacing the file is refused until it closes.
                var holder = new FileStream(Path.Combine(store.DirectoryPath, "x.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
                var release = Task.Run(async () => { await Task.Delay(60); holder.Dispose(); });
                store.Save("x.json", 2);
                release.Wait();
                T.Equal(2, store.Load("x.json", () => 0), "Save while locked");
                var saver = new BackgroundSaver(store);
                for (var i = 3; i <= 50; i++) saver.Schedule("x.json", i);
                T.Check(saver.WaitIdle(TimeSpan.FromSeconds(5)), "Background writer did not finish");
                T.Equal(50, store.Load("x.json", () => 0), "Newest value");
            }
            finally { try { Directory.Delete(store.DirectoryPath, true); } catch (IOException) { } }
        });
        return s.Count;
    }

    private static (SwitchEngine, List<E>, FakeEnvironment?) Setup(SwitchOptions options, FakeEnvironment? environment = null)
    {
        var layout = new ScreenLayout();
        layout.Offsets[Fixtures.LocalId] = default;
        layout.Offsets[Fixtures.RemoteId] = Fixtures.RemoteAboveMiddle;
        var engine = new SwitchEngine(Fixtures.Local, layout) { Options = options, Environment = environment };
        List<E> fx = [];
        engine.SetPeer(Fixtures.Remote, fx);
        return (engine, fx, environment);
    }
}
