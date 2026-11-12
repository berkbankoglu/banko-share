using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BankoShare.Core;

namespace BankoShare.App;

internal sealed record LayoutItem(DeviceInfo Device, bool IsLocal, bool Connected);

/// <summary>
/// Drag-and-drop editor for the shared monitor layout, drawn to scale in physical pixels.
/// A device's monitors move together; the drop position is snapped by the controller.
/// </summary>
internal sealed class LayoutEditor : FrameworkElement
{
    private static readonly Brush Surface = Freeze("#0B111C"), LocalFill = Freeze("#83E3CB"), RemoteFill = Freeze("#7C93E6"),
        OfflineFill = Freeze("#3A4458"), Outline = Freeze("#101827"), Dark = Freeze("#0B111C"), Light = Freeze("#EAF1FF"), Hint = Freeze("#5A6B85");
    private const double Inset = 36;
    private IReadOnlyList<LayoutItem> items = [];
    private ScreenLayout layout = new();
    private (IReadOnlyList<LayoutItem> Items, ScreenLayout Layout)? pending;
    private double scale = 0.1;
    private Point origin;
    private string? dragId, hoverId;
    private Point dragStart;
    private PointI dragFrom, dragTo;

    /// <summary>Device id and its proposed layout offset after a drag.</summary>
    public event Action<string, PointI>? DeviceDropped;
    public double Scale => scale;

    public void SetData(IReadOnlyList<LayoutItem> nextItems, ScreenLayout nextLayout)
    {
        // Keep the picture stable under the user's mouse; apply updates when the drag ends.
        if (dragId != null) { pending = (nextItems, nextLayout); return; }
        items = nextItems; layout = nextLayout;
        InvalidateVisual();
    }

    private PointI OffsetOf(string id) => id == dragId ? dragTo : layout.OffsetOf(id);

    /// <summary>Each monitor with its device-local bounds and its place on the canvas (following an active drag).</summary>
    private IEnumerable<(LayoutItem Item, RectI Monitor, RectI Rect)> Screens() =>
        items.SelectMany(i => i.Device.Monitors.Select(m => (i, m.Bounds, m.Bounds.Offset(OffsetOf(i.Device.Id)))));

    private void Fit()
    {
        var all = Screens().Select(s => s.Rect).ToList();
        if (all.Count == 0 || ActualWidth < 2 * Inset || ActualHeight < 2 * Inset) return;
        var box = RectI.Union(all);
        scale = Math.Min((ActualWidth - 2 * Inset) / box.Width, (ActualHeight - 2 * Inset) / box.Height);
        origin = new Point((ActualWidth - box.Width * scale) / 2 - box.Left * scale, (ActualHeight - box.Height * scale) / 2 - box.Top * scale);
    }

    private Rect ToView(RectI r) => new(origin.X + r.Left * scale, origin.Y + r.Top * scale, r.Width * scale, r.Height * scale);

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 640 : available.Width, double.IsInfinity(available.Height) ? 360 : available.Height);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Surface, null, new Rect(0, 0, ActualWidth, ActualHeight), 10, 10);
        if (dragId == null) Fit();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (items.Count == 0)
        {
            var empty = Text("Monitör bilgisi bekleniyor…", 14, Hint, dpi, FontWeights.Normal);
            dc.DrawText(empty, new Point((ActualWidth - empty.Width) / 2, (ActualHeight - empty.Height) / 2));
            return;
        }
        // Letters follow reading order on the desk, exactly as the jump hotkeys count, and hold still during a drag.
        var letters = new Dictionary<(string, RectI), int>();
        foreach (var (device, monitor) in layout.ReadingOrder(items.Select(i => i.Device))) letters.TryAdd((device.Id, monitor), letters.Count);
        foreach (var (item, monitor, rect) in Screens())
        {
            var view = ToView(rect); view.Inflate(-1.5, -1.5);
            if (view.Width < 4 || view.Height < 4) continue;
            var id = item.Device.Id;
            var fill = item.IsLocal ? LocalFill : item.Connected ? RemoteFill : OfflineFill;
            var ink = item.IsLocal || item.Connected ? Dark : Light;
            dc.PushOpacity(id == dragId ? 0.8 : 1);
            dc.DrawRoundedRectangle(fill, new Pen(id == hoverId || id == dragId ? Light : Outline, id == hoverId || id == dragId ? 2 : 1), view, 6, 6);
            var badge = Math.Clamp(Math.Min(view.Width, view.Height) * 0.3, 14, 40);
            var centre = new Point(view.X + view.Width / 2, view.Y + view.Height / 2 - badge * 0.35);
            dc.DrawEllipse(Dark, null, centre, badge / 2, badge / 2);
            var index = letters.GetValueOrDefault((id, monitor), -1);
            var letter = Text(index >= 0 ? ((char)('A' + index % 26)).ToString() : "·", badge * 0.55, Light, dpi, FontWeights.Bold);
            dc.DrawText(letter, new Point(centre.X - letter.Width / 2, centre.Y - letter.Height / 2));
            var size = Math.Clamp(view.Height * 0.1, 9, 13);
            var name = Text(item.Device.Name, size, ink, dpi, FontWeights.SemiBold, view.Width - 10);
            var resolution = Text($"{rect.Width}×{rect.Height}", size * 0.9, ink, dpi, FontWeights.Normal, view.Width - 10);
            var y = Math.Max(centre.Y + badge / 2 + 4, view.Bottom - name.Height - resolution.Height - 8);
            if (y + name.Height < view.Bottom) dc.DrawText(name, new Point(view.X + (view.Width - name.Width) / 2, y));
            if (y + name.Height + resolution.Height < view.Bottom) dc.DrawText(resolution, new Point(view.X + (view.Width - resolution.Width) / 2, y + name.Height));
            dc.Pop();
        }
    }

    private static FormattedText Text(string s, double size, Brush brush, double dpi, FontWeight weight, double maxWidth = 0)
    {
        var text = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, dpi);
        if (maxWidth > 0) { text.MaxTextWidth = maxWidth; text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        return text;
    }

    private string? DeviceAt(Point p) => Screens().LastOrDefault(s => ToView(s.Rect).Contains(p)).Item?.Device.Id;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var id = DeviceAt(e.GetPosition(this));
        if (id == null) return;
        dragId = id; dragStart = e.GetPosition(this); dragFrom = dragTo = layout.OffsetOf(id);
        CaptureMouse(); e.Handled = true; InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (dragId != null)
        {
            var d = (p - dragStart) / scale;
            dragTo = new(dragFrom.X + (int)Math.Round(d.X), dragFrom.Y + (int)Math.Round(d.Y));
            InvalidateVisual();
            return;
        }
        var hover = DeviceAt(p);
        if (hover == hoverId) return;
        hoverId = hover; Cursor = hover != null ? Cursors.SizeAll : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragId == null) return;
        var (id, to, moved) = (dragId, dragTo, dragTo != dragFrom);
        EndDrag();
        if (moved) DeviceDropped?.Invoke(id, to);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { if (dragId != null) EndDrag(); }
    protected override void OnMouseLeave(MouseEventArgs e) { if (hoverId == null) return; hoverId = null; InvalidateVisual(); }

    private void EndDrag()
    {
        dragId = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (pending is { } p) { items = p.Items; layout = p.Layout; pending = null; }
        InvalidateVisual();
    }

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        brush.Freeze();
        return brush;
    }
}
