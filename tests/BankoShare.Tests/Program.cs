// See https://aka.ms/new-console-template for more information
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using BankoShare.Core;

if (args.Length == 2 && args[0] == "--release-smoke")
{
    await ReleaseSmoke.Run(args[1]);
    return;
}

var passed = 0;
await Test("Protocol round trips and rejects malformed input", () =>
{
    InputEvent[] events = [new(EventKind.Heartbeat), new(EventKind.Move, -100, 200), new(EventKind.Key, 30, 1, 1),
        new(EventKind.Button, 5, 0), new(EventKind.Wheel, -120, 1), new(EventKind.ReleaseAll)];
    foreach (var e in events) Check(InputEvent.Decode(e.Encode()) == e, "Round trip mismatch");
    var bytes = events[0].Encode(); bytes[0] = 2;
    Throws<InvalidDataException>(() => InputEvent.Decode(bytes));
    bytes[0] = 1; bytes[1] = 255;
    Throws<InvalidDataException>(() => InputEvent.Decode(bytes));
    bytes[1] = 0; bytes[2] = 1;
    Throws<InvalidDataException>(() => InputEvent.Decode(bytes));
    Throws<InvalidDataException>(() => InputEvent.Decode(new byte[15]));
    Throws<InvalidDataException>(() => new InputEvent(EventKind.Key, 0).Encode());
    Throws<InvalidDataException>(() => new InputEvent(EventKind.Button, 6).Encode());
    Throws<InvalidDataException>(() => new InputEvent(EventKind.Move, int.MinValue).Encode());
    return Task.CompletedTask;
});
await Test("Pairing codes are per-session and strictly parsed", () =>
{
    using var a = new Pairing(); using var b = new Pairing();
    Check(a.Code != b.Code, "Reused credentials");
    var parsed = Pairing.Parse(a.Code);
    Check(parsed.Secret.SequenceEqual(a.Secret), "Secret mismatch");
    Throws<FormatException>(() => Pairing.Parse("BS1-invalid"));
    Throws<FormatException>(() => Pairing.Parse("BS2-" + new string('0', 64) + "-" + new string('0', 64)));
    return Task.CompletedTask;
});
await Test("TLS authentication and bidirectional event delivery", async () =>
{
    using var pair = new Pairing();
    using var listener = Listen();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var accepting = Accept(listener, pair, limit.Token);
    using var sender = await SecureConnection.ConnectAsync("127.0.0.1", Port(listener), pair.Code, limit.Token);
    using var receiver = await accepting;
    for (var i = 0; i < 100; i++)
    {
        var input = new InputEvent(EventKind.Move, i, -i);
        await sender.SendAsync(input, limit.Token);
        Check(await receiver.ReceiveAsync(limit.Token) == input, "Input mismatch");
    }
    await receiver.SendAsync(new(EventKind.Heartbeat), limit.Token);
    Check((await sender.ReceiveAsync(limit.Token)).Kind == EventKind.Heartbeat, "Reply missing");
    await sender.SendAsync(new(EventKind.ReleaseAll), limit.Token);
    Check((await receiver.ReceiveAsync(limit.Token)).Kind == EventKind.ReleaseAll, "Release missing");
});
await Test("Wrong certificate fingerprint is rejected", () => Rejected(false));
await Test("Wrong pairing secret is rejected", () => Rejected(true));
await Test("Silent peer triggers three-second deadline", async () =>
{
    using var pair = new Pairing(); using var listener = Listen();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var accepting = Accept(listener, pair, limit.Token);
    using var sender = await SecureConnection.ConnectAsync("127.0.0.1", Port(listener), pair.Code, limit.Token);
    using var receiver = await accepting;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    await ThrowsAsync<OperationCanceledException>(() => receiver.ReceiveAsync(limit.Token));
    Check(watch.Elapsed < TimeSpan.FromSeconds(6), "Watchdog was too slow");
});
await Test("Disconnect is detected by receiving peer", async () =>
{
    using var pair = new Pairing(); using var listener = Listen();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var accepting = Accept(listener, pair, limit.Token);
    using var sender = await SecureConnection.ConnectAsync("127.0.0.1", Port(listener), pair.Code, limit.Token);
    using var receiver = await accepting;
    sender.Dispose();
    await ThrowsAsync<IOException>(() => receiver.ReceiveAsync(limit.Token));
});
await Test("Cancellation interrupts an active receive", async () =>
{
    using var pair = new Pairing(); using var listener = Listen();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var accepting = Accept(listener, pair, limit.Token);
    using var sender = await SecureConnection.ConnectAsync("127.0.0.1", Port(listener), pair.Code, limit.Token);
    using var receiver = await accepting;
    using var cancel = new CancellationTokenSource(100);
    await ThrowsAsync<OperationCanceledException>(() => receiver.ReceiveAsync(cancel.Token));
});
passed += await UpdateTests.Run();
Console.WriteLine($"{passed} tests passed. No OS input was injected.");

async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine($"PASS {name}"); }
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
static TcpListener Listen() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return listener; }
static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;
static async Task<SecureConnection> Accept(TcpListener listener, Pairing pair, CancellationToken token)
{
    try { return await SecureConnection.AcceptAsync(await listener.AcceptTcpClientAsync(token), pair, token); }
    catch (Exception e) { Console.WriteLine($"Server result: {e.GetType().Name}: {e.Message} {e.InnerException?.Message}"); throw; }
}
static async Task Rejected(bool wrongSecret)
{
    using var pair = new Pairing(); using var other = new Pairing(); using var listener = Listen();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var parts = pair.Code.Split('-');
    parts[wrongSecret ? 2 : 1] = other.Code.Split('-')[wrongSecret ? 2 : 1];
    var accepting = Accept(listener, pair, limit.Token);
    var failed = false;
    try { using var unexpected = await SecureConnection.ConnectAsync("127.0.0.1", Port(listener), string.Join('-', parts), limit.Token); }
    catch (Exception e) when (e is AuthenticationException or IOException) { failed = true; }
    Check(failed, "Unauthenticated peer accepted");
    try { using var unexpected = await accepting; throw new Exception("Server accepted unauthenticated peer"); }
    catch (Exception e) when (e is AuthenticationException or IOException) { }
}
