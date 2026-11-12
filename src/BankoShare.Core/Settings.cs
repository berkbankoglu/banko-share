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

public sealed record SharePreferences(bool SwitchingEnabled = true);

/// <summary>JSON files in one directory, written atomically. Missing or corrupt files fall back to defaults.</summary>
public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string DirectoryPath { get; } = directory;

    public T Load<T>(string name, Func<T> fallback)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DirectoryPath, name)), Options) ?? fallback(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return fallback(); }
    }

    public void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}

/// <summary>Paired devices: pinned certificate fingerprint, display name, last address and monitors.</summary>
public sealed class PeerDirectory
{
    private const string FileName = "peers.json";
    private readonly object gate = new();
    private readonly Dictionary<string, PeerRecord> peers = new(StringComparer.Ordinal);
    private readonly SettingsStore? store;
    public event Action? Changed;

    public PeerDirectory(SettingsStore? store)
    {
        this.store = store;
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

    public static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
        return clean.Length == 0 ? "Adsız cihaz" : clean;
    }

    private void Persist()
    {
        try { store?.Save(FileName, peers.Values.ToList()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Kept in memory; retried on the next change. */ }
    }
}
