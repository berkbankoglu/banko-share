using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BankoShare.Core;

public sealed record DiscoveredDevice(string Id, string Name, IPEndPoint Endpoint, DateTimeOffset LastSeen);
public sealed record PairingOffer(string Code, DateTimeOffset Expires);
internal sealed record Announcement(string Magic, string Id, string Name, int Port);

/// <summary>
/// Owns every connection: a listener for paired devices and pairing attempts, outbound reconnects to
/// paired devices with backoff, and UDP discovery. Keeps at most one live link per peer.
/// </summary>
public sealed class PeerNetwork : IDisposable
{
    public const int DefaultPort = 24819;
    public const int DiscoveryPort = 24820;
    public const int MaxPairingAttempts = 5;
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    private const string Magic = "BankoShare/1";

    private readonly DeviceIdentity self;
    private readonly string name;
    private readonly int requestedPort;
    private readonly bool discovery;
    private readonly CancellationTokenSource life = new();
    private readonly object gate = new();
    private readonly Dictionary<string, PeerLink> links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiscoveredDevice> discovered = new(StringComparer.Ordinal);
    private readonly HashSet<string> connecting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset Next, int Failures)> backoff = new(StringComparer.Ordinal);
    private (string Secret, PairingOffer Offer, int Attempts)? pairing;
    private TcpListener? listener;
    private UdpClient? udp;

    public PeerDirectory Directory { get; }
    public int Port { get; private set; }
    public string? DiscoveryError { get; private set; }
    public event Action<PeerLink>? LinkUp;
    public event Action<PeerLink>? LinkDown;
    public event Action<PeerLink, Frame>? FrameReceived;
    public event Action<string>? Paired;
    /// <summary>Links, discovery results or the pairing offer changed.</summary>
    public event Action? Changed;

    public PeerNetwork(DeviceIdentity self, string name, PeerDirectory directory, int port = DefaultPort, bool discovery = true)
    {
        this.self = self; this.name = PeerDirectory.CleanName(name); Directory = directory; requestedPort = port; this.discovery = discovery;
    }

    public void Start()
    {
        listener = new TcpListener(IPAddress.Any, requestedPort);
        listener.Start(8);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptLoop(life.Token);
        _ = ConnectLoop(life.Token);
        if (discovery) StartDiscovery();
    }

    public bool IsConnected(string id) { lock (gate) return links.ContainsKey(id); }
    public IReadOnlyList<PeerLink> Links { get { lock (gate) return [.. links.Values]; } }
    public bool Send(string id, Frame frame) { PeerLink? link; lock (gate) links.TryGetValue(id, out link); return link?.TrySend(frame) == true; }
    public void Broadcast(Frame frame) { foreach (var link in Links) link.TrySend(frame); }

    public IReadOnlyList<DiscoveredDevice> Discovered
    {
        get { var fresh = DateTimeOffset.UtcNow.AddSeconds(-10); lock (gate) return [.. discovered.Values.Where(d => d.LastSeen > fresh).OrderBy(d => d.Name)]; }
    }

    public PairingOffer? CurrentPairing
    {
        get { lock (gate) return pairing is { } p && p.Offer.Expires > DateTimeOffset.UtcNow ? p.Offer : null; }
    }

    /// <summary>Shows a one-time code on this device. Another device enters it to pair with us.</summary>
    public PairingOffer OpenPairing()
    {
        var code = PairingCode.Create(self.Id, out var secret);
        var offer = new PairingOffer(code, DateTimeOffset.UtcNow + PairingLifetime);
        lock (gate) pairing = (secret, offer, 0);
        Changed?.Invoke();
        return offer;
    }

    public void ClosePairing() { lock (gate) pairing = null; Changed?.Invoke(); }

    /// <summary>Pairs with the device that shows <paramref name="code"/> and keeps the connection open.</summary>
    public async Task<string> PairAsync(IPEndPoint endpoint, string code, CancellationToken token)
    {
        var (prefix, secret) = PairingCode.Parse(code);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, life.Token);
        TcpClient client; System.Net.Security.SslStream ssl; string peerId;
        try { (client, ssl, peerId) = await PeerLink.ConnectAsync(endpoint, self, id => PairingCode.Matches(id, prefix), linked.Token); }
        catch (AuthenticationException) { throw new AuthenticationException("Bu kod o adresteki bilgisayara ait değil. Adresi ve kodu kontrol edin."); }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            deadline.CancelAfter(PeerLink.HandshakeTimeout);
            await Frame.WriteAsync(ssl, Frame.Of(FrameType.PairRequest, new PairRequestMessage(secret, name)), deadline.Token);
            Frame reply;
            try { reply = await Frame.ReadAsync(ssl, deadline.Token); }
            catch (IOException) { throw new AuthenticationException("Kod yanlış ya da süresi dolmuş. Diğer bilgisayarda yeni kod oluşturun."); }
            if (reply.Type != FrameType.PairAccepted) throw new AuthenticationException("Eşleştirme reddedildi.");
        }
        catch { ssl.Dispose(); client.Dispose(); throw; }
        Directory.AddOrUpdate(peerId, address: endpoint.ToString());
        Register(new PeerLink(client, ssl, peerId, initiator: true), null);
        Paired?.Invoke(peerId);
        return peerId;
    }

    public void Unpair(string id)
    {
        Directory.Remove(id);
        PeerLink? link; lock (gate) { links.TryGetValue(id, out link); backoff.Remove(id); }
        link?.Close();
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener!.AcceptTcpClientAsync(token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { await Task.Delay(200, CancellationToken.None); continue; }
            _ = HandleIncoming(client, token);
        }
    }

    private async Task HandleIncoming(TcpClient client, CancellationToken token)
    {
        try
        {
            var (ssl, peerId) = await PeerLink.AcceptAsync(client, self, token);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(PeerLink.HandshakeTimeout);
                // Every connection opens with one frame: a pairing request, or normal traffic from a paired device.
                var first = await Frame.ReadAsync(ssl, deadline.Token);
                if (first.Type == FrameType.PairRequest)
                {
                    var request = first.As<PairRequestMessage>();
                    if (!TryConsumePairing(request.Secret)) throw new AuthenticationException("Eşleştirme reddedildi.");
                    Directory.AddOrUpdate(peerId, request.Name);
                    await Frame.WriteAsync(ssl, Frame.Empty(FrameType.PairAccepted), deadline.Token);
                    Paired?.Invoke(peerId);
                }
                else if (!Directory.IsPaired(peerId)) throw new AuthenticationException("Eşleşmemiş cihaz.");
                Register(new PeerLink(client, ssl, peerId, initiator: false), first.Type is FrameType.Heartbeat or FrameType.PairRequest ? null : first);
            }
            catch { ssl.Dispose(); throw; }
        }
        catch (Exception) { client.Dispose(); }
    }

    private bool TryConsumePairing(string? secret)
    {
        bool ok;
        lock (gate)
        {
            if (pairing is not { } p || p.Offer.Expires <= DateTimeOffset.UtcNow) { pairing = null; return false; }
            ok = secret is { Length: PairingCode.SecretChars } &&
                CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(secret), Encoding.ASCII.GetBytes(p.Secret));
            // Single use; repeated wrong guesses burn the code.
            pairing = ok || p.Attempts + 1 >= MaxPairingAttempts ? null : p with { Attempts = p.Attempts + 1 };
        }
        Changed?.Invoke();
        return ok;
    }

    private async Task ConnectLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var peer in Directory.All)
            {
                if (!IPEndPoint.TryParse(peer.Address ?? "", out var endpoint)) continue;
                lock (gate)
                {
                    if (links.ContainsKey(peer.Id) || connecting.Contains(peer.Id)) continue;
                    if (backoff.TryGetValue(peer.Id, out var b) && b.Next > now) continue;
                    connecting.Add(peer.Id);
                }
                _ = ConnectTo(peer.Id, endpoint, token);
            }
            try { await Task.Delay(500, token); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConnectTo(string peerId, IPEndPoint endpoint, CancellationToken token)
    {
        try
        {
            var (client, ssl, id) = await PeerLink.ConnectAsync(endpoint, self, x => x == peerId, token);
            lock (gate) backoff.Remove(peerId);
            Register(new PeerLink(client, ssl, id, initiator: true), null);
        }
        catch (Exception)
        {
            lock (gate)
            {
                var failures = backoff.TryGetValue(peerId, out var b) ? b.Failures + 1 : 1;
                backoff[peerId] = (DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, 1 << Math.Min(failures, 5))), failures);
            }
        }
        finally { lock (gate) connecting.Remove(peerId); }
    }

    /// <summary>
    /// Both sides may connect at once. Each keeps the link opened by the device with the smaller id;
    /// between two links in the same direction the newer one wins (the old one is usually dead).
    /// </summary>
    private void Register(PeerLink link, Frame? initial)
    {
        PeerLink? replaced = null;
        lock (gate)
        {
            if (life.IsCancellationRequested) { link.Close(); return; }
            if (links.TryGetValue(link.PeerId, out var existing) && !existing.IsClosed)
            {
                if (Preferred(existing) && !Preferred(link)) { link.Close(); return; }
                replaced = existing;
            }
            links[link.PeerId] = link;
        }
        link.Start((l, f) => FrameReceived?.Invoke(l, f), OnClosed, initial, l => LinkUp?.Invoke(l));
        replaced?.Close();
        Changed?.Invoke();
    }

    private bool Preferred(PeerLink link) =>
        string.CompareOrdinal(link.Initiator ? self.Id : link.PeerId, link.Initiator ? link.PeerId : self.Id) < 0;

    private void OnClosed(PeerLink link)
    {
        bool current;
        lock (gate) { current = links.TryGetValue(link.PeerId, out var c) && c == link; if (current) links.Remove(link.PeerId); }
        if (!current) return;
        LinkDown?.Invoke(link);
        Changed?.Invoke();
    }

    private void StartDiscovery()
    {
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        }
        catch (SocketException e) { DiscoveryError = e.Message; udp?.Dispose(); udp = null; return; }
        _ = Announce(life.Token);
        _ = ListenForAnnouncements(life.Token);
    }

    private async Task Announce(CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Announcement(Magic, self.Id, name, Port));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                foreach (var target in BroadcastAddresses())
                    try { await udp!.SendAsync(payload, new IPEndPoint(target, DiscoveryPort), token); }
                    catch (SocketException) { }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ListenForAnnouncements(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await udp!.ReceiveAsync(token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            if (packet.Buffer.Length > 1024) continue;
            Announcement? a;
            try { a = JsonSerializer.Deserialize<Announcement>(packet.Buffer); } catch (JsonException) { continue; }
            if (a == null || a.Magic != Magic || !DeviceIds.IsValid(a.Id) || a.Id == self.Id || a.Port is < 1 or > 65535) continue;
            // Unauthenticated hint only: it can change where we connect, never whom we trust.
            var endpoint = new IPEndPoint(packet.RemoteEndPoint.Address.MapToIPv4(), a.Port);
            var device = new DiscoveredDevice(a.Id, PeerDirectory.CleanName(a.Name), endpoint, DateTimeOffset.UtcNow);
            bool isNew;
            lock (gate) { isNew = !discovered.TryGetValue(a.Id, out var old) || old.Name != device.Name || !old.Endpoint.Equals(endpoint); discovered[a.Id] = device; }
            if (Directory.IsPaired(a.Id)) Directory.AddOrUpdate(a.Id, address: endpoint.ToString());
            if (isNew) Changed?.Invoke();
        }
    }

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        HashSet<IPAddress> result = [IPAddress.Broadcast];
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    var ip = u.Address.GetAddressBytes(); var mask = u.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    result.Add(new IPAddress(ip.Zip(mask, (a, m) => (byte)(a | ~m)).ToArray()));
                }
            }
        }
        catch (NetworkInformationException) { }
        return result;
    }

    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address))
                .Select(u => u.Address)];
        }
        catch (NetworkInformationException) { return []; }
    }

    public void Dispose()
    {
        life.Cancel();
        try { listener?.Stop(); } catch (SocketException) { }
        udp?.Dispose();
        foreach (var link in Links) link.Close();
    }
}
