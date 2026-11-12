using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BankoShare.Core;

namespace BankoShare.App;

/// <summary>
/// The clipboard follows the pointer: when it moves from this machine to another (we entered it, or it took the
/// pointer back from us), what was copied here since the last exchange goes there, so Ctrl+V works on that side.
/// Text (plain, HTML, RTF), images and files/folders are supported; bytes travel over a separate TLS connection.
/// <para>
/// Only changes made after the app started are shared, and nothing goes back to the machine it came from.
/// Clipboard access happens on the UI (STA) thread; transfers run on the thread pool.
/// </para>
/// </summary>
internal sealed class ClipboardSync : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const long BigTransfer = 20L * 1024 * 1024;
    private readonly ShareHost host;
    private readonly Dispatcher dispatcher;
    private readonly HwndSource source;
    private readonly nint handle;
    private readonly Dictionary<string, long> synced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> outgoing = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnedOld = new(StringComparer.Ordinal);
    private long version;
    private string? ownWriteFrom;
    private long ownWriteTick;

    /// <summary>Short status for the status bar (UI thread).</summary>
    public event Action<string>? Status;
    /// <summary>A large transfer finished or failed: title, text (UI thread). Shown as a tray notice.</summary>
    public event Action<string, string>? Announce;

    /// <summary>Where received files wait to be pasted; old ones are removed after three days.</summary>
    public string ReceivedFolder => Path.Combine(host.SettingsPath, "Alınan dosyalar");

    public ClipboardSync(ShareHost host, Window window)
    {
        this.host = host;
        dispatcher = window.Dispatcher;
        // The window may never be shown (started in the tray); its handle still receives clipboard updates.
        handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        source.AddHook(WndProc);
        AddClipboardFormatListener(handle);
        host.Controller.PointerHandedOff += peer => dispatcher.BeginInvoke(() => HandOff(peer));
        host.Network.TransferHandler = ReceiveAsync;
        _ = Task.Run(CleanOldReceived);
    }

    private nint WndProc(nint hwnd, int message, nint w, nint l, ref bool handled)
    {
        if (message == WmClipboardUpdate) OnClipboardChanged();
        return 0;
    }

    private void OnClipboardChanged()
    {
        version++;
        // Our own write (one or two updates right after it): the source machine already has this content.
        if (ownWriteFrom is not { } from || Environment.TickCount64 - ownWriteTick > 1500) return;
        synced[from] = version;
        // The pointer has meanwhile moved on to a third machine: pass it along.
        if (host.Controller.ControllingPeer is { } current && current != from) HandOff(current);
    }

    private void HandOff(string peer)
    {
        try { TryHandOff(peer); }
        catch (Exception e) { Diag.Write($"Pano okunamadı: {e.GetType().Name}: {e.Message}"); }
    }

    private void TryHandOff(string peer)
    {
        var options = host.Controller.Options;
        if (!options.ShareClipboard || synced.GetValueOrDefault(peer) == version) return;
        if (!Versions.AtLeast(host.Controller.PeerVersion(peer), 0, 5))
        {
            if (warnedOld.Add(peer)) Status?.Invoke($"{PeerName(peer)} eski bir sürüm kullanıyor; pano paylaşımı için orada da güncelleme gerekiyor.");
            return;
        }
        synced[peer] = version;
        var copied = ClipboardIO.Read(options.ShareFiles);
        if (copied == null) return;
        if (outgoing.Remove(peer, out var previous)) previous.Cancel();
        var cancel = new CancellationTokenSource();
        outgoing[peer] = cancel;
        _ = SendAsync(peer, copied.Value, cancel);
    }

    private async Task SendAsync(string peer, CopiedClipboard copied, CancellationTokenSource cancel)
    {
        var name = PeerName(peer);
        try
        {
            ClipboardFiles.Collected? files = null;
            if (copied.Paths != null)
            {
                string? problem = null;
                files = await Task.Run(() => ClipboardFiles.Collect(copied.Paths, out problem));
                if (files == null) Status?.Invoke(problem ?? "Dosyalar okunamadı.");
            }
            var content = new ClipboardContent
            {
                Text = copied.Text, Html = copied.Html, Rtf = copied.Rtf, ImageLength = copied.Image?.Length ?? 0,
                Roots = files?.Roots ?? [], Files = files?.Files ?? [], Folders = files?.Folders ?? []
            };
            if (content.IsEmpty) return;
            var what = Describe(content);
            var big = content.TotalBytes > BigTransfer;
            if (big) Status?.Invoke($"{name} bilgisayarına {what} gönderiliyor…");
            var progress = Throttled(done => Status?.Invoke($"{name} bilgisayarına {what} gönderiliyor · %{Percent(done, content.TotalBytes)}"), big);
            var started = DateTime.UtcNow;
            var accepted = await Task.Run(() => host.Network.TransferAsync(peer,
                (stream, token) => ClipboardTransfer.SendAsync(stream, content, copied.Image, files?.Sources ?? [], progress, token), cancel.Token));
            Diag.Write($"Pano gönderildi: {name}, {what}, {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms{(accepted ? "" : ", reddedildi")}");
            if (!accepted) Status?.Invoke($"{name} panoyu kabul etmedi; orada pano veya dosya paylaşımı kapalı ya da yeterli yer yok.");
            else if (big) { Status?.Invoke($"{name} bilgisayarına {what} gönderildi."); Announce?.Invoke("Aktarım tamamlandı", $"{what} {name} bilgisayarına gönderildi."); }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { /* A newer copy replaced this one. */ }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException
                                    or OperationCanceledException or UnauthorizedAccessException or InvalidDataException)
        {
            Diag.Write($"Pano gönderilemedi: {name}: {e.GetType().Name}: {e.Message}");
            Status?.Invoke($"Pano {name} bilgisayarına gönderilemedi: {e.Message}");
        }
        finally
        {
            if (outgoing.TryGetValue(peer, out var current) && current == cancel) outgoing.Remove(peer);
            cancel.Dispose();
        }
    }

    /// <summary>Incoming transfer from a paired peer (thread pool).</summary>
    private async Task ReceiveAsync(string peer, Stream stream, CancellationToken token)
    {
        var name = PeerName(peer);
        ClipboardContent? incoming = null;
        var big = false;
        var progress = new Progress<long>();
        var lastReport = 0L;
        progress.ProgressChanged += (_, done) =>
        {
            if (!big || Environment.TickCount64 - Interlocked.Read(ref lastReport) < 300) return;
            Interlocked.Exchange(ref lastReport, Environment.TickCount64);
            dispatcher.BeginInvoke(() => Status?.Invoke($"{name} bilgisayarından {Describe(incoming!)} alınıyor · %{Percent(done, incoming!.TotalBytes)}"));
        };
        try
        {
            var received = await ClipboardTransfer.ReceiveAsync(stream, content =>
            {
                incoming = content;
                var options = host.Controller.Options;
                if (!options.ShareClipboard || content.Roots.Length > 0 && !options.ShareFiles) return null;
                big = content.TotalBytes > BigTransfer;
                if (content.Roots.Length == 0) return Path.GetTempPath();
                var folder = NewFolder(name);
                if (new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace < content.FileBytes + 512L * 1024 * 1024)
                {
                    dispatcher.BeginInvoke(() => Status?.Invoke($"{name} bilgisayarından gelen dosyalar için diskte yeterli yer yok."));
                    return null;
                }
                Directory.CreateDirectory(folder);
                if (big) dispatcher.BeginInvoke(() => Status?.Invoke($"{name} bilgisayarından {Describe(content)} alınıyor…"));
                return folder;
            }, progress, token);
            if (received == null) return;
            Diag.Write($"Pano alındı: {name}, {Describe(received.Content)}");
            await dispatcher.InvokeAsync(() => Write(peer, name, received));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
        {
            Diag.Write($"Pano alınamadı: {name}: {e.GetType().Name}: {e.Message}");
            if (big) _ = dispatcher.BeginInvoke(() => Status?.Invoke($"{name} bilgisayarından aktarım yarım kaldı: {e.Message}"));
        }
    }

    private void Write(string peer, string name, ReceivedClipboard received)
    {
        ownWriteFrom = peer; ownWriteTick = Environment.TickCount64;
        if (!ClipboardIO.Write(received)) { Status?.Invoke("Pano şu an başka bir program tarafından kullanılıyor; gelen içerik konamadı."); return; }
        var c = received.Content;
        if (c.Roots.Length == 0) return;
        Status?.Invoke($"{name} bilgisayarından {Describe(c)} geldi · yapıştırabilirsiniz (Ctrl+V).");
        if (c.TotalBytes > BigTransfer) Announce?.Invoke("Dosyalar hazır", $"{name} bilgisayarından {Describe(c)} geldi. Ctrl+V ile yapıştırabilirsiniz.");
    }

    private static string Describe(ClipboardContent c)
    {
        if (c.Roots.Length > 0)
        {
            var size = c.FileBytes switch
            {
                >= 1L << 30 => $"{c.FileBytes / (double)(1L << 30):F1} GB",
                >= 1L << 20 => $"{c.FileBytes / (double)(1L << 20):F1} MB",
                _ => $"{Math.Max(1, c.FileBytes / 1024)} KB"
            };
            return c.Roots.Length == 1 ? $"\"{c.Roots[0]}\" ({size})" : $"{c.Roots.Length} öğe ({size})";
        }
        return c.ImageLength > 0 ? "resim" : "metin";
    }

    private static int Percent(long done, long total) => total <= 0 ? 100 : (int)Math.Clamp(done * 100 / total, 0, 100);

    private static IProgress<long>? Throttled(Action<long> report, bool enabled)
    {
        if (!enabled) return null;
        var last = 0L;
        return new Progress<long>(done =>
        {
            if (Environment.TickCount64 - last < 300) return;
            last = Environment.TickCount64;
            report(done);
        });
    }

    private string PeerName(string peer) => host.Network.Directory.Get(peer)?.Name ?? "Diğer bilgisayar";

    private string NewFolder(string peerName)
    {
        var safe = new string([.. peerName.Select(ch => ClipboardContent.IsSafeName(ch.ToString()) ? ch : '_')]);
        var basePath = Path.Combine(ReceivedFolder, $"{DateTime.Now:yyyy-MM-dd HH.mm.ss} {safe}");
        var path = basePath;
        for (var n = 2; Directory.Exists(path); n++) path = $"{basePath} ({n})";
        return path;
    }

    /// <summary>Received copies older than three days are no longer on anyone's clipboard.</summary>
    private void CleanOldReceived()
    {
        try
        {
            if (!Directory.Exists(ReceivedFolder)) return;
            foreach (var dir in new DirectoryInfo(ReceivedFolder).EnumerateDirectories())
                if (dir.CreationTimeUtc < DateTime.UtcNow.AddDays(-3))
                    try { dir.Delete(true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(handle);
        source.RemoveHook(WndProc);
        host.Network.TransferHandler = null;
        foreach (var c in outgoing.Values) c.Cancel();
    }

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
