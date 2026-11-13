using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BankoShare.Core;
using Microsoft.Win32;

namespace BankoShare.App;

public partial class MainWindow : Window
{
    // BankoSpace "Noir" palette.
    private static readonly Brush Connected = Freeze(0x3F, 0xB9, 0x50);
    private static readonly Brush Sharing = Freeze(0x7E, 0xE7, 0x87);
    private static readonly Brush Idle = Freeze(0x5C, 0x5C, 0x5C);
    private static readonly Brush Muted = Freeze(0x72, 0x72, 0x72);
    private static readonly Brush Secondary = Freeze(0xB0, 0xB0, 0xB0);
    private static readonly Brush Warning = Freeze(0xD2, 0x99, 0x22);
    private static readonly Brush Danger = Freeze(0xF8, 0x51, 0x49);
    private readonly ShareHost? host;
    private readonly GithubUpdates updates = new();
    private readonly UpdateCoordinator updater;
    private readonly TrayIcon tray;
    private readonly ClipboardSync? clipboard;
    /// <summary>Why this machine is paused right now (a game or another sharing program), or null.</summary>
    private string? pauseReason, pauseTrayText;
    private string? announcedOtherApp;
    private readonly SettingsStore appStore;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer updatePoll = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppPreferences preferences;
    private RegisteredWaitHandle? showWait;
    private bool refreshQueued, updatingUi, exiting, disposed;
    // Null forces the first render, including the empty-list messages.
    private string? peersSignature, discoveredSignature;
    private SwitchOptions? shownOptions;
    private (DateTimeOffset At, string Text) addresses = (DateTimeOffset.MinValue, "");
    private DateTimeOffset noticeUntil;

    public MainWindow()
    {
        InitializeComponent();
        // Before anything creates the window handle (the clipboard watcher does), or the dark title bar is never applied.
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        updater = new UpdateCoordinator(updates) { Automatic = UpdatePreferences.Load().Automatic };
        VersionText.Text = UpdateVersionText.Text = $"v{updater.CurrentVersion}";
        AutoUpdate.IsChecked = updater.Automatic;
        try { host = ShareHost.Start(updater.CurrentVersion); }
        catch (SocketException)
        { ShowFatal($"Bağlantı noktası {PeerNetwork.DefaultPort} başka bir uygulama tarafından kullanılıyor. Banko Share'in başka bir kopyası açık olabilir."); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        { ShowFatal("Banko Share başlatılamadı: " + e.Message); }
        appStore = new SettingsStore(host?.SettingsPath ?? ShareHost.SettingsDirectory);
        preferences = appStore.Load(AppPreferences.FileName, () => new AppPreferences());
        // Installed copies start with Windows by default; the choice is remembered once made.
        if (updates.IsInstalled && !preferences.AutostartConfigured && host?.IsTestInstance != true)
        {
            TrySetAutostart(true);
            SavePreferences(preferences with { AutostartConfigured = true });
        }
        tray = new TrayIcon(autostartAvailable: updates.IsInstalled);
        tray.OpenRequested += ShowFromTray;
        tray.SwitchingToggled += () => host?.Controller.SetSwitchingEnabled(!host.Controller.Options.SwitchingEnabled);
        tray.HomeRequested += () => host?.Controller.ReturnHome();
        tray.AutostartToggled += enabled => { TrySetAutostart(enabled); Refresh(); };
        tray.ExitRequested += ExitApplication;
        if (host != null)
        {
            host.Controller.Changed += QueueRefresh;
            host.Controller.Notice += message => Dispatcher.BeginInvoke(() => ShowNotice(message));
            host.Network.Paired += _ => Dispatcher.BeginInvoke(() => { ShowNotice("Yeni cihaz eşleştirildi."); NavLayout.IsChecked = true; });
            Editor.DeviceDropped += OnDeviceDropped;
            host.Games.Changed += () => Dispatcher.BeginInvoke(ApplyGameMode);
            host.Controller.PeerLogReceived += (id, text) => Dispatcher.BeginInvoke(() => SavePeerLog(id, text));
            clipboard = new ClipboardSync(host, this);
            clipboard.Status += ShowNotice;
            clipboard.Announce += (title, text) => { if (!host.Suspended) tray.ShowNotice(title, text); };
            if (host.Warning != null) { WarningText.Text = host.Warning; WarningText.Visibility = Visibility.Visible; }
        }
        // A second launch (Start menu, desktop shortcut) brings this window back instead of starting another copy.
        if (App.ShowSignal != null)
            showWait = ThreadPool.RegisterWaitForSingleObject(App.ShowSignal, (_, _) => Dispatcher.BeginInvoke(ShowFromTray), null, Timeout.Infinite, false);
        updater.Changed += RefreshUpdates;
        updates.RestartHidden = () => !IsVisible;
        updates.RestartPending = pending => tray.Visible = !pending;
        updatePoll.Tick += async (_, _) => { if (updater.Automatic) await updater.CheckAsync(lifetime.Token); };
        tick.Tick += (_, _) => { ApplyGameMode(); updater.Tick(DateTimeOffset.Now, Busy); RefreshUpdates(); Refresh(); };
        // Not ContentRendered: when Windows starts us in the tray the window is never rendered.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            updatePoll.Start(); tick.Start();
            if (updater.Automatic) await updater.CheckAsync(lifetime.Token);
            else UpdateStatus.Text = "Otomatik güncelleme kapalı.";
        });
        Closing += (_, e) =>
        {
            if (exiting) return;
            // Closing the window keeps sharing running in the notification area.
            e.Cancel = true;
            HideToTray();
        };
        Application.Current.SessionEnding += (_, _) => exiting = true;
        Application.Current.Exit += (_, _) => Shutdown();
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => Shutdown();
        Refresh();
    }

    private bool Busy => host?.Controller.IsBusy == true;

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void ShowFatal(string message)
    {
        StatusText.Text = message; StatusDot.Fill = Danger;
        DevicesPanel.IsEnabled = LayoutPanel.IsEnabled = SwitchingPanel.IsEnabled = false;
    }

    private void ShowNotice(string message) { NoticeText.Text = message; noticeUntil = DateTimeOffset.Now.AddSeconds(8); }

    // ---- Notification area and lifetime ----

    private void HideToTray()
    {
        Hide();
        if (preferences.TrayNoticeShown) return;
        tray.ShowNotice("Banko Share arka planda çalışıyor", "Fare ve klavye paylaşımı sürüyor. Açmak için simgeye tıklayın; kapatmak için sağ tıklayıp Çıkış'ı seçin.");
        SavePreferences(preferences with { TrayNoticeShown = true });
    }

    private void ShowFromTray()
    {
        if (exiting) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        // Windows only lets a window come to the front this way while the user is interacting with us.
        Topmost = true; Topmost = false;
        Activate();
    }

    private void ExitApplication()
    {
        exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>Stops sharing and removes the tray icon. Runs once, whichever way the app ends.</summary>
    private void Shutdown()
    {
        if (disposed) return;
        disposed = true;
        exiting = true;
        updatePoll.Stop(); tick.Stop(); lifetime.Cancel();
        showWait?.Unregister(null);
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        tray.Dispose();
        clipboard?.Dispose();
        host?.Dispose();
    }

    private void TrySetAutostart(bool enabled)
    {
        if (!updates.IsInstalled) return;
        try { Autostart.Set(enabled); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { ShowNotice("Windows başlangıç ayarı değiştirilemedi."); }
    }

    private void SavePreferences(AppPreferences next)
    {
        preferences = next;
        try { appStore.Save(AppPreferences.FileName, next); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Asked again next time. */ }
    }

    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (updatingUi) return;
        TrySetAutostart(AutostartBox.IsChecked == true);
        Refresh();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    /// <summary>
    /// Steps aside while a locked game is in front (or runs, if so chosen) or another sharing program (ShareMouse…)
    /// is open: two programs hooking the same mouse fight over every drag and freeze the pointer. Comes back on its own.
    /// </summary>
    private void ApplyGameMode()
    {
        if (host == null || disposed) return;
        var o = host.Controller.Options;
        host.Games.Configure(o.PauseForProtectedGames, o.ExtraGames);
        var game = host.Games.RunningGame;
        var whileRunning = o.GameLock == GameLockMode.WhileRunning;
        var locked = game != null && (whileRunning || host.Games.InFront);
        var other = host.Games.OtherSharingApp;
        GameStateText.Text = game == null ? "" : locked ? $"{game} · kilitli" : $"{game} arkada · geçiş açık";
        GameStateText.Foreground = locked ? Warning : Sharing;
        (pauseReason, pauseTrayText) = other != null
            ? ($"{other} açık · iki paylaşım programı aynı fareyi yönetirse imleç donar; Banko Share duraklatıldı, {other}'ı kapatın", $"Banko Share · {other} açık, duraklatıldı")
            : locked && whileRunning ? ($"Oyun kilidi · {game} açık; kapanana kadar imleç ve klavye bu bilgisayarda kalır", $"Banko Share · oyun kilidi ({game} açık)")
            : locked ? ($"Oyun kilidi · {game} önde; diğer bilgisayara geçmek için Alt+Tab ile oyundan çıkın", $"Banko Share · oyun kilidi ({game} önde)")
            : (null, null);
        var pause = pauseReason != null;
        if (other != null && other != announcedOtherApp && !locked)
            tray.ShowNotice($"{other} açık", $"Banko Share, {other} ile aynı anda çalışamaz ve duraklatıldı. Banko Share'i kullanmak için {other}'ı kapatın; Banko Share kendiliğinden devam eder.");
        announcedOtherApp = other;
        if (pause == host.Suspended) return;
        try { host.SetSuspended(pause); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { ShowNotice("Fare kancası yeniden kurulamadı: " + e.Message); return; }
        // Alt+Tab in and out of a game is routine: the status line says enough. The rest is worth a notice.
        if (other != null) ShowNotice($"{other} açık: Banko Share duraklatıldı.");
        else if (pause && whileRunning) ShowNotice($"{game} açık: oyun kilidi devrede.");
        Refresh();
    }

    private void SavePeerLog(string id, string text)
    {
        var name = host?.Network.Directory.Get(id)?.Name ?? id[..8];
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Diag.FilePath) ?? System.IO.Path.Combine(ShareHost.SettingsDirectory, "logs");
            var safe = new string([.. name.Select(ch => ClipboardContent.IsSafeName(ch.ToString()) ? ch : '_')]);
            File.WriteAllText(System.IO.Path.Combine(folder, $"karşı-{safe}.log"), text);
            ShowNotice($"{name} bilgisayarının kaydı alındı.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowNotice("Kayıt yazılamadı: " + e.Message); }
    }

    private void OpenReceived_Click(object sender, RoutedEventArgs e)
    {
        if (clipboard == null) return;
        Directory.CreateDirectory(clipboard.ReceivedFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{clipboard.ReceivedFolder}\"") { UseShellExecute = true });
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (System.IO.Path.GetDirectoryName(Diag.FilePath) is not { } folder) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void FetchLogs_Click(object sender, RoutedEventArgs e)
    {
        var asked = host?.Controller.RequestPeerLogs() ?? 0;
        ShowNotice(asked == 0 ? "Kayıt istenecek bağlı bir bilgisayar yok (diğer bilgisayarda 0.5 veya üstü olmalı)." : "Diğer bilgisayarların kayıtları isteniyor…");
    }

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
        if (disposed) return;
        if (DateTimeOffset.Now > noticeUntil) NoticeText.Text = "";
        var autostart = updates.IsInstalled && Autostart.IsEnabled();
        updatingUi = true;
        AutostartBox.IsChecked = autostart;
        AutostartBox.IsEnabled = updates.IsInstalled;
        updatingUi = false;
        AutostartNote.Text = updates.IsInstalled
            ? "Oturum açınca Banko Share bildirim alanında başlar ve eşleşmiş bilgisayarlara bağlanır."
            : "Bu kopya kurulu değil; Windows ile başlatma yalnızca Setup ile kurulan sürümde kullanılır.";
        if (host == null) { tray.Update(TrayState.Idle, "Banko Share · başlatılamadı", false, false, autostart); return; }
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
        if (!ReferenceEquals(s.Options, shownOptions)) ShowOptions(s.Options);
        var connected = s.Peers.Count(p => p.Connected);
        (StatusText.Text, StatusDot.Fill) = s switch
        {
            { Paused: true } => (pauseReason ?? "Banko Share bu bilgisayarda duraklatıldı", Warning),
            { ControllingName: { } name } => ($"Fare ve klavye şu an {name} bilgisayarında · geri dönmek için Esc Esc", Sharing),
            { ControlledByName: { } name } => ($"{name} bu bilgisayarı kontrol ediyor · fareyi oynatarak geri alın", Sharing),
            { Options.SwitchingEnabled: false } => ("Ekran kenarından geçiş kapalı", Idle),
            _ when connected > 0 => ($"{connected} cihaz bağlı · imleci değen kenara iterek geçin", Connected),
            _ when s.Peers.Count > 0 => ("Eşleşmiş cihaz bekleniyor · iki bilgisayarda da Banko Share açık olmalı", Idle),
            _ => ("Henüz eşleşmiş cihaz yok", Idle)
        };
        var trayState = s.Paused ? TrayState.Idle : s.ControllingName != null || s.ControlledByName != null ? TrayState.Sharing : connected > 0 ? TrayState.Connected : TrayState.Idle;
        var trayText = s switch
        {
            { Paused: true } => pauseTrayText ?? "Banko Share · duraklatıldı",
            { ControllingName: { } name } => $"Banko Share · fare {name} bilgisayarında",
            { ControlledByName: { } name } => $"Banko Share · {name} kontrol ediyor",
            _ when connected > 0 => $"Banko Share · {string.Join(", ", s.Peers.Where(p => p.Connected).Select(p => p.Name))} bağlı",
            _ => "Banko Share · bağlı cihaz yok"
        };
        tray.Update(trayState, trayText, s.Options.SwitchingEnabled, s.ControllingName != null || s.ControlledByName != null, autostart);
    }

    private void RefreshPeers(ShareSnapshot s)
    {
        var signature = string.Join("|", s.Peers.Select(p => $"{p.Id}:{p.Name}:{p.Connected}:{p.Paused}:{p.Version}:{p.Address}"));
        if (signature == peersSignature) return;
        peersSignature = signature;
        PeersPanel.Children.Clear();
        if (s.Peers.Count == 0)
        {
            PeersPanel.Children.Add(new TextBlock { Text = "Henüz eşleşmiş cihaz yok. Aşağıdan ekleyin.", Foreground = Secondary });
            return;
        }
        foreach (var p in s.Peers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var remove = new Button { Content = "Kaldır", Style = (Style)FindResource("Secondary"), Margin = new Thickness(10, 0, 0, 0), Tag = p.Id };
            remove.Click += RemovePeer_Click;
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = p.Connected ? Connected : Idle, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = (p.Connected ? p.Paused ? "Bağlı · şu an kilitli (oyun önde ya da başka paylaşım programı açık)" : "Bağlı" : "Bağlı değil") + (p.Version != null ? " · v" + p.Version.Split('+')[0] : "") + (p.Version != null && !Versions.AtLeast(p.Version, 0, 5) ? " · eski sürüm, güncellenmeli" : "") + (p.Address != null ? " · " + p.Address : ""), Foreground = Muted, FontSize = 12 });
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
            var item = new ListBoxItem { Content = $"{d.Name}  ·  {d.Endpoint.Address}", Tag = d };
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
        LayoutHintText.Foreground = loose.Count > 0 ? Warning : Secondary;
    }

    // ---- Devices page ----

    private void ShowCode_Click(object sender, RoutedEventArgs e)
    {
        if (host == null) return;
        RefreshPairing(host.Network.OpenPairing());
        PairCodeText.Focus(); PairCodeText.SelectAll();
    }

    private void CancelCode_Click(object sender, RoutedEventArgs e) { host?.Network.ClosePairing(); RefreshPairing(null); }

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !PairButton.IsEnabled) return;
        e.Handled = true;
        Pair_Click(sender, e);
    }

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

    private void ShowOptions(SwitchOptions o)
    {
        shownOptions = o;
        updatingUi = true;
        SwitchingBox.IsChecked = o.SwitchingEnabled;
        EqualizeBox.IsChecked = o.EqualizeEdges;
        WrapBox.IsChecked = o.Wrap;
        FullscreenBox.IsChecked = o.BlockFullscreen;
        MaximizedBox.IsChecked = o.BlockMaximized;
        CornersBox.IsChecked = o.BlockCorners;
        GamesBox.IsChecked = o.PauseForProtectedGames;
        GameLockPanel.IsEnabled = o.PauseForProtectedGames;
        (o.GameLock == GameLockMode.WhileRunning ? GameLockRunning : GameLockFront).IsChecked = true;
        if (!GamesListBox.IsKeyboardFocusWithin) GamesListBox.Text = ProgramList(o.ExtraGames);
        ClipboardBox.IsChecked = o.ShareClipboard;
        FilesBox.IsChecked = o.ShareFiles;
        FilesBox.IsEnabled = o.ShareClipboard;
        (o.RequiredKey switch { SwitchKey.Shift => KeyShift, SwitchKey.Ctrl => KeyCtrl, SwitchKey.Alt => KeyAlt, _ => KeyNone }).IsChecked = true;
        (o.DoubleTap switch { DoubleTapMode.OverrideBlocks => TapOverride, DoubleTapMode.Always => TapAlways, _ => TapOff }).IsChecked = true;
        (o.JumpMods switch { KeyMods.Ctrl | KeyMods.Alt => JumpCtrlAlt, KeyMods.Ctrl | KeyMods.Shift => JumpCtrlShift, _ => JumpOff }).IsChecked = true;
        // Never overwrite what the user is typing or recording.
        if (!ProgramsBox.IsKeyboardFocusWithin) ProgramsBox.Text = string.Join(", ", o.BlockedPrograms.Select(p => p + ".exe"));
        foreach (var box in (TextBox[])[ToggleHotkeyBox, NextHotkeyBox, HomeHotkeyBox])
            if (!box.IsKeyboardFocusWithin) box.Text = HotkeyText.Describe(HotkeyOf(o, box));
        updatingUi = false;
    }

    /// <summary>Each control changes only its own field, so a hotkey toggle made meanwhile is kept.</summary>
    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (updatingUi || host == null) return;
        static bool On(CheckBox box) => box.IsChecked == true;
        host.Controller.UpdateOptions(o =>
            sender == SwitchingBox ? o with { SwitchingEnabled = On(SwitchingBox) }
            : sender == EqualizeBox ? o with { EqualizeEdges = On(EqualizeBox) }
            : sender == WrapBox ? o with { Wrap = On(WrapBox) }
            : sender == FullscreenBox ? o with { BlockFullscreen = On(FullscreenBox) }
            : sender == MaximizedBox ? o with { BlockMaximized = On(MaximizedBox) }
            : sender == CornersBox ? o with { BlockCorners = On(CornersBox) }
            : sender == GamesBox ? o with { PauseForProtectedGames = On(GamesBox) }
            : sender == GameLockFront ? o with { GameLock = GameLockMode.InFront }
            : sender == GameLockRunning ? o with { GameLock = GameLockMode.WhileRunning }
            : sender == ClipboardBox ? o with { ShareClipboard = On(ClipboardBox) }
            : sender == FilesBox ? o with { ShareFiles = On(FilesBox) }
            : sender == KeyNone ? o with { RequiredKey = SwitchKey.None }
            : sender == KeyShift ? o with { RequiredKey = SwitchKey.Shift }
            : sender == KeyCtrl ? o with { RequiredKey = SwitchKey.Ctrl }
            : sender == KeyAlt ? o with { RequiredKey = SwitchKey.Alt }
            : sender == TapOff ? o with { DoubleTap = DoubleTapMode.Off }
            : sender == TapOverride ? o with { DoubleTap = DoubleTapMode.OverrideBlocks }
            : sender == TapAlways ? o with { DoubleTap = DoubleTapMode.Always }
            : sender == JumpOff ? o with { JumpMods = KeyMods.None }
            : sender == JumpCtrlAlt ? o with { JumpMods = KeyMods.Ctrl | KeyMods.Alt }
            : sender == JumpCtrlShift ? o with { JumpMods = KeyMods.Ctrl | KeyMods.Shift }
            : o);
        if (sender == JumpCtrlAlt || sender == JumpCtrlShift) CheckHotkeyConflicts();
        if (sender == GamesBox || sender == GameLockFront || sender == GameLockRunning) ApplyGameMode();
    }

    private static string ProgramList(IEnumerable<string> names) => string.Join(", ", names.Select(p => p + ".exe"));

    private static string[] ParsePrograms(string text) =>
        SwitchOptions.NormalizePrograms(text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    private void ApplyPrograms()
    {
        if (host == null) return;
        var list = ParsePrograms(ProgramsBox.Text);
        var applied = list.SequenceEqual(host.Controller.Options.BlockedPrograms)
            ? host.Controller.Options
            : host.Controller.UpdateOptions(o => o with { BlockedPrograms = list });
        ProgramsBox.Text = ProgramList(applied.BlockedPrograms);
    }

    private void Programs_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyPrograms();

    private void Programs_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyPrograms();
        e.Handled = true;
    }

    private void AddProgram_Click(object sender, RoutedEventArgs e) =>
        ShowProgramMenu(AddProgramButton, host?.Controller.Options.BlockedPrograms ?? [], name =>
        {
            var applied = host?.Controller.UpdateOptions(o => o with { BlockedPrograms = [.. o.BlockedPrograms, name] });
            if (applied != null) ProgramsBox.Text = ProgramList(applied.BlockedPrograms);
        });

    private void ApplyGames()
    {
        if (host == null) return;
        var list = ParsePrograms(GamesListBox.Text);
        var applied = list.SequenceEqual(host.Controller.Options.ExtraGames)
            ? host.Controller.Options
            : host.Controller.UpdateOptions(o => o with { ExtraGames = list });
        GamesListBox.Text = ProgramList(applied.ExtraGames);
        ApplyGameMode();
    }

    private void Games_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyGames();

    private void Games_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyGames();
        e.Handled = true;
    }

    private void AddGame_Click(object sender, RoutedEventArgs e) =>
        ShowProgramMenu(AddGameButton, host?.Controller.Options.ExtraGames ?? [], name =>
        {
            var applied = host?.Controller.UpdateOptions(o => o with { ExtraGames = [.. o.ExtraGames, name] });
            if (applied == null) return;
            GamesListBox.Text = ProgramList(applied.ExtraGames);
            ApplyGameMode();
        });

    private static void ShowProgramMenu(Button anchor, string[] already, Action<string> add)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (name, title) in RunningPrograms())
        {
            // A TextBlock header keeps underscores in names from turning into access keys.
            var item = new MenuItem { Header = new TextBlock { Text = $"{name}.exe  ·  {title}", MaxWidth = 460, TextTrimming = TextTrimming.CharacterEllipsis }, IsEnabled = !already.Contains(name) };
            item.Click += (_, _) => add(name);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Pencereli program bulunamadı", IsEnabled = false });
        menu.IsOpen = true;
    }

    /// <summary>Processes with a titled main window, other than this one, by executable name.</summary>
    private static List<(string Name, string Title)> RunningPrograms()
    {
        List<(string Name, string Title)> found = [];
        foreach (var process in Process.GetProcesses())
            using (process)
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == 0 || string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                    found.Add((SwitchOptions.NormalizeProgram(process.ProcessName), process.MainWindowTitle.Trim()));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { /* Exited meanwhile or inaccessible. */ }
        return [.. found.Where(p => p.Name.Length > 0).GroupBy(p => p.Name).Select(g => g.First()).OrderBy(p => p.Name, StringComparer.Ordinal)];
    }

    // ---- Hotkey recording ----

    private static Hotkey? HotkeyOf(SwitchOptions o, TextBox box) => (string)box.Tag switch
    {
        "Toggle" => o.ToggleHotkey,
        "Next" => o.NextDeviceHotkey,
        _ => o.HomeHotkey
    };

    private void Hotkey_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        host?.Controller.SuspendHotkeys(true);
        ((TextBox)sender).Text = "Tuş birleşimine basın…";
    }

    private void Hotkey_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        host?.Controller.SuspendHotkeys(false);
        if (host != null) ((TextBox)sender).Text = HotkeyText.Describe(HotkeyOf(host.Controller.Options, (TextBox)sender));
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return; // normal focus navigation
        e.Handled = true;
        if (host == null) return;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { Keyboard.ClearFocus(); return; }
        if (key is Key.Delete or Key.Back) { SaveHotkey(box, null); return; }
        // Same rule as the hook: only the left Alt counts, and AltGr's synthetic Ctrl does not.
        var mods = VirtualKeys.ModsFrom(vk => Keyboard.IsKeyDown(KeyInterop.KeyFromVirtualKey(vk)));
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (VirtualKeys.IsModifier(vk)) { box.Text = mods == KeyMods.None ? "Tuş birleşimine basın…" : HotkeyText.Mods(mods) + " + …"; return; }
        var hotkey = new Hotkey(mods, vk);
        if (!hotkey.IsValid) { box.Text = "En az iki değiştirici: Ctrl, sol Alt, Shift"; return; }
        SaveHotkey(box, hotkey);
    }

    private void SaveHotkey(TextBox box, Hotkey? hotkey)
    {
        var tag = (string)box.Tag;
        var o = host!.Controller.Options;
        TextBox[] others = [.. ((TextBox[])[ToggleHotkeyBox, NextHotkeyBox, HomeHotkeyBox]).Where(b => b != box)];
        if (hotkey != null && (others.Any(b => HotkeyOf(o, b) == hotkey) || o.JumpMods != KeyMods.None && hotkey.Mods == o.JumpMods && VirtualKeys.DigitIndex(hotkey.Vk) >= 0))
        {
            box.Text = "Bu birleşim başka bir kısayolda kullanılıyor";
            return;
        }
        host.Controller.UpdateOptions(x => tag switch
        {
            "Toggle" => x with { ToggleHotkey = hotkey },
            "Next" => x with { NextDeviceHotkey = hotkey },
            _ => x with { HomeHotkey = hotkey }
        });
        Keyboard.ClearFocus(); // recording ends; the new hotkey works right away
        CheckHotkeyConflicts();
    }

    /// <summary>Warns when a recorded hotkey is also one of the jump combinations.</summary>
    private void CheckHotkeyConflicts()
    {
        var o = host?.Controller.Options;
        var clash = o != null && o.JumpMods != KeyMods.None && ((Hotkey?[])[o.ToggleHotkey, o.NextDeviceHotkey, o.HomeHotkey])
            .Any(h => h != null && h.Mods == o.JumpMods && VirtualKeys.DigitIndex(h.Vk) >= 0);
        HotkeyStatusText.Text = clash ? "Bir kısayol, monitöre atlama birleşimiyle çakışıyor; çakışan kısayol önceliklidir." : "";
        HotkeyStatusText.Visibility = clash ? Visibility.Visible : Visibility.Collapsed;
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
