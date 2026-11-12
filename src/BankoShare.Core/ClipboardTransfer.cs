using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankoShare.Core;

public sealed record ClipboardFile(string Path, long Length);

/// <summary>
/// What one machine hands to another when the pointer moves there: text formats, an image and/or files.
/// File bytes follow the header in the order of <see cref="Files"/>; paths are '/'-separated and relative.
/// </summary>
public sealed record ClipboardContent
{
    public const int MaxTextChars = 16 * 1024 * 1024;
    public const long MaxImageBytes = 64L * 1024 * 1024;
    public const int MaxEntries = 50_000;
    public const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;

    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    /// <summary>PNG bytes that follow the header.</summary>
    public long ImageLength { get; init; }
    /// <summary>Top-level names, as the source's file list showed them: these go on the receiver's clipboard.</summary>
    public string[] Roots { get; init; } = [];
    public ClipboardFile[] Files { get; init; } = [];
    /// <summary>Folders to create even when empty.</summary>
    public string[] Folders { get; init; } = [];

    [JsonIgnore] public long FileBytes => Files.Sum(f => f.Length);
    [JsonIgnore] public long TotalBytes => ImageLength + FileBytes;
    [JsonIgnore] public bool IsEmpty => Text == null && Html == null && Rtf == null && ImageLength == 0 && Roots.Length == 0;

    /// <summary>Comes from the network: bound everything and refuse any path that could leave the target folder.</summary>
    public void Validate()
    {
        if ((Text?.Length ?? 0) > MaxTextChars || (Html?.Length ?? 0) > MaxTextChars || (Rtf?.Length ?? 0) > MaxTextChars)
            throw new InvalidDataException("Clipboard text too large.");
        if (ImageLength is < 0 or > MaxImageBytes) throw new InvalidDataException("Invalid image size.");
        if (Roots is null || Files is null || Folders is null || Roots.Length + Files.Length + Folders.Length > MaxEntries)
            throw new InvalidDataException("Too many entries.");
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Roots)
            if (!IsSafeName(r) || !roots.Add(r)) throw new InvalidDataException("Invalid root name.");
        long total = 0;
        foreach (var f in Files)
        {
            if (f is null || f.Length < 0 || !IsSafeRelative(f.Path, roots)) throw new InvalidDataException("Invalid file entry.");
            total += f.Length;
            if (total > MaxTotalBytes) throw new InvalidDataException("Transfer too large.");
        }
        foreach (var d in Folders)
            if (!IsSafeRelative(d, roots)) throw new InvalidDataException("Invalid folder entry.");
    }

    private static bool IsSafeRelative(string? path, HashSet<string> roots)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024) return false;
        var parts = path.Split('/');
        return roots.Contains(parts[0]) && parts.All(IsSafeName);
    }

    /// <summary>One path segment that Windows accepts and that cannot climb or name a device or drive.</summary>
    public static bool IsSafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && name is not ("." or "..") && !name.EndsWith('.') && !name.EndsWith(' ')
        && name.IndexOfAny(InvalidChars) < 0 && !ReservedNames.Contains(System.IO.Path.GetFileNameWithoutExtension(name));

    private static readonly char[] InvalidChars = [.. System.IO.Path.GetInvalidFileNameChars(), '/', '\\', ':'];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
}

/// <summary>What arrived. <see cref="RootPaths"/> are the files and folders to put on this machine's clipboard.</summary>
public sealed record ReceivedClipboard(ClipboardContent Content, byte[]? Image, string? Folder)
{
    public string[] RootPaths => Folder == null ? [] : [.. Content.Roots.Select(r => Path.Combine(Folder, r))];
}

/// <summary>Collects copied files and folders into a transfer list, walking folders without following links.</summary>
public static class ClipboardFiles
{
    public sealed record Collected(string[] Roots, ClipboardFile[] Files, string[] Folders, string[] Sources);

    /// <summary>Returns null (with a reason) when the selection is too large or unreadable.</summary>
    public static Collected? Collect(IEnumerable<string> paths, out string? problem, int maxEntries = ClipboardContent.MaxEntries,
        long maxBytes = ClipboardContent.MaxTotalBytes)
    {
        problem = null;
        List<string> roots = [], folders = [], sources = [];
        List<ClipboardFile> files = [];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        try
        {
            foreach (var path in paths)
            {
                var full = Path.GetFullPath(path.TrimEnd('\\', '/'));
                var name = Path.GetFileName(full);
                if (!ClipboardContent.IsSafeName(name)) { problem = "Bu öğe aktarılamıyor: " + full; return null; }
                // Two copied items with the same name (from different folders) must not overwrite each other.
                var root = name;
                for (var n = 2; !used.Add(root); n++)
                    root = Path.GetFileNameWithoutExtension(name) + $" ({n})" + Path.GetExtension(name);
                roots.Add(root);
                if (File.Exists(full)) { Add(root, new FileInfo(full)); continue; }
                if (!Directory.Exists(full)) { problem = "Kopyalanan öğe bulunamadı: " + full; return null; }
                folders.Add(root);
                Walk(new DirectoryInfo(full), root);
            }
        }
        catch (LimitException e) { problem = e.Message; return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problem = "Dosyalar okunamadı: " + e.Message; return null; }
        return new([.. roots], [.. files], [.. folders], [.. sources]);

        void Walk(DirectoryInfo dir, string relative)
        {
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                var rel = relative + "/" + entry.Name;
                if (entry is DirectoryInfo sub)
                {
                    // Junctions and symbolic links could loop or reach outside the selection.
                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    folders.Add(rel); Count();
                    Walk(sub, rel);
                }
                else Add(rel, (FileInfo)entry);
            }
        }

        void Add(string rel, FileInfo info)
        {
            files.Add(new ClipboardFile(rel, info.Length)); sources.Add(info.FullName); Count();
            total += info.Length;
            if (total > maxBytes) throw new LimitException($"Seçim çok büyük; en fazla {maxBytes / (1024 * 1024 * 1024)} GB aktarılabilir.");
        }

        void Count()
        {
            if (files.Count + folders.Count > maxEntries) throw new LimitException($"Çok fazla dosya var; en fazla {maxEntries:N0} öğe aktarılabilir.");
        }
    }

    private sealed class LimitException(string message) : Exception(message);
}

/// <summary>
/// Streams one clipboard hand-off over its own TLS connection, so a large file never delays mouse traffic.
/// Sender: header (length-prefixed JSON) → wait for accept → image → file bytes → wait for done.
/// The receiver checks everything before accepting and gives up when the sender goes quiet for 30 seconds.
/// </summary>
public static class ClipboardTransfer
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private const int HeaderLimit = 80 * 1024 * 1024;
    private const int Chunk = 128 * 1024;
    private const byte Accepted = 1, Refused = 0, Done = 1;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 8 };

    /// <summary>Returns false if the receiver refused (sharing off, too large, no space).</summary>
    public static async Task<bool> SendAsync(Stream stream, ClipboardContent content, byte[]? image, IReadOnlyList<string> sources,
        IProgress<long>? progress, CancellationToken token)
    {
        if (sources.Count != content.Files.Length || (image?.Length ?? 0) != content.ImageLength) throw new ArgumentException("Content and data differ.");
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(IdleTimeout);
        var header = JsonSerializer.SerializeToUtf8Bytes(content, Json);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, header.Length);
        await stream.WriteAsync(length, idle.Token);
        await stream.WriteAsync(header, idle.Token);
        await stream.FlushAsync(idle.Token);
        if (await ReadByte(stream, idle.Token) != Accepted) return false;
        long done = 0;
        if (image != null) { await stream.WriteAsync(image, idle.Token); done += image.Length; progress?.Report(done); }
        var buffer = new byte[Chunk];
        for (var i = 0; i < sources.Count; i++)
        {
            await using var file = new FileStream(sources[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Chunk, useAsync: true);
            var left = content.Files[i].Length;
            while (left > 0)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token);
                if (read == 0) throw new IOException("Dosya aktarım sırasında küçüldü: " + sources[i]);
                await stream.WriteAsync(buffer.AsMemory(0, read), idle.Token);
                left -= read; done += read;
                idle.CancelAfter(IdleTimeout);
                progress?.Report(done);
            }
        }
        await stream.FlushAsync(idle.Token);
        return await ReadByte(stream, idle.Token) == Done;
    }

    /// <summary>
    /// <paramref name="accept"/> sees the validated header and returns a new, empty folder for files (or this
    /// machine's temp path when there are none), or null to refuse. A failed transfer deletes its folder.
    /// </summary>
    public static async Task<ReceivedClipboard?> ReceiveAsync(Stream stream, Func<ClipboardContent, string?> accept, IProgress<long>? progress,
        CancellationToken token)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(IdleTimeout);
        var length = new byte[4];
        await stream.ReadExactlyAsync(length, idle.Token);
        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is <= 0 or > HeaderLimit) throw new InvalidDataException("Invalid clipboard header.");
        var header = new byte[size];
        await stream.ReadExactlyAsync(header, idle.Token);
        ClipboardContent content;
        try { content = JsonSerializer.Deserialize<ClipboardContent>(header, Json) ?? throw new InvalidDataException("Empty clipboard header."); }
        catch (JsonException e) { throw new InvalidDataException("Malformed clipboard header.", e); }
        content.Validate();
        var folder = accept(content);
        if (folder == null) { await stream.WriteAsync(new[] { Refused }, idle.Token); return null; }
        await stream.WriteAsync(new[] { Accepted }, idle.Token);
        await stream.FlushAsync(idle.Token);
        var root = Path.GetFullPath(folder);
        try
        {
            byte[]? image = null;
            long done = 0;
            if (content.ImageLength > 0)
            {
                image = new byte[content.ImageLength];
                await stream.ReadExactlyAsync(image, idle.Token);
                done += image.Length; progress?.Report(done);
            }
            foreach (var d in content.Folders) Directory.CreateDirectory(Inside(root, d));
            var buffer = new byte[Chunk];
            foreach (var f in content.Files)
            {
                var target = Inside(root, f.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, Chunk, useAsync: true);
                var left = f.Length;
                while (left > 0)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token);
                    if (read == 0) throw new EndOfStreamException("Transfer ended early.");
                    await file.WriteAsync(buffer.AsMemory(0, read), idle.Token);
                    left -= read; done += read;
                    idle.CancelAfter(IdleTimeout);
                    progress?.Report(done);
                }
            }
            await stream.WriteAsync(new[] { Done }, idle.Token);
            await stream.FlushAsync(idle.Token);
            return new ReceivedClipboard(content, image, content.Roots.Length > 0 ? root : null);
        }
        catch
        {
            if (content.Roots.Length > 0) try { Directory.Delete(root, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Resolves a validated relative path and proves it stays inside <paramref name="root"/>.</summary>
    private static string Inside(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path leaves the transfer folder.");
        return full;
    }

    private static async Task<int> ReadByte(Stream stream, CancellationToken token)
    {
        var one = new byte[1];
        await stream.ReadExactlyAsync(one, token);
        return one[0];
    }
}
