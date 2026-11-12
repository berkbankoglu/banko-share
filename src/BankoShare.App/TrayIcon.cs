using System.Drawing;
using System.Windows.Forms;

namespace BankoShare.App;

/// <summary>
/// Notification-area icon with a Noir context menu. Lives on the WPF UI thread; WPF's message loop
/// pumps the hidden window that NotifyIcon creates.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ToolStripMenuItem switching, home, autostart;
    private TrayState? shownState;
    private string shownText = "";

    public event Action? OpenRequested, SwitchingToggled, HomeRequested, ExitRequested;
    public event Action<bool>? AutostartToggled;

    public TrayIcon(bool autostartAvailable)
    {
        var menu = new ContextMenuStrip { Renderer = new NoirRenderer(), ShowImageMargin = false, ShowCheckMargin = true, BackColor = Noir.Elevated, ForeColor = Noir.Text };
        var open = new ToolStripMenuItem("Banko Share'i aç") { Font = new Font(menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();
        switching = new ToolStripMenuItem("Kenardan geçiş");
        switching.Click += (_, _) => SwitchingToggled?.Invoke();
        home = new ToolStripMenuItem("Bu bilgisayara dön");
        home.Click += (_, _) => HomeRequested?.Invoke();
        autostart = new ToolStripMenuItem("Windows açılınca başlat") { Enabled = autostartAvailable };
        autostart.Click += (_, _) => AutostartToggled?.Invoke(!autostart.Checked);
        var exit = new ToolStripMenuItem("Çıkış");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.AddRange([open, new ToolStripSeparator(), switching, home, new ToolStripSeparator(), autostart, new ToolStripSeparator(), exit]);
        foreach (ToolStripItem item in menu.Items) item.ForeColor = Noir.Text;
        icon = new NotifyIcon { ContextMenuStrip = menu, Text = "Banko Share" };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(); };
        icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        Update(TrayState.Idle, "Banko Share", true, false, false);
        icon.Visible = true;
    }

    public void Update(TrayState state, string text, bool switchingEnabled, bool canGoHome, bool autostartEnabled)
    {
        if (state != shownState)
        {
            var old = icon.Icon;
            icon.Icon = AppIcon.Create(state, SystemInformation.SmallIconSize.Width);
            old?.Dispose();
            shownState = state;
        }
        // The tooltip is limited to 127 characters.
        var tip = text.Length > 120 ? text[..119] + "…" : text;
        if (tip != shownText) { icon.Text = tip; shownText = tip; }
        switching.Checked = switchingEnabled;
        home.Enabled = canGoHome;
        autostart.Checked = autostartEnabled;
    }

    public void ShowNotice(string title, string text) => icon.ShowBalloonTip(6000, title, text, ToolTipIcon.None);

    /// <summary>Hidden right before an update replaces the process, so no stale icon is left behind.</summary>
    public bool Visible { get => icon.Visible; set => icon.Visible = value; }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Icon?.Dispose();
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
    }

    private static class Noir
    {
        public static readonly Color Elevated = Color.FromArgb(0x2A, 0x2A, 0x2A), Hover = Color.FromArgb(0x31, 0x31, 0x31),
            Border = Color.FromArgb(0x33, 0x33, 0x33), Text = Color.FromArgb(0xF0, 0xF0, 0xF0), Green = Color.FromArgb(0x7E, 0xE7, 0x87);
    }

    private sealed class NoirRenderer() : ToolStripProfessionalRenderer(new NoirColors())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Noir.Text : Color.FromArgb(0x72, 0x72, 0x72);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Noir.Green, Math.Max(1.5f, r.Height / 8f));
            e.Graphics.DrawLines(pen, [new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f), new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f)]);
        }
    }

    private sealed class NoirColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Noir.Elevated;
        public override Color ImageMarginGradientBegin => Noir.Elevated;
        public override Color ImageMarginGradientMiddle => Noir.Elevated;
        public override Color ImageMarginGradientEnd => Noir.Elevated;
        public override Color MenuBorder => Noir.Border;
        public override Color MenuItemBorder => Noir.Hover;
        public override Color MenuItemSelected => Noir.Hover;
        public override Color MenuItemSelectedGradientBegin => Noir.Hover;
        public override Color MenuItemSelectedGradientEnd => Noir.Hover;
        public override Color MenuItemPressedGradientBegin => Noir.Hover;
        public override Color MenuItemPressedGradientEnd => Noir.Hover;
        public override Color SeparatorDark => Noir.Border;
        public override Color SeparatorLight => Noir.Elevated;
        public override Color CheckBackground => Noir.Elevated;
        public override Color CheckSelectedBackground => Noir.Hover;
        public override Color CheckPressedBackground => Noir.Hover;
        public override Color ButtonSelectedBorder => Noir.Hover;
    }
}
