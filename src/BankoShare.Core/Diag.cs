using System.Text;

namespace BankoShare.Core;

/// <summary>
/// Small operational log: connections, pointer hand-offs, pauses, slow input and transfers. Never key or text
/// contents. <see cref="Write"/> only queues a line, so it is safe on the input thread; a background task appends
/// to banko-share.log (rotated at 2 MB). Peers can ask for the tail to diagnose a machine we cannot reach.
/// </summary>
public static class Diag
{
    private const int KeepLines = 2000;
    private const long RotateBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static readonly List<string> Pending = [];
    private static string? file;
    private static bool writing;

    public static string? FilePath { get { lock (Gate) return file; } }

    public static void Start(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "banko-share.log");
            if (File.Exists(path) && new FileInfo(path).Length > RotateBytes) File.Move(path, path + ".old", true);
            lock (Gate) file = path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* In-memory tail still works. */ }
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > KeepLines) Recent.Dequeue();
            if (file == null) return;
            Pending.Add(line);
            if (writing) return;
            writing = true;
        }
        _ = Task.Run(Flush);
    }

    /// <summary>The most recent lines, at most <paramref name="maxBytes"/> of UTF-8.</summary>
    public static string Tail(int maxBytes)
    {
        string[] lines;
        lock (Gate) lines = [.. Recent];
        var result = new List<string>();
        var size = 0;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            size += Encoding.UTF8.GetByteCount(lines[i]) + 1;
            if (size > maxBytes) break;
            result.Add(lines[i]);
        }
        result.Reverse();
        return string.Join('\n', result);
    }

    private static void Flush()
    {
        while (true)
        {
            string[] batch; string? path;
            lock (Gate)
            {
                if (Pending.Count == 0) { writing = false; return; }
                batch = [.. Pending]; Pending.Clear(); path = file;
            }
            try { if (path != null) File.AppendAllLines(path, batch); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Dropped; the in-memory tail keeps them. */ }
        }
    }
}
