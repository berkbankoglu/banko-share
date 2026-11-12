using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

namespace BankoShare.Core;

/// <summary>
/// One authenticated, mutually pinned TLS connection to a paired device.
/// One reader, one writer and a heartbeat; every wait is bounded. Any failure closes the link.
/// </summary>
public sealed class PeerLink
{
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(8);
    private static readonly SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    private readonly TcpClient client;
    private readonly SslStream stream;
    private readonly Channel<Frame> outbound = Channel.CreateBounded<Frame>(new BoundedChannelOptions(4096)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource life = new();
    private Action<PeerLink, Frame>? onFrame;
    private Action<PeerLink>? onClosed;
    private int closed;

    public string PeerId { get; }
    public bool Initiator { get; }
    public IPAddress Address { get; }
    public bool IsClosed => Volatile.Read(ref closed) != 0;

    internal PeerLink(TcpClient client, SslStream stream, string peerId, bool initiator)
    {
        this.client = client; this.stream = stream; PeerId = peerId; Initiator = initiator;
        client.NoDelay = true;
        Address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4() ?? IPAddress.None;
    }

    /// <summary>
    /// Runs <paramref name="started"/> and then delivers <paramref name="initial"/> (a frame read during the
    /// handshake) before any later frame, so handlers see traffic in order.
    /// </summary>
    public void Start(Action<PeerLink, Frame> frameHandler, Action<PeerLink> closedHandler, Frame? initial = null, Action<PeerLink>? started = null)
    {
        onFrame = frameHandler; onClosed = closedHandler;
        try
        {
            started?.Invoke(this);
            if (initial is { } first) frameHandler(this, first);
        }
        catch { Close(); return; }
        _ = Run();
    }

    /// <summary>
    /// Never blocks. A full queue means the peer stopped reading, so the link is closed — on another
    /// thread, because callers (the input hook) may hold locks that close handlers also take.
    /// </summary>
    public bool TrySend(Frame frame)
    {
        if (IsClosed) return false;
        if (outbound.Writer.TryWrite(frame)) return true;
        _ = Task.Run(Close);
        return false;
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        life.Cancel();
        outbound.Writer.TryComplete();
        stream.Dispose(); client.Dispose();
        onClosed?.Invoke(this);
    }

    private async Task Run()
    {
        var token = life.Token;
        Task[] loops = [ReadLoop(token), WriteLoop(token), HeartbeatLoop(token)];
        try { await Task.WhenAny(loops); } catch { }
        Close();
        try { await Task.WhenAll(loops); } catch { }
    }

    private async Task ReadLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(ReadTimeout);
            var frame = await Frame.ReadAsync(stream, deadline.Token);
            if (frame.Type != FrameType.Heartbeat) onFrame?.Invoke(this, frame);
        }
    }

    private async Task WriteLoop(CancellationToken token)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        List<Frame> batch = [];
        while (await outbound.Reader.WaitToReadAsync(token))
        {
            while (batch.Count < 512 && outbound.Reader.TryRead(out var f)) batch.Add(f);
            for (var i = 0; i < batch.Count; i++)
                if (!(batch[i].IsMove && i + 1 < batch.Count && batch[i + 1].IsMove)) batch[i].WriteTo(buffer);
            batch.Clear();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(WriteTimeout);
            await stream.WriteAsync(buffer.WrittenMemory, deadline.Token);
            await stream.FlushAsync(deadline.Token);
            buffer.ResetWrittenCount();
        }
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(token)) TrySend(Frame.Empty(FrameType.Heartbeat));
    }

    /// <summary>Client side of the TLS handshake. The server certificate must satisfy <paramref name="acceptServer"/>.</summary>
    public static async Task<(TcpClient Client, SslStream Stream, string PeerId)> ConnectAsync(
        IPEndPoint endpoint, DeviceIdentity self, Func<string, bool> acceptServer, CancellationToken token)
    {
        var client = new TcpClient(AddressFamily.InterNetwork);
        SslStream? ssl = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(HandshakeTimeout);
        try
        {
            await client.ConnectAsync(endpoint, deadline.Token);
            string? peerId = null;
            ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "BankoShare",
                EnabledSslProtocols = Protocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificates = [self.Certificate],
                LocalCertificateSelectionCallback = (_, _, _, _, _) => self.Certificate,
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert == null) return false;
                    var id = DeviceIdentity.Fingerprint(cert);
                    if (id == self.Id || !acceptServer(id)) return false;
                    peerId = id; return true;
                }
            }, deadline.Token);
            return (client, ssl, peerId ?? throw new AuthenticationException("Sertifika doğrulanamadı."));
        }
        catch { ssl?.Dispose(); client.Dispose(); throw; }
    }

    /// <summary>Server side of the TLS handshake. Requires a client certificate; the caller decides whether it is trusted.</summary>
    public static async Task<(SslStream Stream, string PeerId)> AcceptAsync(TcpClient client, DeviceIdentity self, CancellationToken token)
    {
        var ssl = new SslStream(client.GetStream(), false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(HandshakeTimeout);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = self.Certificate,
                ClientCertificateRequired = true,
                EnabledSslProtocols = Protocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                // Self-signed by design: trust comes from the pinned fingerprint checked by the caller.
                RemoteCertificateValidationCallback = (_, cert, _, _) => cert != null
            }, deadline.Token);
            var id = DeviceIdentity.Fingerprint(ssl.RemoteCertificate ?? throw new AuthenticationException("İstemci sertifikası yok."));
            if (id == self.Id) throw new AuthenticationException("Kendine bağlantı.");
            return (ssl, id);
        }
        catch { ssl.Dispose(); throw; }
    }
}
