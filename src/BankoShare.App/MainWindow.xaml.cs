using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BankoShare.Core;
using Microsoft.Win32;

namespace BankoShare.App;

public partial class MainWindow : Window
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x83, 0xE3, 0xCB));
    private static readonly Brush Remote = new SolidColorBrush(Color.FromRgb(0x7C, 0x93, 0xE6));
    private static readonly Brush Idle = new SolidColorBrush(Color.FromRgb(0x3A, 0x44, 0x58));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xAA, 0xBB, 0xD3));
    private readonly ShareHost? host;
    private readonly UpdateCoordinator updater;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer updatePoll = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool refreshQueued, updatingUi;
    // Null forces the first render, including the empty-list messages.
    private string? peersSignature, discoveredSignature;
    private (DateTimeOffset At, string Text) addresses = (DateTimeOffset.MinValue, "");
    private DateTimeOffset noticeUntil;

    public MainWindow()
    {
        InitializeComponent();
        updater = new UpdateCoordinator(new GithubUpdates()) { Automatic = UpdatePreferences.Load().Automatic };
        VersionText.Text = UpdateVersionText.Text = $"v{updater.CurrentVersion}";
        AutoUpdate.IsChecked = updater.Automatic;
        try { host = ShareHost.Start(updater.CurrentVersion); }
        catch (SocketException)
        { ShowFatal($"Bağlantı noktası {PeerNetwork.DefaultPort} başka bir uygulama tarafından kullanılıyor. Banko Share'in başka bir kopyası açık olabilir."); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        { ShowFatal("Banko Share başlatılamadı: " + e.Message); }
        if (host != null)
        {
            host.Controller.Changed += QueueRefresh;
            host.Controller.Notice += message => Dispatcher.BeginInvoke(() => ShowNotice(message));
            host.Network.Paired += _ => Dispatcher.BeginInvoke(() => { ShowNotice("Yeni cihaz eşleştirildi."); NavLayout.IsChecked = true; });
            Editor.DeviceDropped += OnDeviceDropped;
            if (host.Warning != null) { WarningText.Text = host.Warning; WarningText.Visibility = Visibility.Visible; }
        }
        updater.Changed += RefreshUpdates;
        updatePoll.Tick += async (_, _) => { if (updater.Automatic) await updater.CheckAsync(lifetime.Token); };
        tick.Tick += (_, _) => { updater.Tick(DateTimeOffset.Now, Busy); RefreshUpdates(); Refresh(); };
        ContentRendered += async (_, _) =>
        {
            updatePoll.Start(); tick.Start();
            if (updater.Automatic) await updater.CheckAsync(lifetime.Token);
            else UpdateStatus.Text = "Otomatik güncelleme kapalı.";
        };
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) =>
        {
            updatePoll.Stop(); tick.Stop(); lifetime.Cancel();
            SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            host?.Dispose();
        };
        Refresh();
    }

    private bool Busy => host?.Controller.IsBusy == true;

    private void ShowFatal(string message)
    {
        StatusText.Text = message; StatusDot.Fill = Brushes.IndianRed;
        DevicesPanel.IsEnabled = LayoutPanel.IsEnabled = SwitchingPanel.IsEnabled = false;
    }

    private void ShowNotice(string message) { NoticeText.Text = message; noticeUntil = DateTimeOffset.Now.AddSeconds(8); }

    // ---- Refresh ----

    private void QueueRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
    }

    private void Refresh()
    {
        refreshQueued = false;
        if (DateTimeOffset.Now > noticeUntil) NoticeText.Text = "";
        if (host == null) return;
        var s = host.Controller.Snapshot();
        LocalNameText.Text = s.Local.Name;
        if (DateTimeOffset.Now - addresses.At > TimeSpan.FromSeconds(15))
            addresses = (DateTimeOffset.Now, string.Join(" / ", PeerNetwork.LocalAddresses()));
        LocalInfoText.Text = $"{s.Local.Name} · {(addresses.Text.Length > 0 ? addresses.Text : "ağ yok")} · port {host.Network.Port} · {s.Local.Monitors.Length} monitör"
            + (host.Network.DiscoveryError != null ? " · ağda bulma kapalı" : "");
        RefreshPeers(s);
        RefreshDiscovered(s);
        RefreshPairing(s.Pairing);
        RefreshLayout(s);
        updatingUi = true; SwitchingBox.IsChecked = s.SwitchingEnabled; updatingUi = false;
        var connected = s.Peers.Count(p => p.Connected);
        (StatusText.Text, StatusDot.Fill) = s switch
        {
            { ControllingName: { } name } => ($"Fare ve klavye şu an {name} bilgisayarında · geri dönmek için Esc Esc", Remote),
            { ControlledByName: { } name } => ($"{name} bu bilgisayarı kontrol ediyor · fareyi oynatarak geri alın", Remote),
            { SwitchingEnabled: false } => ("Ekran kenarından geçiş kapalı", Idle),
            _ when connected > 0 => ($"{connected} cihaz bağlı · imleci değen kenara iterek geçin", Accent),
            _ when s.Peers.Count > 0 => ("Eşleşmiş cihaz bekleniyor · iki bilgisayarda da Banko Share açık olmalı", Idle),
            _ => ("Henüz eşleşmiş cihaz yok", Idle)
        };
    }

    private void RefreshPeers(ShareSnapshot s)
    {
        var signature = string.Join("|", s.Peers.Select(p => $"{p.Id}:{p.Name}:{p.Connected}:{p.Address}"));
        if (signature == peersSignature) return;
        peersSignature = signature;
        PeersPanel.Children.Clear();
        if (s.Peers.Count == 0)
        {
            PeersPanel.Children.Add(new TextBlock { Text = "Henüz eşleşmiş cihaz yok. Aşağıdan ekleyin.", Foreground = Muted });
            return;
        }
        foreach (var p in s.Peers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var remove = new Button { Content = "Kaldır", Style = (Style)FindResource("Secondary"), Margin = new Thickness(10, 0, 0, 0), Tag = p.Id };
            remove.Click += RemovePeer_Click;
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = p.Connected ? Accent : Idle, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = (p.Connected ? "Bağlı" : "Bağlı değil") + (p.Address != null ? " · " + p.Address : ""), Foreground = Muted, FontSize = 12 });
            row.Children.Add(text);
            PeersPanel.Children.Add(row);
        }
    }

    private void RefreshDiscovered(ShareSnapshot s)
    {
        var signature = string.Join("|", s.Discovered.Select(d => $"{d.Id}:{d.Name}:{d.Endpoint}"));
        if (signature == discoveredSignature) return;
        discoveredSignature = signature;
        var selected = (DiscoveredList.SelectedItem as ListBoxItem)?.Tag as DiscoveredDevice;
        updatingUi = true;
        DiscoveredList.Items.Clear();
        foreach (var d in s.Discovered)
        {
            var item = new ListBoxItem { Content = $"{d.Name}  ·  {d.Endpoint.Address}", Tag = d, Foreground = Brushes.White };
            DiscoveredList.Items.Add(item);
            if (d.Id == selected?.Id) item.IsSelected = true;
        }
        if (s.Discovered.Count == 0) DiscoveredList.Items.Add(new ListBoxItem { Content = "Aranıyor…", IsEnabled = false, Foreground = Muted });
        updatingUi = false;
    }

    private void RefreshPairing(PairingOffer? offer)
    {
        CodePanel.Visibility = offer != null ? Visibility.Visible : Visibility.Collapsed;
        ShowCodeButton.Visibility = offer == null ? Visibility.Visible : Visibility.Collapsed;
        if (offer == null) return;
        if (PairCodeText.Text != offer.Code) PairCodeText.Text = offer.Code;
        var left = offer.Expires - DateTimeOffset.UtcNow;
        PairExpiryText.Text = $"Diğer bilgisayarda bu kodu girin · {Math.Max(0, (int)left.TotalMinutes)}:{Math.Max(0, left.Seconds):00} kaldı";
    }

    private void RefreshLayout(ShareSnapshot s)
    {
        var items = new List<LayoutItem> { new(s.Local, true, true) };
        items.AddRange(s.Peers.Where(p => p.Device != null).Select(p => new LayoutItem(p.Device!, false, p.Connected)));
        Editor.SetData(items, s.Layout);
        var devices = items.Select(i => i.Device).ToList();
        var loose = s.Peers.Where(p => p.Device != null && !s.Layout.IsConnected(p.Device, devices)).Select(p => p.Name).ToList();
        LayoutHintText.Text = s.Peers.Count == 0 ? "Henüz eşleşmiş cihaz yok. Önce Cihazlar sayfasından eşleştirin."
            : loose.Count > 0 ? $"{string.Join(", ", loose)} hiçbir monitöre değmiyor; imleç oraya geçemez. Sürükleyip bir kenara yaklaştırın."
            : items.Count == 1 ? "Diğer bilgisayarın monitör bilgisi ilk bağlantıda gelir."
            : !s.Peers.Any(p => p.Connected) ? "Düzen kaydedildi. Cihazlar bağlanınca imleç değen kenarlardan geçer."
            : "Hazır. İmleç, birbirine değen kenarlardan geçer.";
        LayoutHintText.Foreground = loose.Count > 0 ? new SolidColorBrush(Color.FromRgb(0xF6, 0xC1, 0x77)) : Muted;
    }

    // ---- Devices page ----

    private void ShowCode_Click(object sender, RoutedEventArgs e)
    {
        if (host == null) return;
        RefreshPairing(host.Network.OpenPairing());
        PairCodeText.Focus(); PairCodeText.SelectAll();
    }

    private void CancelCode_Click(object sender, RoutedEventArgs e) { host?.Network.ClosePairing(); RefreshPairing(null); }

    private void Discovered_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (updatingUi || (DiscoveredList.SelectedItem as ListBoxItem)?.Tag is not DiscoveredDevice d) return;
        AddressBox.Text = d.Endpoint.Port == PeerNetwork.DefaultPort ? d.Endpoint.Address.ToString() : d.Endpoint.ToString();
        CodeBox.Focus();
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (host == null) return;
        if (!TryEndpoint(AddressBox.Text, out var endpoint)) { PairStatusText.Text = "Geçerli bir IPv4 adresi girin (ör. 192.168.1.4)."; return; }
        PairButton.IsEnabled = false; PairStatusText.Text = "Bağlanıyor…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await host.Network.PairAsync(endpoint, CodeBox.Text, timeout.Token);
            CodeBox.Clear();
            PairStatusText.Text = "Eşleştirildi. Monitör düzeni sayfasından yerleşimi ayarlayın.";
        }
        catch (Exception ex) when (ex is FormatException or AuthenticationException) { PairStatusText.Text = ex.Message; }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException)
        { PairStatusText.Text = "Diğer bilgisayara ulaşılamadı. Orada Banko Share açık mı ve Windows güvenlik duvarı izin veriyor mu?"; }
        finally { PairButton.IsEnabled = true; }
    }

    private static bool TryEndpoint(string text, out IPEndPoint endpoint)
    {
        endpoint = null!;
        if (!IPEndPoint.TryParse(text.Trim(), out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(parsed.Address)) return false;
        endpoint = parsed.Port == 0 ? new IPEndPoint(parsed.Address, PeerNetwork.DefaultPort) : parsed;
        return true;
    }

    private void RemovePeer_Click(object sender, RoutedEventArgs e)
    {
        if (host == null || (sender as Button)?.Tag is not string id) return;
        var name = host.Network.Directory.Get(id)?.Name ?? "Bu cihaz";
        if (MessageBox.Show(this, $"{name} kaldırılsın mı? Tekrar kullanmak için yeniden eşleştirmeniz gerekir.", "Banko Share",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            host.Network.Unpair(id);
    }

    // ---- Layout page ----

    private void OnDeviceDropped(string id, PointI offset)
    {
        if (host == null) return;
        // About 28 screen pixels of magnetism, whatever the zoom level.
        var threshold = (int)Math.Ceiling(28 / Math.Max(Editor.Scale, 0.001));
        if (!host.Controller.MoveDevice(id, offset, threshold)) ShowNotice("Monitörler üst üste binemez; konum geri alındı.");
        Refresh();
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e) => host?.Controller.ResetLayout();

    // ---- Switching page ----

    private void Switching_Changed(object sender, RoutedEventArgs e)
    {
        if (!updatingUi) host?.Controller.SetSwitchingEnabled(SwitchingBox.IsChecked == true);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (DevicesPanel == null) return;
        DevicesPanel.Visibility = NavDevices.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LayoutPanel.Visibility = NavLayout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SwitchingPanel.Visibility = NavSwitching.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPanel.Visibility = NavUpdates.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- System events (may arrive on any thread) ----

    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
            host?.Controller.ReturnHome();
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) host?.Controller.ReturnHome();
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        try { host?.RefreshMonitors(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { ShowNotice("Monitör bilgisi okunamadı: " + ex.Message); }
    });

    // ---- Updates page ----

    private void RefreshUpdates()
    {
        UpdateStatus.Text = !updater.Automatic && updater.Phase == UpdatePhase.Idle ? "Otomatik güncelleme kapalı." : updater.Status;
        LastCheckText.Text = updater.LastChecked is { } checkedAt ? $"Son kontrol: {checkedAt:dd.MM.yyyy HH:mm}" : "Açılışta ve her 10 dakikada kontrol edilir.";
        CheckUpdateButton.IsEnabled = updater.Phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Applying or UpdatePhase.Ready);
        ApplyUpdateButton.IsEnabled = updater.Phase == UpdatePhase.Ready && !Busy;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await updater.CheckAsync(lifetime.Token);
    private void ApplyUpdate_Click(object sender, RoutedEventArgs e) => updater.Apply(Busy);

    private async void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (updater == null) return;
        updater.Automatic = AutoUpdate.IsChecked == true;
        try { new UpdatePreferences(updater.Automatic).Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { UpdateStatus.Text = "Tercih bu oturumda değişti; diske kaydedilemedi."; }
        if (IsLoaded && updater.Automatic) await updater.CheckAsync(lifetime.Token);
    }
}
