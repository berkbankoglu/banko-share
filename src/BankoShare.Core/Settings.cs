using System.Text.Json;

namespace BankoShare.Core;

public sealed record PeerRecord(string Id, string Name, string? Address, MonitorInfo[]? Monitors)
{
    public DeviceInfo? LastKnownDevice
    {
        get
        {
            if (Monitors == null) return null;
            var d = new DeviceInfo(Id, Name, Monitors);
            try { d.Validate(); return d; } catch (InvalidDataException) { return null; }
        }
    }
}

/// <summary>JSON files in one directory, written atomically. Missing or corrupt files fall back to defaults.</summary>
public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly int[] RetryDelaysMs = [10, 20, 40, 80, 160];
    public string DirectoryPath { get; } = directory;

    public T Load<T>(string name, Func<T> fallback)
    {
        try
        {
            // Delete sharing lets a concurrent save replace the file while we read the old one.
            using var stream = new FileStream(Path.Combine(DirectoryPath, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? fallback();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return fallback(); }
    }

    /// <summary>
    /// Writes a temporary file and swaps it in. Antivirus scanners, indexers and readers briefly hold files open,
    /// which makes the swap fail with a sharing violation or access denied, so it is retried for about 300 ms.
    /// Blocks: never call it on the input thread or under a lock the input thread takes.
    /// </summary>
    public void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, name);
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, Options);
        for (var attempt = 0; ; attempt++)
        {
            try { File.WriteAllText(temp, json); File.Move(temp, path, true); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < RetryDelaysMs.Length) { Thread.Sleep(RetryDelaysMs[attempt]); }
        }
    }
}

/// <summary>
/// Writes settings off the caller's thread. Only the newest value per file is written, by a single worker,
/// so callers may schedule from under any lock. Values must not change after scheduling.
/// </summary>
public sealed class BackgroundSaver(SettingsStore? store, Action<string>? failed = null)
{
    private readonly object gate = new();
    private readonly Dictionary<string, object> pending = new(StringComparer.Ordinal);
    private Task worker = Task.CompletedTask;
    private bool running;

    public void Schedule(string name, object value)
    {
        if (store == null) return;
        lock (gate)
        {
            pending[name] = value;
            if (running) return;
            running = true;
            worker = Task.Run(Drain);
        }
    }

    /// <summary>Waits for scheduled writes, e.g. before the process exits.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        Task current;
        lock (gate) current = worker;
        return current.Wait(timeout);
    }

    private void Drain()
    {
        while (true)
        {
            string name; object value;
            lock (gate)
            {
                if (pending.Count == 0) { running = false; return; }
                (name, value) = pending.First();
                pending.Remove(name);
            }
            try { store!.Save(name, value); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed?.Invoke(name); }
        }
    }
}

/// <summary>Paired devices: pinned certificate fingerprint, display name, last address and monitors.</summary>
public sealed class PeerDirectory
{
    public const string FileName = "peers.json";
    private readonly object gate = new();
    private readonly Dictionary<string, PeerRecord> peers = new(StringComparer.Ordinal);
    private readonly BackgroundSaver saver;
    public event Action? Changed;

    public PeerDirectory(SettingsStore? store)
    {
        // Kept in memory if a write fails; the next change writes the whole list again.
        saver = new BackgroundSaver(store);
        foreach (var p in store?.Load<List<PeerRecord>>(FileName, () => []) ?? [])
            if (p != null && DeviceIds.IsValid(p.Id)) peers[p.Id] = p with { Name = CleanName(p.Name), Monitors = p.LastKnownDevice?.Monitors };
    }

    public bool IsPaired(string id) { lock (gate) return peers.ContainsKey(id); }
    public PeerRecord? Get(string id) { lock (gate) return peers.GetValueOrDefault(id); }
    public IReadOnlyList<PeerRecord> All { get { lock (gate) return [.. peers.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)]; } }

    /// <summary>Adds a peer or updates the fields that are not null.</summary>
    public void AddOrUpdate(string id, string? name = null, string? address = null, MonitorInfo[]? monitors = null)
    {
        if (!DeviceIds.IsValid(id)) throw new ArgumentException("Invalid device id.", nameof(id));
        lock (gate)
        {
            var old = peers.GetValueOrDefault(id);
            var next = new PeerRecord(id, name != null ? CleanName(name) : old?.Name ?? "Yeni cihaz", address ?? old?.Address, monitors ?? old?.Monitors);
            if (old != null && old.Name == next.Name && old.Address == next.Address && ReferenceEquals(old.Monitors, next.Monitors)) return;
            peers[id] = next;
            Persist();
        }
        Changed?.Invoke();
    }

    public bool Remove(string id)
    {
        lock (gate) { if (!peers.Remove(id)) return false; Persist(); }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Waits for pending writes, e.g. before the process exits.</summary>
    public bool Flush(TimeSpan timeout) => saver.WaitIdle(timeout);

    public static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
        return clean.Length == 0 ? "Adsız cihaz" : clean;
    }

    private void Persist() => saver.Schedule(FileName, peers.Values.ToList());
}
