using System.Net;
using System.Text.Json.Serialization;
using LageBuch.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

    private BadMasterDataHost(WebApplication app, int port)
    {
        _app = app;
        Port = port;
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

        var app = builder.Build();
        app.MapHub<IncidentHub>(SyncProtocol.HubPath);
        app.MapGet(SyncProtocol.VersionPath, () => Results.Json(new VersionInfo(version), SyncJson.Options));
        app.MapGet(SyncProtocol.SnapshotPath, () => Results.Json(SnapshotMapper.ToSnapshot(incident), SyncJson.Options));

        // The whole point: well-formed HTTP, malformed Stammdaten.
        app.MapGet(SyncProtocol.MasterDataPath, () => Results.Content("{ not json", "application/json"));

        await app.StartAsync();
        return new BadMasterDataHost(app, port);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
