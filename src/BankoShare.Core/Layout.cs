namespace BankoShare.Core;

/// <summary>
/// Shared arrangement of every device's desktop. A device keeps its own Windows monitor arrangement;
/// the layout only stores where each device's desktop origin sits in one common coordinate space.
/// </summary>
public sealed class ScreenLayout
{
    public const int MaxCoordinate = 1_000_000;
    public long Revision { get; set; }
    public string Author { get; set; } = "";
    public Dictionary<string, PointI> Offsets { get; set; } = new(StringComparer.Ordinal);

    public ScreenLayout Clone() => new() { Revision = Revision, Author = Author, Offsets = new(Offsets, StringComparer.Ordinal) };
    public PointI OffsetOf(string id) => Offsets.TryGetValue(id, out var o) ? o : default;
    public IEnumerable<RectI> RectsOf(DeviceInfo d) => d.Monitors.Select(m => m.Bounds.Offset(OffsetOf(d.Id)));

    /// <summary>Newer revision wins; equal revisions prefer the smaller author id so both peers converge on one layout.</summary>
    public bool IsPreferredOver(ScreenLayout other) =>
        Revision != other.Revision ? Revision > other.Revision : string.CompareOrdinal(Author, other.Author) < 0;

    public bool SameArrangement(ScreenLayout other) =>
        Offsets.Count == other.Offsets.Count && Offsets.All(o => other.Offsets.TryGetValue(o.Key, out var v) && v == o.Value);

    public void Validate()
    {
        if (Revision < 0 || Offsets is null || Offsets.Count > 64 || Author is null || !(Author.Length == 0 || DeviceIds.IsValid(Author)))
            throw new InvalidDataException("Invalid layout.");
        foreach (var (id, o) in Offsets)
            if (!DeviceIds.IsValid(id) || Math.Abs(o.X) > MaxCoordinate || Math.Abs(o.Y) > MaxCoordinate)
                throw new InvalidDataException("Invalid layout offset.");
    }

    /// <summary>Puts devices without a position to the right of everything placed so far, in id order.</summary>
    public bool PlaceMissing(IEnumerable<DeviceInfo> devices)
    {
        var all = devices.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        var changed = false;
        foreach (var d in all)
        {
            if (Offsets.ContainsKey(d.Id)) continue;
            var placed = all.Where(x => Offsets.ContainsKey(x.Id)).SelectMany(RectsOf).ToList();
            var own = RectI.Union(d.Monitors.Select(m => m.Bounds));
            Offsets[d.Id] = placed.Count == 0 ? default : new PointI(RectI.Union(placed).Right - own.Left, RectI.Union(placed).Top - own.Top);
            changed = true;
        }
        return changed;
    }

    /// <summary>Finds the monitor under a layout point, returning bounds in that device's own desktop coordinates.</summary>
    public (DeviceInfo Device, RectI Monitor)? HitTest(PointI layoutPoint, IEnumerable<DeviceInfo> devices, string? excludeId = null)
    {
        foreach (var d in devices)
        {
            if (d.Id == excludeId || !Offsets.ContainsKey(d.Id)) continue;
            var local = layoutPoint - OffsetOf(d.Id);
            if (d.MonitorAt(local) is { } m) return (d, m);
        }
        return null;
    }

    /// <summary>
    /// Moves a dragged device so its monitors touch or align with nearby monitors of other devices.
    /// Returns false (and leaves the layout unchanged) when the result would overlap another device.
    /// </summary>
    public bool SnapAndPlace(string deviceId, PointI proposed, IReadOnlyCollection<DeviceInfo> devices, int threshold)
    {
        var moving = devices.FirstOrDefault(d => d.Id == deviceId);
        if (moving == null) return false;
        var others = devices.Where(d => d.Id != deviceId && Offsets.ContainsKey(d.Id)).SelectMany(RectsOf).ToList();
        var mine = moving.Monitors.Select(m => m.Bounds.Offset(proposed)).ToList();
        int? bestX = null, bestY = null;
        foreach (var m in mine)
            foreach (var o in others)
            {
                foreach (var dx in new[] { o.Right - m.Left, o.Left - m.Right, o.Left - m.Left, o.Right - m.Right })
                    if (Math.Abs(dx) <= threshold && (bestX == null || Math.Abs(dx) < Math.Abs(bestX.Value))) bestX = dx;
                foreach (var dy in new[] { o.Bottom - m.Top, o.Top - m.Bottom, o.Top - m.Top, o.Bottom - m.Bottom })
                    if (Math.Abs(dy) <= threshold && (bestY == null || Math.Abs(dy) < Math.Abs(bestY.Value))) bestY = dy;
            }
        PointI[] attempts = [new(proposed.X + (bestX ?? 0), proposed.Y + (bestY ?? 0)), new(proposed.X + (bestX ?? 0), proposed.Y),
            new(proposed.X, proposed.Y + (bestY ?? 0)), proposed];
        foreach (var at in attempts)
        {
            if (Math.Abs(at.X) > MaxCoordinate || Math.Abs(at.Y) > MaxCoordinate) continue;
            if (moving.Monitors.Any(m => others.Any(o => o.Intersects(m.Bounds.Offset(at))))) continue;
            Offsets[deviceId] = at;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Every placed monitor in reading order on the desk (top to bottom, then left to right). Index 0 is
    /// monitor A in the layout editor and digit 1 for the jump hotkeys. Monitors keep device-local bounds.
    /// </summary>
    public List<(DeviceInfo Device, RectI Monitor)> ReadingOrder(IEnumerable<DeviceInfo> devices) =>
    [
        .. devices.Where(d => Offsets.ContainsKey(d.Id))
            .SelectMany(d => d.Monitors.Select(m => (Device: d, Monitor: m.Bounds, At: m.Bounds.Offset(OffsetOf(d.Id)))))
            .OrderBy(x => x.At.Top).ThenBy(x => x.At.Left).ThenBy(x => x.Device.Id, StringComparer.Ordinal)
            .Select(x => (x.Device, x.Monitor))
    ];

    /// <summary>True when at least one monitor of the device shares an edge with another device's monitor.</summary>
    public bool IsConnected(DeviceInfo device, IEnumerable<DeviceInfo> devices)
    {
        var mine = RectsOf(device).ToList();
        return devices.Where(d => d.Id != device.Id && Offsets.ContainsKey(d.Id)).SelectMany(RectsOf).Any(o => mine.Any(m => m.Touches(o)));
    }
}
