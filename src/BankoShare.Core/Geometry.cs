using System.Text.Json.Serialization;

namespace BankoShare.Core;

public readonly record struct PointI(int X, int Y)
{
    public static PointI operator +(PointI a, PointI b) => new(a.X + b.X, a.Y + b.Y);
    public static PointI operator -(PointI a, PointI b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>Physical-pixel rectangle. Right and Bottom are exclusive.</summary>
public readonly record struct RectI(int Left, int Top, int Width, int Height)
{
    [JsonIgnore] public int Right => Left + Width;
    [JsonIgnore] public int Bottom => Top + Height;
    [JsonIgnore] public PointI Center => new(Left + Width / 2, Top + Height / 2);
    public bool Contains(PointI p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    public PointI Clamp(PointI p) => new(Math.Clamp(p.X, Left, Right - 1), Math.Clamp(p.Y, Top, Bottom - 1));
    public RectI Offset(PointI by) => new(Left + by.X, Top + by.Y, Width, Height);
    public bool Intersects(RectI o) => Left < o.Right && o.Left < Right && Top < o.Bottom && o.Top < Bottom;
    public long DistanceSquared(PointI p) { var c = Clamp(p); long dx = p.X - c.X, dy = p.Y - c.Y; return dx * dx + dy * dy; }

    /// <summary>True when the rectangles share an edge segment of positive length.</summary>
    public bool Touches(RectI o) =>
        (Right == o.Left || o.Right == Left) && Math.Min(Bottom, o.Bottom) > Math.Max(Top, o.Top) ||
        (Bottom == o.Top || o.Bottom == Top) && Math.Min(Right, o.Right) > Math.Max(Left, o.Left);

    public static RectI Union(IEnumerable<RectI> rects)
    {
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var x in rects) { l = Math.Min(l, x.Left); t = Math.Min(t, x.Top); r = Math.Max(r, x.Right); b = Math.Max(b, x.Bottom); }
        return l == int.MaxValue ? default : new(l, t, r - l, b - t);
    }
}
