using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// A TCP relay between a joined client and a host, which a test can break the way a real network
/// breaks — without any hook in production code, and in front of the real <see cref="IncidentHost"/>.
/// <para>
/// It forwards bytes and nothing else, so TLS, the PIN, the protocol gate and SignalR all run end to
/// end through it. <see cref="Pause"/> keeps every connection open but stops moving bytes in either
/// direction: a half-open link, where neither side is told anything and SignalR still believes it is
/// connected. <see cref="Reset"/> tears every connection down with a TCP reset, which a client sees as
/// an error, so SignalR's automatic reconnect runs — through the relay again.
/// </para>
/// </summary>
internal sealed class FaultyRelay : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _stop = new();

    // One reset switch per live link. The link's own LinkAsync owns its sockets outright; Reset only
    // flips the switch, so exactly one party ever closes a socket.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _links = new();
    private readonly SemaphoreSlim _held = new(0);
    private readonly Task _acceptLoop;
    private TaskCompletionSource _flowing = Completed();

    private FaultyRelay(int targetPort)
    {
        _targetPort = targetPort;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    /// <summary>The port a client connects to instead of the host's.</summary>
    public int Port { get; }

    public static FaultyRelay Start(int targetPort) => new(targetPort);

    /// <summary>Stops moving bytes on every connection, current and future, until <see cref="Resume"/>.</summary>
    public void Pause()
    {
        if (_flowing.Task.IsCompleted)
        {
            _flowing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Completes once bytes have arrived at the relay while it was paused and were held there — proof
    /// that a request is on its way into the silence, so a test never has to guess that it is.
    /// </summary>
    public async Task WaitForHeldTrafficAsync(TimeSpan? timeout = null)
    {
        if (!await _held.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("No traffic reached the paused relay.");
        }
    }

    /// <summary>Moves bytes again, including whatever was held while paused.</summary>
    public void Resume() => _flowing.TrySetResult();

    /// <summary>Tears every current connection down with a TCP reset. New connections are relayed as usual.</summary>
    public void Reset()
    {
        foreach (var reset in _links.Values)
        {
            try
            {
                reset.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The link ended on its own between the listing and here.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        Resume();
        Reset();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
            // The listener stopping under AcceptTcpClientAsync is the ordinary way out.
        }

        _stop.Dispose();
        _held.Dispose();
    }

    private static TaskCompletionSource Completed()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }

    // Linger 0 makes the coming close send RST instead of FIN: the peer sees a reset connection, an
    // error, rather than an orderly end of stream.
    private static void CloseWithReset(TcpClient client)
    {
        try
        {
            client.Client.LingerState = new LingerOption(true, 0);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Never connected, or already gone: nothing to reset.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Test-only relay: a link that fails to set up is simply dropped, which is what a real network would do too; the client under test sees the closed socket.")]
    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient downstream;
            try
            {
                downstream = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }

            _ = LinkAsync(downstream);
        }

        async Task LinkAsync(TcpClient down)
        {
            // This method owns both sockets, and the using closes them on every way out. A reset only
            // cancels the link, and the close below then goes out as an RST.
            using var downstream = down;
            using var up = new TcpClient();
            using var reset = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var id = Guid.NewGuid();
            _links[id] = reset;
            try
            {
                await up.ConnectAsync(IPAddress.Loopback, _targetPort, reset.Token);
                await Task.WhenAny(
                    PumpAsync(downstream.GetStream(), up.GetStream(), reset.Token),
                    PumpAsync(up.GetStream(), downstream.GetStream(), reset.Token));
            }
            catch (Exception)
            {
                // Reset, disposal, or the host going away: all end the link.
            }
            finally
            {
                _links.TryRemove(id, out _);
                if (reset.IsCancellationRequested)
                {
                    CloseWithReset(downstream);
                    CloseWithReset(up);
                }
            }
        }
    }

    private async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await from.ReadAsync(buffer, ct)) > 0)
        {
            // Held here while paused: the bytes were accepted from one side and are not passed on,
            // which is exactly what a half-open link does to both peers.
            var flowing = _flowing.Task;
            if (!flowing.IsCompleted)
            {
                _held.Release();
            }

            await flowing.WaitAsync(ct);
            await to.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
