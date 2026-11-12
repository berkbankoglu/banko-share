using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BankoShare.App;

internal enum TrayState { Idle, Connected, Sharing }

/// <summary>
/// The Banko Share mark in BankoSpace's Noir palette: a pointer on a dark rounded tile. Grey with no device,
/// green when a paired device is connected, green with a dot while the pointer is shared.
/// The same drawing produces the tray icon at run time and BankoShare.ico at design time.
/// </summary>
internal static class AppIcon
{
    private static readonly Color Tile = Color.FromArgb(0x1E, 0x1E, 0x1E), Edge = Color.FromArgb(0x3A, 0x3A, 0x3A),
        Ink = Color.FromArgb(0x1A, 0x1A, 0x1A), Grey = Color.FromArgb(0xB0, 0xB0, 0xB0), Green = Color.FromArgb(0x7E, 0xE7, 0x87);

    public static void Draw(Graphics g, int size, TrayState state)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var s = size / 32f;
        using (var tile = Rounded(new RectangleF(0.5f * s, 0.5f * s, 31 * s, 31 * s), 7.5f * s))
        {
            using var fill = new SolidBrush(Tile);
            g.FillPath(fill, tile);
            if (size >= 24) { using var edge = new Pen(Edge, Math.Max(1, s)); g.DrawPath(edge, tile); }
        }
        PointF P(float x, float y) => new(x * s, y * s);
        PointF[] pointer = [P(9, 5.5f), P(9, 25), P(13.6f, 20.6f), P(17, 27.4f), P(20.6f, 25.8f), P(17.2f, 19.1f), P(23.4f, 18.7f)];
        using (var body = new SolidBrush(state == TrayState.Idle ? Grey : Green)) g.FillPolygon(body, pointer);
        using (var outline = new Pen(Ink, Math.Max(1, 1.2f * s)) { LineJoin = LineJoin.Round }) g.DrawPolygon(outline, pointer);
        if (state == TrayState.Sharing)
        {
            using var dot = new SolidBrush(Green);
            using var ring = new Pen(Tile, Math.Max(1, 1.5f * s));
            var d = new RectangleF(21 * s, 3.5f * s, 8 * s, 8 * s);
            g.FillEllipse(dot, d);
            g.DrawEllipse(ring, d);
        }
    }

    /// <summary>A tray-sized icon. The caller disposes it.</summary>
    public static Icon Create(TrayState state, int size)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap)) Draw(g, size, state);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint handle);
}
