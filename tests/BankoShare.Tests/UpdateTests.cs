using BankoShare.Core;

internal static class UpdateTests
{
    public static async Task<int> Run()
    {
        var count = 0;
        async Task Test(string name, Func<Task> action)
        { await action(); count++; Console.WriteLine($"PASS {name}"); }
        await Test("Update waits for idle and resets grace period when a session starts", async () =>
        {
            var backend = new Fake(); var updater = new UpdateCoordinator(backend);
            await updater.CheckAsync(default);
            var now = DateTimeOffset.UtcNow;
            updater.Tick(now, false);
            updater.Tick(now.AddSeconds(20), true);
            Check(backend.Applied == 0, "Applied during a session");
            updater.Tick(now.AddSeconds(21), false);
            updater.Tick(now.AddSeconds(35), false);
            Check(backend.Applied == 0, "Skipped reset grace period");
            updater.Tick(now.AddSeconds(36), false);
            Check(backend.Applied == 1, "Did not apply after idle grace period");
        });
        await Test("Automatic opt-out blocks restart; manual apply still requires idle", async () =>
        {
            var backend = new Fake(); var updater = new UpdateCoordinator(backend) { Automatic = false };
            await updater.CheckAsync(default);
            updater.Tick(DateTimeOffset.UtcNow, false);
            updater.Tick(DateTimeOffset.UtcNow.AddDays(1), false);
            updater.Apply(true);
            Check(backend.Applied == 0, "Ignored opt-out or session guard");
            updater.Apply(false); updater.Apply(false);
            Check(backend.Applied == 1, "Apply must happen once");
        });
        await Test("Failed package download is never installed and can be retried", async () =>
        {
            var backend = new Fake { FailDownload = true }; var updater = new UpdateCoordinator(backend);
            await updater.CheckAsync(default);
            updater.Tick(DateTimeOffset.UtcNow.AddDays(1), false); updater.Apply(false);
            Check(updater.Phase == UpdatePhase.Error && backend.Applied == 0, "Failed package became installable");
            backend.FailDownload = false; await updater.CheckAsync(default);
            Check(updater.Phase == UpdatePhase.Ready, "Retry failed");
        });
        await Test("Overlapping checks do not start duplicate downloads", async () =>
        {
            var backend = new Fake { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var updater = new UpdateCoordinator(backend);
            var first = updater.CheckAsync(default);
            await updater.CheckAsync(default);
            backend.Gate.SetResult(); await first;
            Check(backend.Checked == 1 && backend.Downloaded == 1, "Duplicate update work");
        });
        await Test("No update and unpackaged builds never become installable", async () =>
        {
            var backend = new Fake { OfferedVersion = null }; var updater = new UpdateCoordinator(backend);
            await updater.CheckAsync(default);
            Check(updater.Phase == UpdatePhase.Idle && backend.Downloaded == 0, "Downloaded absent update");
            backend.IsInstalled = false; await updater.CheckAsync(default); updater.Apply(false);
            Check(updater.Phase == UpdatePhase.Unpackaged && backend.Applied == 0, "Updated unpackaged app");
        });
        await Test("Cancelled check never downloads or applies", async () =>
        {
            var backend = new Fake { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var updater = new UpdateCoordinator(backend);
            using var cancel = new CancellationTokenSource();
            var task = updater.CheckAsync(cancel.Token); cancel.Cancel(); backend.Gate.SetResult(); await task;
            updater.Apply(false);
            Check(backend.Downloaded == 0 && backend.Applied == 0, "Applied cancelled update");
        });
        await Test("Late progress cannot turn a verified package back into a download", async () =>
        {
            var backend = new Fake(); var updater = new UpdateCoordinator(backend);
            await updater.CheckAsync(default); backend.Progress!(100);
            Check(updater.Phase == UpdatePhase.Ready, "Late worker progress changed state");
        });
        return count;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Fake : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string CurrentVersion => "0.2.0";
        public string? OfferedVersion { get; set; } = "0.2.1";
        public bool FailDownload { get; set; }
        public int Checked, Downloaded, Applied;
        public TaskCompletionSource? Gate;
        public Action<int>? Progress;
        public async Task<string?> FindUpdateAsync(CancellationToken token)
        { Checked++; if (Gate != null) await Gate.Task; return OfferedVersion; }
        public Task DownloadAsync(Action<int> progress, CancellationToken token)
        {
            Downloaded++; Progress = progress;
            if (FailDownload) throw new IOException("Checksum mismatch");
            progress(100); return Task.CompletedTask;
        }
        public void ApplyAndRestart() => Applied++;
    }
}
