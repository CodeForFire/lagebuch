using System.Net;
using System.Text.Json.Serialization;
using LageBuch.Persistence.MasterData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// A stand-in host whose served state and pushes are driven entirely by the test, over real Kestrel,
/// real TLS and a real SignalR hub — so the client under test is the production
/// <see cref="RemoteIncidentSession"/>, unmodified.
/// <para>
/// It exists because the real <see cref="IncidentHost"/> <em>structurally cannot</em> produce the two
/// failures this reconcile work is about (#295). It broadcasts unconditionally on every applied
/// change, and SignalR delivers in order over one connection, so a host that skips a broadcast or
/// delivers one late is not reachable through it. The alternatives are all worse: killing the
/// transport mid-flight is indeterminate, stopping and restarting the host mints a fresh certificate
/// and the client's Trust-on-First-Use pin then ends the session, joining late still starts current
/// because <c>ConnectAsync</c> fetches <c>/snapshot</c> first, and mutating the host's
/// <c>Incident</c> directly bypasses <c>Changed</c> so its revision never moves.
/// </para>
/// <para>
/// <see cref="Current"/> models the host's true state: <c>/snapshot</c> serves it and
/// <c>/revision</c> serves its <see cref="IncidentSnapshot.Revision"/>, so the two can never
/// disagree — exactly the invariant the real host maintains by reading both on its UI thread.
/// <see cref="PushAsync"/> deliberately does <em>not</em> touch it, which is what lets a test model a
/// stale broadcast still in flight while the host has already moved on.
/// </para>
/// No PIN middleware: the client's header is simply ignored.
/// </summary>
internal sealed class ScriptedSnapshotHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly IHubContext<IncidentHub> _hub;
    private int _commandsReceived;

    private ScriptedSnapshotHost(WebApplication app, int port, IHubContext<IncidentHub> hub)
    {
        _app = app;
        Port = port;
        _hub = hub;
    }

    public int Port { get; }

    /// <summary>
    /// What the host would say if asked right now: served by <c>GET /snapshot</c>, and its
    /// <see cref="IncidentSnapshot.Revision"/> by <c>GET /revision</c>. Set it to move the host
    /// forward (or, with a lower revision, to model a host that restarted sharing).
    /// </summary>
    public IncidentSnapshot Current { get; set; } = null!;

    /// <summary>
    /// When set, <c>POST /command</c> answers 400 with this reason instead of applying anything —
    /// the shape the real host produces from a domain guard.
    /// </summary>
    public string? RejectCommandsWith { get; set; }

    /// <summary>
    /// How many commands reached <c>POST /command</c>. Lets a test prove a command was actually sent
    /// rather than inferring it from state that something else might have produced.
    /// </summary>
    public int CommandsReceived => _commandsReceived;

    public static async Task<ScriptedSnapshotHost> StartAsync(IncidentSnapshot initial, string version = "1.0.0")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var (cert, _) = SyncCertificate.Generate();
        var port = TestHost.FreeTcpPort();
        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, port, l => l.UseHttps(cert)));
        builder.Services.AddSignalR().AddJsonProtocol(o =>
            o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        var host = new ScriptedSnapshotHost(app, port, app.Services.GetRequiredService<IHubContext<IncidentHub>>())
        {
            Current = initial,
        };

        app.MapHub<IncidentHub>(SyncProtocol.HubPath);
        app.MapGet(SyncProtocol.VersionPath, () => Results.Json(new VersionInfo(version), SyncJson.Options));
        app.MapGet(SyncProtocol.SnapshotPath, () => Results.Json(host.Current, SyncJson.Options));
        app.MapGet(SyncProtocol.RevisionPath, () => Results.Json(new RevisionInfo(host.Current.Revision), SyncJson.Options));
        app.MapGet(SyncProtocol.MasterDataPath, () => Results.Content(
            MasterDataJson.Serialize(MasterDataSet.Empty), "application/json"));

        // Answers like the real host -- the fresh snapshot as the 200 body, or a 400 carrying the
        // reason -- but deliberately pushes nothing. That is what makes "the sender converges from its
        // own response" observable at all: on the real host a broadcast is dispatched before the
        // response is written, so it could always have been the broadcast that did it.
        app.MapPost(SyncProtocol.CommandPath, (SyncCommand _) =>
        {
            Interlocked.Increment(ref host._commandsReceived);
            return host.RejectCommandsWith is { } reason
                ? Results.BadRequest(reason)
                : Results.Json(host.Current, SyncJson.Options);
        });

        await app.StartAsync();
        return host;
    }

    /// <summary>
    /// Pushes one snapshot down the hub, without changing <see cref="Current"/> — so a test can send
    /// a snapshot the host has already superseded, which is what a delayed broadcast looks like.
    /// </summary>
    public Task PushAsync(IncidentSnapshot snapshot) =>
        _hub.Clients.All.SendAsync(SyncProtocol.SnapshotMethod, snapshot);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
