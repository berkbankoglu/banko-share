using BankoShare.Core;

internal static class LayoutTests
{
    public static int Run()
    {
        var s = new Suite();
        s.Test("New devices are placed deterministically to the right of the arrangement", () =>
        {
            var a = new ScreenLayout(); var b = new ScreenLayout();
            T.Check(a.PlaceMissing([Fixtures.Local, Fixtures.Remote]), "Nothing placed");
            b.PlaceMissing([Fixtures.Remote, Fixtures.Local]);
            T.Check(a.SameArrangement(b), "Placement depends on input order");
            T.Equal(default, a.OffsetOf(Fixtures.LocalId), "First device origin");
            // BANKOARTS spans x -1920..4480, so DESKTOP starts at its right edge.
            T.Equal(new PointI(4480, 0), a.OffsetOf(Fixtures.RemoteId), "Second device offset");
            T.Check(!a.PlaceMissing([Fixtures.Local, Fixtures.Remote]), "Placed twice");
        });
        s.Test("Layout conflicts resolve the same way on both peers", () =>
        {
            var older = new ScreenLayout { Revision = 1, Author = Fixtures.RemoteId };
            var newer = new ScreenLayout { Revision = 2, Author = Fixtures.RemoteId };
            T.Check(newer.IsPreferredOver(older) && !older.IsPreferredOver(newer), "Revision ignored");
            var fromA = new ScreenLayout { Revision = 0, Author = Fixtures.LocalId };
            var fromB = new ScreenLayout { Revision = 0, Author = Fixtures.RemoteId };
            T.Check(fromA.IsPreferredOver(fromB) && !fromB.IsPreferredOver(fromA), "Tie not broken by author");
            T.Check(!fromA.IsPreferredOver(fromA.Clone()), "A layout must not replace an identical one");
        });
        s.Test("Dragging snaps a device edge-to-edge and rejects overlap", () =>
        {
            var layout = new ScreenLayout();
            layout.PlaceMissing([Fixtures.Local, Fixtures.Remote]);
            DeviceInfo[] devices = [Fixtures.Local, Fixtures.Remote];
            // Dropped slightly overlapping the middle monitor and a little off horizontally.
            T.Check(layout.SnapAndPlace(Fixtures.RemoteId, new(330, -1060), devices, 40), "Snap refused");
            T.Equal(-1080, layout.OffsetOf(Fixtures.RemoteId).Y, "Bottom edge should meet the top of the middle monitor");
            T.Check(layout.IsConnected(Fixtures.Remote, devices), "Snapped device is not adjacent");
            var before = layout.OffsetOf(Fixtures.RemoteId);
            T.Check(!layout.SnapAndPlace(Fixtures.RemoteId, new(500, 300), devices, 40), "Overlap accepted");
            T.Equal(before, layout.OffsetOf(Fixtures.RemoteId), "Rejected drop changed the layout");
            T.Check(layout.SnapAndPlace(Fixtures.RemoteId, new(9000, 9000), devices, 40), "Free drop refused");
            T.Check(!layout.IsConnected(Fixtures.Remote, devices), "Far device reported as adjacent");
        });
        s.Test("Hit testing maps layout points into each device's own coordinates", () =>
        {
            var layout = new ScreenLayout();
            layout.Offsets[Fixtures.LocalId] = default; layout.Offsets[Fixtures.RemoteId] = Fixtures.RemoteAboveMiddle;
            var hit = layout.HitTest(new(1000, -10), [Fixtures.Local, Fixtures.Remote]);
            T.Check(hit is { } h && h.Device.Id == Fixtures.RemoteId && h.Monitor == new RectI(0, 0, 1920, 1080), "Wrong monitor");
            T.Check(layout.HitTest(new(100, -10), [Fixtures.Local, Fixtures.Remote]) == null, "Empty space hit");
            T.Check(layout.HitTest(new(-1, 5), [Fixtures.Local, Fixtures.Remote])?.Monitor == new RectI(-1920, 0, 1920, 1080), "Negative monitor missed");
            T.Check(new RectI(0, 0, 10, 10).Touches(new(10, 5, 10, 10)) && !new RectI(0, 0, 10, 10).Touches(new(10, 10, 10, 10)), "Touches");
        });
        s.Test("Peer-supplied device and layout data is bounded", () =>
        {
            Fixtures.Local.Validate();
            T.Throws<InvalidDataException>(() => (Fixtures.Local with { Id = "abc" }).Validate());
            T.Throws<InvalidDataException>(() => (Fixtures.Local with { Name = new string('x', 65) }).Validate());
            T.Throws<InvalidDataException>(() => (Fixtures.Local with { Monitors = [] }).Validate());
            T.Throws<InvalidDataException>(() => (Fixtures.Local with { Monitors = [new(new(0, 0, 0, 1080), true, 96)] }).Validate());
            T.Throws<InvalidDataException>(() => (Fixtures.Local with { Monitors = [new(new(int.MaxValue - 5, 0, 1920, 1080), true, 96)] }).Validate());
            var bad = new ScreenLayout(); bad.Offsets["x"] = default;
            T.Throws<InvalidDataException>(bad.Validate);
            var far = new ScreenLayout(); far.Offsets[Fixtures.LocalId] = new(int.MaxValue, 0);
            T.Throws<InvalidDataException>(far.Validate);
            T.Throws<InvalidDataException>(new ScreenLayout { Author = null! }.Validate);
        });
        return s.Count;
    }
}
