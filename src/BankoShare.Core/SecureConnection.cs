using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace BankoShare.Core;

public sealed class SecureConnection : IDisposable
{
    private readonly TcpClient client;
    private readonly SslStream stream;
    private SecureConnection(TcpClient client, SslStream stream) { this.client = client; this.stream = stream; client.NoDelay = true; }
    public static async Task<SecureConnection> ConnectAsync(string host, int port, string code, CancellationToken token)
    {
        var pair = Pairing.Parse(code);
        var client = new TcpClient();
        SslStream? stream = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await client.ConnectAsync(host, port, deadline.Token);
            stream = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert != null &&
                CryptographicOperations.FixedTimeEquals(cert.GetCertHash(HashAlgorithmName.SHA256), pair.Fingerprint));
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            { TargetHost = "BankoShare", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, deadline.Token);
            await stream.WriteAsync(pair.Secret, deadline.Token);
            var ack = new byte[1];
            await stream.ReadExactlyAsync(ack, deadline.Token);
            if (ack[0] != 1) throw new AuthenticationException("Eşleştirme reddedildi.");
            return new SecureConnection(client, stream);
        }
        catch { stream?.Dispose(); client.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(pair.Secret); }
    }
    public static async Task<SecureConnection> AcceptAsync(TcpClient client, Pairing pairing, CancellationToken token)
    {
        var stream = new SslStream(client.GetStream(), false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            { ServerCertificate = pairing.Certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, deadline.Token);
            var secret = new byte[32];
            try
            {
                await stream.ReadExactlyAsync(secret, deadline.Token);
                if (!CryptographicOperations.FixedTimeEquals(secret, pairing.Secret))
                    throw new AuthenticationException("Eşleştirme reddedildi.");
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
            await stream.WriteAsync(new byte[] { 1 }, deadline.Token);
            return new SecureConnection(client, stream);
        }
        catch { stream.Dispose(); client.Dispose(); throw; }
    }
    // One reader and one writer per connection. All waits are bounded.
    public async Task SendAsync(InputEvent input, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await stream.WriteAsync(input.Encode(), deadline.Token);
    }
    public async Task<InputEvent> ReceiveAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var frame = new byte[InputEvent.Size];
        await stream.ReadExactlyAsync(frame, deadline.Token);
        return InputEvent.Decode(frame);
    }
    public void Dispose() { stream.Dispose(); client.Dispose(); }
}
