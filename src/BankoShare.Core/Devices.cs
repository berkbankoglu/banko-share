namespace BankoShare.Core;

public sealed record MonitorInfo(RectI Bounds, bool Primary, int Dpi);

/// <summary>A device as it describes itself. Id is the SHA-256 fingerprint of its certificate in upper-case hex.</summary>
public sealed record DeviceInfo(string Id, string Name, MonitorInfo[] Monitors)
{
    public const int MaxMonitors = 16;

    public RectI PrimaryBounds => (Monitors.FirstOrDefault(m => m.Primary) ?? Monitors[0]).Bounds;

    public RectI? MonitorAt(PointI p)
    {
        foreach (var m in Monitors) if (m.Bounds.Contains(p)) return m.Bounds;
        return null;
    }

    public RectI NearestMonitor(PointI p) => Monitors.MinBy(m => m.Bounds.DistanceSquared(p))!.Bounds;

    /// <summary>Peer-supplied data is untrusted: bound every field before it reaches geometry code.</summary>
    public void Validate()
    {
        if (!DeviceIds.IsValid(Id)) throw new InvalidDataException("Invalid device id.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64) throw new InvalidDataException("Invalid device name.");
        if (Monitors is not { Length: > 0 and <= MaxMonitors }) throw new InvalidDataException("Invalid monitor list.");
        foreach (var m in Monitors)
        {
            if (m is null) throw new InvalidDataException("Invalid monitor.");
            var b = m.Bounds;
            if (b.Width is < 64 or > 16384 || b.Height is < 64 or > 16384 || Math.Abs(b.Left) > 32768 || Math.Abs(b.Top) > 32768 || m.Dpi is < 48 or > 960)
                throw new InvalidDataException("Invalid monitor bounds.");
        }
    }
}

public static class DeviceIds
{
    public static bool IsValid(string? id) => id is { Length: 64 } && id.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
