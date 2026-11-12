using System.Collections.Concurrent;

namespace BankoShare.Core;

/// <summary>
/// Runs operating-system input calls (SendInput, SetCursorPos, cursor hiding) in order on one dedicated thread.
/// <para>
/// SendInput does not return until every low-level hook in the session has seen the event. If the caller holds
/// a lock that our own hook thread is waiting for (a physical mouse move on the controlled machine), both wait
/// on each other until Windows times the hook out, about a second, and repeated timeouts make Windows remove
/// the hook. Posting here means no lock is ever held across those calls.
/// </para>
/// </summary>
public sealed class OsWorker : IDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread thread;
    private readonly Action<Exception>? failed;
    private int pending;

    public OsWorker(string name, Action<Exception>? failed = null)
    {
        this.failed = failed;
        thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>Actions queued or running. A new crossing waits until earlier pointer changes have landed.</summary>
    public int Pending => Volatile.Read(ref pending);

    public void Post(Action action)
    {
        Interlocked.Increment(ref pending);
        try { queue.Add(action); }
        catch (InvalidOperationException) { Interlocked.Decrement(ref pending); } // shutting down
    }

    /// <summary>Waits until everything posted so far has run. Never call it from a thread a posted action may wait on.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Pending > 0) { if (DateTime.UtcNow > deadline) return false; Thread.Sleep(1); }
        return true;
    }

    private void Run()
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception e) { failed?.Invoke(e); }
            finally { Interlocked.Decrement(ref pending); }
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
        thread.Join(TimeSpan.FromSeconds(2));
    }
}
