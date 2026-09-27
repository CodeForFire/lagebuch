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
    private readonly ConcurrentDictionary<Guid, (TcpClient Downstream, TcpClient Upstream)> _links = new();
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

    /// <summary>Moves bytes again, including whatever was held while paused.</summary>
    public void Resume() => _flowing.TrySetResult();

    /// <summary>Tears every current connection down with a TCP reset. New connections are relayed as usual.</summary>
    public void Reset()
    {
        foreach (var (id, link) in _links)
        {
            if (_links.TryRemove(id, out _))
            {
                Abort(link.Downstream);
                Abort(link.Upstream);
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
    }

    private static TaskCompletionSource Completed()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }

    // Linger 0 makes Close send RST instead of FIN: the peer sees a reset connection, an error, rather
    // than an orderly end of stream.
    private static void Abort(TcpClient client)
    {
        try
        {
            client.Client.LingerState = new LingerOption(true, 0);
            client.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down by the other direction's pump.
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
            var id = Guid.NewGuid();
            var up = new TcpClient();
            try
            {
                await up.ConnectAsync(IPAddress.Loopback, _targetPort, _stop.Token);
                _links[id] = (down, up);
                await Task.WhenAny(
                    PumpAsync(down.GetStream(), up.GetStream()),
                    PumpAsync(up.GetStream(), down.GetStream()));
            }
            catch (Exception)
            {
                // Reset, disposal, or the host going away: all end the link.
            }
            finally
            {
                if (_links.TryRemove(id, out _))
                {
                    down.Dispose();
                    up.Dispose();
                }
            }
        }
    }

    private async Task PumpAsync(NetworkStream from, NetworkStream to)
    {
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await from.ReadAsync(buffer, _stop.Token)) > 0)
        {
            // Held here while paused: the bytes were accepted from one side and are not passed on,
            // which is exactly what a half-open link does to both peers.
            await _flowing.Task.WaitAsync(_stop.Token);
            await to.WriteAsync(buffer.AsMemory(0, read), _stop.Token);
        }
    }
}
