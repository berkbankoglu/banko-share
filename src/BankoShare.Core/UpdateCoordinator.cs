namespace BankoShare.Core;

public enum UpdatePhase { Idle, Checking, Downloading, Ready, Applying, Error, Unpackaged }

public interface IUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<string?> FindUpdateAsync(CancellationToken token);
    Task DownloadAsync(Action<int> progress, CancellationToken token);
    void ApplyAndRestart();
}

// Called on the UI dispatcher. The backend owns package verification and installation.
public sealed class UpdateCoordinator(IUpdateBackend backend)
{
    private bool checking;
    private DateTimeOffset? installAt;
    public bool Automatic { get; set; } = true;
    public UpdatePhase Phase { get; private set; } = UpdatePhase.Idle;
    public string CurrentVersion => backend.CurrentVersion;
    public string? AvailableVersion { get; private set; }
    public string Status { get; private set; } = "Güncelleme kontrolü bekleniyor.";
    public DateTimeOffset? LastChecked { get; private set; }
    public event Action? Changed;

    public async Task CheckAsync(CancellationToken token)
    {
        if (checking || Phase is UpdatePhase.Ready or UpdatePhase.Applying) return;
        if (!backend.IsInstalled)
        { Set(UpdatePhase.Unpackaged, "Otomatik güncelleme için GitHub'daki Setup.exe ile kurun."); return; }
        checking = true;
        try
        {
            Set(UpdatePhase.Checking, "GitHub'da yeni sürüm aranıyor…");
            AvailableVersion = await backend.FindUpdateAsync(token);
            token.ThrowIfCancellationRequested();
            LastChecked = DateTimeOffset.Now;
            if (AvailableVersion == null)
            { Set(UpdatePhase.Idle, $"Güncel · v{CurrentVersion}"); return; }
            Set(UpdatePhase.Downloading, $"v{AvailableVersion} indiriliyor…");
            await backend.DownloadAsync(p =>
            {
                if (Phase == UpdatePhase.Downloading && !token.IsCancellationRequested)
                    Set(UpdatePhase.Downloading, $"v{AvailableVersion} indiriliyor · %{p}");
            }, token);
            token.ThrowIfCancellationRequested();
            installAt = null;
            Set(UpdatePhase.Ready, $"v{AvailableVersion} indirildi ve doğrulandı.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { Set(UpdatePhase.Idle, "Güncelleme kontrolü durduruldu."); }
        catch (Exception)
        {
            // Do not display server response bodies or credentials in UI/logs.
            Set(UpdatePhase.Error, "Güncelleme alınamadı. İnternet bağlantısını kontrol edin; tekrar denenecek.");
        }
        finally { checking = false; }
    }

    public void Tick(DateTimeOffset now, bool sessionBusy)
    {
        if (Phase != UpdatePhase.Ready) return;
        if (sessionBusy)
        { installAt = null; Set(Phase, $"v{AvailableVersion} hazır. Oturum bitince kurulacak."); return; }
        if (!Automatic)
        { installAt = null; Set(Phase, $"v{AvailableVersion} hazır. Kurmak için yeniden başlatın."); return; }
        installAt ??= now.AddSeconds(15);
        if (now >= installAt) { Apply(sessionBusy); return; }
        Set(Phase, $"v{AvailableVersion} hazır · {Math.Ceiling((installAt.Value - now).TotalSeconds)} sn sonra yeniden başlatılacak.");
    }

    public void Apply(bool sessionBusy)
    {
        if (sessionBusy || Phase != UpdatePhase.Ready) return;
        Set(UpdatePhase.Applying, "Güncelleme kuruluyor; Banko Share yeniden açılacak…");
        try { backend.ApplyAndRestart(); }
        catch (Exception) { Set(UpdatePhase.Error, "Güncelleme kurulamadı. Uygulamayı kapatıp tekrar açın veya Setup.exe ile kurun."); }
    }

    private void Set(UpdatePhase phase, string status)
    { Phase = phase; Status = status; Changed?.Invoke(); }
}
