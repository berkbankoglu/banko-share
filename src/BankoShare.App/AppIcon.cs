using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BankoShare.App;

internal enum TrayState { Idle, Connected, Sharing }

/// <summary>
/// The Banko Share mark: a large pointer in Claude's clay orange, filling the icon so it reads at taskbar and tray
/// sizes. No tile behind it: the taskbar shows through. A green dot top right while a paired device is connected,
/// a ringed dot while the pointer is shared. The same drawing produces the tray icon at run time and BankoShare.ico
/// at design time.
/// </summary>
internal static class AppIcon
{
    private static readonly Color Top = Color.FromArgb(0xE6, 0x8D, 0x6E), Bottom = Color.FromArgb(0xCF, 0x66, 0x44),
        Rim = Color.FromArgb(0x8E, 0x3D, 0x24),
        Green = Color.FromArgb(0x5C, 0xD9, 0x6A), GreenRim = Color.FromArgb(0x1F, 0x6B, 0x2A);

    /// <summary>The pointer on a 32-unit grid: tip, left edge, notch, tail, wing.</summary>
    private static readonly PointF[] Pointer =
        [new(6.4f, 1.6f), new(6.4f, 26.9f), new(12.3f, 21.3f), new(16.6f, 30.1f), new(21.2f, 28.0f), new(16.9f, 19.4f), new(24.9f, 18.9f)];

    public static void Draw(Graphics g, int size, TrayState state)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var s = size / 32f;
        using var path = new GraphicsPath();
        path.AddPolygon([.. Pointer.Select(p => new PointF(p.X * s, p.Y * s))]);
        // Rim first, body over it: half the rim shows outside, with rounded corners.
        using (var rim = new Pen(Rim, Math.Max(1.4f, 1.3f * s)) { LineJoin = LineJoin.Round }) g.DrawPath(rim, path);
        using (var body = new LinearGradientBrush(new PointF(0, 1.6f * s), new PointF(0, 30.1f * s), Top, Bottom)) g.FillPath(body, path);
        if (state == TrayState.Idle) return;
        var d = new RectangleF(21.4f * s, 1.6f * s, 9.4f * s, 9.4f * s);
        using var edge = new Pen(GreenRim, Math.Max(1f, 1.1f * s));
        using var green = new SolidBrush(Green);
        if (state == TrayState.Connected)
        {
            g.FillEllipse(green, d);
            g.DrawEllipse(edge, d);
            return;
        }
        // Sharing: a ring around a dot.
        using (var ring = new Pen(Green, Math.Max(1.5f, 2.0f * s))) g.DrawEllipse(ring, Inset(d, 1.0f * s));
        g.FillEllipse(green, Inset(d, 3.3f * s));
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

    private static RectangleF Inset(RectangleF r, float by) => new(r.X + by, r.Y + by, r.Width - 2 * by, r.Height - 2 * by);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint handle);
}
