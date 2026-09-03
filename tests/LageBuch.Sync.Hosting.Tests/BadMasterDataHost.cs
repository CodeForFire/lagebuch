using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json.Serialization;
using LageBuch.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// A stand-in host that clears the version handshake, the snapshot fetch and the hub connect but
/// serves a corrupt Stammdaten payload (#183). The real <see cref="IncidentHost"/> serializes a
/// MasterDataSet and so cannot emit invalid JSON without a test-only hook in production code.
/// Used to prove a joining client rejects the host and tears its connection down rather than
/// leaking an open hub connection. No PIN middleware — the client's header is simply ignored.
/// </summary>
internal sealed class BadMasterDataHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConnectionTracker _tracker;

    private BadMasterDataHost(WebApplication app, int port, ConnectionTracker tracker)
    {
        _app = app;
        Port = port;
        _tracker = tracker;
    }

    public int Port { get; }

    public static async Task<BadMasterDataHost> StartAsync(Incident incident, string version = "1.0.0")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var (cert, _) = SyncCertificate.Generate();
        var port = TestHost.FreeTcpPort();
        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, port, l => l.UseHttps(cert)));
        builder.Services.AddSignalR().AddJsonProtocol(o =>
            o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Registered so TrackingHub (DI-activated per connection) can report back to the one
        // instance the test holds onto (#183: proves the join tears the hub connection down
        // instead of leaking it — see WaitForClientDisconnectAsync).
        var tracker = new ConnectionTracker();
        builder.Services.AddSingleton(tracker);

        var app = builder.Build();
        app.MapHub<TrackingHub>(SyncProtocol.HubPath);
        app.MapGet(SyncProtocol.VersionPath, () => Results.Json(new VersionInfo(version), SyncJson.Options));
        app.MapGet(SyncProtocol.SnapshotPath, () => Results.Json(SnapshotMapper.ToSnapshot(incident), SyncJson.Options));

        // The whole point: well-formed HTTP, malformed Stammdaten.
        app.MapGet(SyncProtocol.MasterDataPath, () => Results.Content("{ not json", "application/json"));

        await app.StartAsync();
        return new BadMasterDataHost(app, port, tracker);
    }

    /// <summary>
    /// Waits for the joining client's hub connection to disconnect from the server's point of view.
    /// Server-side <c>OnDisconnectedAsync</c> fires asynchronously, after the client has already
    /// disposed its <c>HubConnection</c>, so this is a bounded wait rather than an immediate check —
    /// mirroring the <c>NextChange</c>/<c>disconnected</c> idiom used elsewhere in this test project
    /// (see e.g. <c>WorkspaceCollaborationTests.Losing_the_host_disconnects_then_returns_the_client_home</c>).
    /// </summary>
    public Task WaitForClientDisconnectAsync(TimeSpan timeout) => _tracker.Disconnected.Task.WaitAsync(timeout);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    // Method-less like the real IncidentHub -- it carries no server-callable methods either -- only
    // overridden here to observe the connection lifecycle for the test above.
    [SuppressMessage(
        "Performance",
        "CA1812",
        Justification = "Activated by SignalR's DI container via MapHub<TrackingHub>, not `new`d directly.")]
    private sealed class TrackingHub : Hub
    {
        private readonly ConnectionTracker _tracker;

        public TrackingHub(ConnectionTracker tracker) => _tracker = tracker;

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _tracker.Disconnected.TrySetResult();
            return base.OnDisconnectedAsync(exception);
        }
    }

    private sealed class ConnectionTracker
    {
        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
