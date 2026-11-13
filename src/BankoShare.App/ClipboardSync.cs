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
/// The clipboard follows the pointer. What was copied here goes to another machine when the pointer moves there
/// (we entered it, it took the pointer back from us, or its own mouse took over here), so Ctrl+V works on that side.
/// While a session runs, text and images copied here go to the machine on the other end at once; files wait for
/// the pointer, as they may be large. Text (plain, HTML, RTF), images and files/folders are supported; bytes travel
/// over a separate TLS connection.
/// <para>
/// Copies are recognised by Windows' clipboard sequence number, so a missed change notification cannot hide one.
/// Nothing goes back to the machine it came from. Clipboard access happens on the UI (STA) thread; transfers run on
/// the thread pool.
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
    private readonly ClipboardLedger ledger = new();
    private readonly Dictionary<string, CancellationTokenSource> outgoing = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnedOld = new(StringComparer.Ordinal);

    /// <summary>Why a hand-off happens: the pointer moved, something was copied mid-session, or a copy landed right after the pointer left.</summary>
    private enum Trigger { Pointer, Copied, JustAfter, Forward }

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
        // Without change notifications the clipboard still goes along when the pointer moves; only mid-session sending stops.
        Diag.Write(AddClipboardFormatListener(handle)
            ? "Pano izleniyor"
            : $"Pano değişiklik bildirimi kurulamadı (hata {Marshal.GetLastWin32Error()}); pano yalnızca geçişlerde gönderilecek");
        host.Controller.PointerHandedOff += peer => dispatcher.BeginInvoke(() => HandOff(peer, Trigger.Pointer));
        host.Network.TransferHandler = ReceiveAsync;
        _ = Task.Run(CleanOldReceived);
    }

    private static uint Sequence() => GetClipboardSequenceNumber();

    private nint WndProc(nint hwnd, int message, nint w, nint l, ref bool handled)
    {
        if (message == WmClipboardUpdate) OnClipboardChanged();
        return 0;
    }

    private void OnClipboardChanged()
    {
        var sequence = Sequence();
        if (ledger.WrittenFrom(sequence) is { } from)
        {
            // Our own write of a peer's content. The pointer may have moved on to a third machine meanwhile: pass it along.
            if (host.Controller.ControllingPeer is { } current && current != from) HandOff(current, Trigger.Forward);
            return;
        }
        // Copied here. The machine at the other end of the current session gets it at once.
        if ((host.Controller.ControllingPeer ?? host.Controller.ControlledByPeer) is { } session) HandOff(session, Trigger.Copied);
        // Ctrl+C and a quick move: the copy landed just after the pointer left, so it goes after it.
        else if (ledger.RecentHandOff(Environment.TickCount64) is { } recent) HandOff(recent, Trigger.JustAfter);
    }

    private void HandOff(string peer, Trigger trigger, bool retry = true)
    {
        try { TryHandOff(peer, trigger, retry); }
        catch (Exception e) { Diag.Write($"Pano okunamadı: {e.GetType().Name}: {e.Message}"); }
    }

    private void TryHandOff(string peer, Trigger trigger, bool retry)
    {
        if (trigger == Trigger.Pointer) ledger.HandedOff(peer, Environment.TickCount64);
        var options = host.Controller.Options;
        if (!options.ShareClipboard) return;
        var sequence = Sequence();
        if (ledger.Has(peer, sequence)) return; // nothing new for this machine
        var name = PeerName(peer);
        if (!Versions.AtLeast(host.Controller.PeerVersion(peer), 0, 5))
        {
            if (!warnedOld.Add(peer)) return;
            Diag.Write($"Pano gönderilmedi: {name} sürümü {host.Controller.PeerVersion(peer) ?? "bilinmiyor"} (0.5 veya üstü gerekir)");
            Status?.Invoke($"{name} eski bir sürüm kullanıyor; pano paylaşımı için orada da güncelleme gerekiyor.");
            return;
        }
        var copied = ClipboardIO.Read(options.ShareFiles, out var problem);
        if (copied == null)
        {
            if (problem == ClipboardIO.Busy && retry)
            {
                Diag.Write($"Pano okunamadı ({problem}); yeniden denenecek");
                _ = Task.Delay(300).ContinueWith(_ => dispatcher.BeginInvoke(() => HandOff(peer, trigger, retry: false)), TaskScheduler.Default);
                return;
            }
            Diag.Write($"Pano gönderilmedi: {name}: {problem}");
            ledger.Sending(peer, sequence); // nothing usable on this clipboard; do not try it again
            return;
        }
        // Files can be large: mid-session they wait until the pointer actually goes there.
        if (copied.Value.Paths != null && trigger == Trigger.Copied) return;
        ledger.Sending(peer, sequence);
        if (outgoing.Remove(peer, out var previous)) previous.Cancel();
        var cancel = new CancellationTokenSource();
        outgoing[peer] = cancel;
        _ = SendAsync(peer, copied.Value, sequence, trigger, cancel);
    }

    private async Task SendAsync(string peer, CopiedClipboard copied, uint sequence, Trigger trigger, CancellationTokenSource cancel)
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
            var why = trigger switch { Trigger.Copied => "kopyalanınca", Trigger.JustAfter => "geçişten hemen sonra kopyalandı", Trigger.Forward => "aktarıldı", _ => "geçişte" };
            Diag.Write($"Pano gönderildi: {name}, {what} ({why}), {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms{(accepted ? "" : ", reddedildi")}");
            if (!accepted) Status?.Invoke($"{name} panoyu kabul etmedi; orada pano veya dosya paylaşımı kapalı ya da yeterli yer yok.");
            else if (big) { Status?.Invoke($"{name} bilgisayarına {what} gönderildi."); Announce?.Invoke("Aktarım tamamlandı", $"{what} {name} bilgisayarına gönderildi."); }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { /* A newer copy replaced this one. */ }
        catch (Exception e)
        {
            // Every failure is logged, and the same copy is tried again the next time the pointer goes there.
            ledger.Failed(peer, sequence);
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
        catch (Exception e)
        {
            Diag.Write($"Pano alınamadı: {name}: {e.GetType().Name}: {e.Message}");
            if (big) _ = dispatcher.BeginInvoke(() => Status?.Invoke($"{name} bilgisayarından aktarım yarım kaldı: {e.Message}"));
        }
    }

    private void Write(string peer, string name, ReceivedClipboard received)
    {
        if (!ClipboardIO.Write(received))
        {
            Diag.Write($"Pano yazılamadı: {name} bilgisayarından geleni koyarken pano başka bir programda açıktı");
            Status?.Invoke("Pano şu an başka bir program tarafından kullanılıyor; gelen içerik konamadı.");
            return;
        }
        // The sender already has this; it only goes on to other machines.
        ledger.Wrote(peer, Sequence());
        var c = received.Content;
        Status?.Invoke($"{name} bilgisayarından {Describe(c)} geldi · Ctrl+V ile yapıştırabilirsiniz.");
        if (c.Roots.Length > 0 && c.TotalBytes > BigTransfer) Announce?.Invoke("Dosyalar hazır", $"{name} bilgisayarından {Describe(c)} geldi. Ctrl+V ile yapıştırabilirsiniz.");
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

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
