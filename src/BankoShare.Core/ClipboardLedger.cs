namespace BankoShare.Core;

/// <summary>
/// Which machine already has which clipboard, keyed by Windows' clipboard sequence number. The number changes with
/// every copy whether or not a change notification arrived, so a missed notification can never hide a copy.
/// UI thread only.
/// </summary>
public sealed class ClipboardLedger(long recentMs = 3000)
{
    private readonly Dictionary<string, uint> has = new(StringComparer.Ordinal);
    private (string Peer, uint Sequence)? ownWrite;
    private (string Peer, long At)? lastHandOff;

    /// <summary>The peer already has the clipboard at <paramref name="sequence"/>: we sent it there, or it came from there.</summary>
    public bool Has(string peer, uint sequence) => has.TryGetValue(peer, out var s) && s == sequence;

    /// <summary>The clipboard at <paramref name="sequence"/> is on its way to the peer (or has nothing worth sending).</summary>
    public void Sending(string peer, uint sequence) => has[peer] = sequence;

    /// <summary>A send failed: try again at the next chance, unless something newer went meanwhile.</summary>
    public void Failed(string peer, uint sequence)
    {
        if (Has(peer, sequence)) has.Remove(peer);
    }

    /// <summary>Content from <paramref name="peer"/> was put on the clipboard; <paramref name="sequence"/> is the number right after.</summary>
    public void Wrote(string peer, uint sequence)
    {
        has[peer] = sequence;
        ownWrite = (peer, sequence);
    }

    /// <summary>The peer whose content is on the clipboard at <paramref name="sequence"/>, if we put it there.</summary>
    public string? WrittenFrom(uint sequence) => ownWrite is { } w && w.Sequence == sequence ? w.Peer : null;

    public void HandedOff(string peer, long nowMs) => lastHandOff = (peer, nowMs);

    /// <summary>The pointer left for this peer a moment ago, so a copy landing now was meant for it (Ctrl+C, then a quick move).</summary>
    public string? RecentHandOff(long nowMs) => lastHandOff is { } h && nowMs - h.At <= recentMs ? h.Peer : null;
}
