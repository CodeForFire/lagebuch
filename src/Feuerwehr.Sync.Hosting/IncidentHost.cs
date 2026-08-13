using System.Net;
using System.Text.Json.Serialization;
using Feuerwehr.AppLogic;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Feuerwehr.Sync.Hosting;

/// <summary>
/// Runs an embedded Kestrel + SignalR server exposing one open incident to joined clients. Client
/// commands are applied to the host's authoritative session via <see cref="CommandApplier"/>
/// (host clock, per-device operator), saved, and the full snapshot is broadcast to everyone — and
/// the host's own UI edits broadcast the same way through the session's <see cref="LocalIncidentSession.Changed"/>
/// event. Solo mode is simply never calling <see cref="StartAsync"/>.
/// </summary>
public sealed class IncidentHost : IAsyncDisposable
{
    private readonly LocalIncidentSession _session;
    private readonly IClock _clock;
    private readonly string _appVersion;
    private WebApplication? _app;
    private IHubContext<IncidentHub>? _hub;

    public IncidentHost(LocalIncidentSession session, IClock clock, string appVersion)
    {
        _session = session;
        _clock = clock;
        _appVersion = appVersion;
    }

    public bool IsRunning => _app is not null;

    public async Task StartAsync(IPAddress bindAddress, int port = SyncProtocol.Port)
    {
        if (_app is not null)
            return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{bindAddress}:{port}");
        // Keep the hub's JSON aligned with SyncJson: enums as strings, web (camelCase) naming.
        builder.Services.AddSignalR().AddJsonProtocol(o =>
            o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        // Bind the polymorphic SyncCommand body with the same enum-as-string contract as the client.
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        app.MapHub<IncidentHub>(SyncProtocol.HubPath);
        app.MapGet(SyncProtocol.VersionPath, () => Results.Json(new VersionInfo(_appVersion), SyncJson.Options));
        app.MapGet(SyncProtocol.SnapshotPath, () => Results.Json(SnapshotMapper.ToSnapshot(_session.Incident), SyncJson.Options));
        app.MapPost(SyncProtocol.CommandPath, HandleCommand);

        _hub = app.Services.GetRequiredService<IHubContext<IncidentHub>>();
        _session.Changed += OnSessionChanged; // the host's own edits reach clients too
        await app.StartAsync();
        _app = app;
    }

    private async Task<IResult> HandleCommand(SyncCommand command)
    {
        try
        {
            CommandApplier.Apply(command, _session.Incident, _clock);
        }
        catch (Exception ex) when (ex is IncidentClosedException or ArgumentException or InvalidOperationException)
        {
            // The same domain guards a local edit hits — reject cleanly rather than 500.
            return Results.BadRequest(ex.Message);
        }

        _session.Save();
        var snapshot = SnapshotMapper.ToSnapshot(_session.Incident);
        await Broadcast(snapshot);
        return Results.Json(snapshot, SyncJson.Options);
    }

    private void OnSessionChanged() => _ = Broadcast(SnapshotMapper.ToSnapshot(_session.Incident));

    private Task Broadcast(IncidentSnapshot snapshot) =>
        _hub is null ? Task.CompletedTask : _hub.Clients.All.SendAsync(SyncProtocol.SnapshotMethod, snapshot);

    public async Task StopAsync()
    {
        _session.Changed -= OnSessionChanged;
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
            _hub = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
