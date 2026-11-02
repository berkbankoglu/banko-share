using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Windows;
using BankoShare.Core;
using Microsoft.Win32;
using System.Windows.Threading;

namespace BankoShare.App;

public partial class MainWindow : Window
{
    private const int Port = 24819;
    private CancellationTokenSource? session;
    private SecureConnection? connection;
    private TcpListener? listener;
    private InputCapture? capture;
    private readonly InputInjector injector = new();
    private Channel<InputEvent>? queue;
    private Task? running;
    private bool closing;
    private UpdateCoordinator? updater;
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly DispatcherTimer updatePoll = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer updateTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool SessionBusy => session != null || running is { IsCompleted: false };
    public MainWindow()
    {
        InitializeComponent();
        updater = new UpdateCoordinator(new GithubUpdates()) { Automatic = UpdatePreferences.Load().Automatic };
        VersionText.Text = $"v{updater.CurrentVersion}";
        AutoUpdate.IsChecked = updater.Automatic;
        updater.Changed += RefreshUpdates;
        updatePoll.Tick += async (_, _) => { if (updater.Automatic) await updater.CheckAsync(updateLifetime.Token); };
        updateTick.Tick += (_, _) => { updater.Tick(DateTimeOffset.Now, SessionBusy); RefreshUpdates(); };
        ContentRendered += async (_, _) =>
        {
            updatePoll.Start(); updateTick.Start();
            if (updater.Automatic) await updater.CheckAsync(updateLifetime.Token);
            else { UpdateStatus.Text = "Otomatik güncelleme kapalı."; }
        };
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        Closed += (_, _) =>
        {
            updatePoll.Stop(); updateTick.Stop(); updateLifetime.Cancel();
            SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
        };
        Closing += async (_, e) =>
        {
            if (closing || running == null || running.IsCompleted) return;
            e.Cancel = true; Stop();
            await running; closing = true; Close();
        };
        Deactivated += (_, _) => { if (capture?.Active == true) ToggleControl(); };
    }
    private void RefreshUpdates()
    {
        if (updater == null) return;
        UpdateStatus.Text = !updater.Automatic && updater.Phase == UpdatePhase.Idle ? "Otomatik güncelleme kapalı." : updater.Status;
        LastCheckText.Text = updater.LastChecked is { } checkedAt ? $"Son kontrol: {checkedAt:dd.MM.yyyy HH:mm}" : "Açılışta ve her 10 dakikada kontrol edilir.";
        CheckUpdateButton.IsEnabled = updater.Phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Applying or UpdatePhase.Ready);
        ApplyUpdateButton.IsEnabled = updater.Phase == UpdatePhase.Ready && !SessionBusy;
        if (updater.Phase == UpdatePhase.Applying) Busy(true);
        else if (!SessionBusy) Busy(false);
    }
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    { if (updater != null) await updater.CheckAsync(updateLifetime.Token); }
    private void ApplyUpdate_Click(object sender, RoutedEventArgs e) => updater?.Apply(SessionBusy);
    private async void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (updater == null) return;
        updater.Automatic = AutoUpdate.IsChecked == true;
        try { new UpdatePreferences(updater.Automatic).Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { UpdateStatus.Text = "Tercih bu oturumda değişti; diske kaydedilemedi."; }
        if (IsLoaded && updater.Automatic) await updater.CheckAsync(updateLifetime.Token);
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    { if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect) Dispatcher.BeginInvoke(Stop); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(Stop); }
    private void Busy(bool busy)
    {
        ListenButton.IsEnabled = ConnectButton.IsEnabled = HostBox.IsEnabled = CodeBox.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
        if (!busy) ControlButton.IsEnabled = false;
    }
    private void Listen_Click(object sender, RoutedEventArgs e) => running = RunReceiver();
    private void Connect_Click(object sender, RoutedEventArgs e) => running = RunSender();
    private async Task RunReceiver()
    {
        session = new(); var token = session.Token; Busy(true);
        try
        {
            using var pairing = new Pairing();
            PairCode.Text = pairing.Code;
            var addresses = Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork);
            AddressText.Text = $"Adres: {string.Join(" / ", addresses)} · Port: {Port}";
            listener = new TcpListener(IPAddress.Any, Port); listener.Start(1);
            StatusText.Text = "Alıcı açık. Eşleştirilmiş bilgisayar bekleniyor.";
            capture = new InputCapture(_ => true, Stop);
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                try { connection = await SecureConnection.AcceptAsync(client, pairing, token); break; }
                catch (Exception) when (!token.IsCancellationRequested)
                { StatusText.Text = "Geçersiz veya zaman aşımına uğrayan bağlantı reddedildi. Bekleniyor."; }
            }
            token.ThrowIfCancellationRequested();
            listener.Stop(); listener = null; PairCode.Clear();
            StatusText.Text = "Bağlandı. Bu bilgisayar kontrol edilebilir. Fiziksel F12 ile durdurun.";
            while (!token.IsCancellationRequested)
            {
                var input = await connection!.ReceiveAsync(token);
                // A queued continuation may run after Stop was clicked or F12 ended the session.
                token.ThrowIfCancellationRequested();
                injector.Apply(input);
                await connection.SendAsync(new(EventKind.Heartbeat), token);
            }
        }
        catch (OperationCanceledException) { StatusText.Text = token.IsCancellationRequested ? "Oturum durduruldu." : "Bağlantı zaman aşımına uğradı. Kontrol bırakıldı."; }
        catch (Exception ex) { StatusText.Text = $"Bağlantı kapandı: {ex.Message}"; }
        finally { Cleanup(); }
    }
    private async Task RunSender()
    {
        if (!IPAddress.TryParse(HostBox.Text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        { StatusText.Text = "Diğer bilgisayarın IPv4 adresini girin."; return; }
        session = new(); var token = session.Token; Busy(true);
        Task? heartbeat = null, reader = null;
        try
        {
            StatusText.Text = "Şifreli bağlantı kuruluyor…";
            connection = await SecureConnection.ConnectAsync(address.ToString(), Port, CodeBox.Text, token);
            CodeBox.Clear();
            queue = Channel.CreateBounded<InputEvent>(new BoundedChannelOptions(512) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            capture = new InputCapture(Enqueue, ToggleControl);
            ControlButton.IsEnabled = true;
            StatusText.Text = "Güvenli bağlantı hazır. F12 ile uzak kontrolü açın.";
            heartbeat = Heartbeats(token); reader = MonitorPeer(token);
            await foreach (var input in queue.Reader.ReadAllAsync(token)) await connection.SendAsync(input, token);
        }
        catch (OperationCanceledException) { StatusText.Text = "Oturum durduruldu; yerel kontrol geri alındı."; }
        catch (Exception ex) { StatusText.Text = $"Bağlantı kapandı: {ex.Message}"; }
        finally
        {
            session.Cancel(); connection?.Dispose();
            if (heartbeat != null) try { await heartbeat; } catch (OperationCanceledException) { }
            if (reader != null) try { await reader; } catch (OperationCanceledException) { }
            Cleanup();
        }
    }
    private async Task Heartbeats(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(token)) if (!Enqueue(new(EventKind.Heartbeat))) break;
    }
    private async Task MonitorPeer(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
                if ((await connection!.ReceiveAsync(token)).Kind != EventKind.Heartbeat) throw new InvalidDataException("Beklenmeyen alıcı yanıtı.");
        }
        catch (Exception) { Stop(); }
    }
    private bool Enqueue(InputEvent input)
    {
        if (session?.IsCancellationRequested == false && queue?.Writer.TryWrite(input) == true) return true;
        Stop(); return false;
    }
    private void ToggleControl()
    {
        if (capture == null || !ControlButton.IsEnabled) return;
        var current = capture;
        current.SetActive(!current.Active);
        ControlButton.Content = current.Active ? "Yerel kontrole dön (F12)" : "Kontrolü aç (F12)";
        StatusText.Text = current.Active ? "UZAK KONTROL AÇIK · F12 ile bu bilgisayara dönün." : "Yerel kontrol açık. F12 ile diğer bilgisayara geçin.";
    }
    private void Control_Click(object sender, RoutedEventArgs e) => ToggleControl();
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();
    private void Stop()
    {
        capture?.Dispose(); capture = null;
        session?.Cancel(); listener?.Stop(); connection?.Dispose();
    }
    private void Cleanup()
    {
        Stop(); connection = null; listener = null; queue = null;
        session?.Dispose(); session = null; PairCode.Clear();
        try { injector.ReleaseAll(); }
        catch (Exception) { StatusText.Text += " Bazı girişler bırakılamadı; basılı kalan tuşları fiziksel klavyede bırakın."; }
        ControlButton.Content = "Kontrolü aç (F12)"; Busy(false);
    }
}
