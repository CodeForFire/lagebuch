using System.Net;
using System.Text.Json.Serialization;
using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LageBuch.Sync.Hosting;

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
    private readonly string _pin;
    private readonly IUiDispatcher _ui;
    private WebApplication? _app;
    private IHubContext<IncidentHub>? _hub;

    public IncidentHost(LocalIncidentSession session, IClock clock, string appVersion, IUiDispatcher ui, string pin)
    {
        _session = session;
        _clock = clock;
        _appVersion = appVersion;
        _pin = pin;
        _ui = ui;
    }

    public bool IsRunning => _app is not null;

    public async Task StartAsync(IPAddress bindAddress, int port = SyncProtocol.Port)
    {
        if (_app is not null)
        {
            return;
        }

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

        // The join gate (§ #64): every request — the version/snapshot/command HTTP calls and the hub's
        // negotiate/transport requests — must carry the share PIN in SyncProtocol.PinHeader. Rejecting
        // here, before routing, keeps every endpoint and the hub gated with one check. Plain HTTP means
        // the PIN is not secret against a LAN sniffer; it blocks uninvited joins, not eavesdropping.
        app.Use(async (context, next) =>
        {
            if (!PinMatches(context.Request.Headers[SyncProtocol.PinHeader]))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        app.MapHub<IncidentHub>(SyncProtocol.HubPath);
        app.MapGet(SyncProtocol.VersionPath, () => Results.Json(new VersionInfo(_appVersion), SyncJson.Options));
        app.MapGet(SyncProtocol.SnapshotPath, () => Results.Json(SnapshotMapper.ToSnapshot(_session.Incident), SyncJson.Options));
        app.MapPost(SyncProtocol.CommandPath, HandleCommand);
        app.MapGet(SyncProtocol.FilesRouteTemplate, HandleGetFile);

        _hub = app.Services.GetRequiredService<IHubContext<IncidentHub>>();
        _session.Changed += OnSessionChanged; // the host's own edits reach clients too
        await app.StartAsync();
        _app = app;
    }

    private async Task<IResult> HandleCommand(SyncCommand command)
    {
        // A Kestrel request thread runs this. Apply + persist + notify on the UI thread so the host's
        // authoritative Incident is only ever mutated there (matching solo mode) and the Changed it
        // raises reaches the host's own Avalonia-bound views — which reject an off-thread mutation.
        try
        {
            return await _ui.InvokeAsync(() =>
            {
                CommandApplier.Apply(command, _session.Incident, _clock, _session.SaveFileBytes);

                // Persist + raise the session's Changed, which refreshes the host's own UI and, through
                // OnSessionChanged, broadcasts the new snapshot to every client — the same path a host
                // edit takes (§5), so a client's contribution appears live on the host too.
                _session.SaveExternalChange();
                return Results.Json(SnapshotMapper.ToSnapshot(_session.Incident), SyncJson.Options);
            });
        }
        catch (Exception ex) when (ex is IncidentClosedException or ArgumentException or InvalidOperationException
                                       or KeyNotFoundException)
        {
            // The same domain guards a local edit hits — reject cleanly rather than 500. An unknown
            // id (KeyNotFoundException, e.g. EditJournalEntry/RenameFile/ToggleChecklistItem against
            // a stale or forged id) belongs here too, not just the argument/state guards.
            return Results.BadRequest(ex.Message);
        }
    }

    // On-demand pull (§5): the snapshot carries file metadata only, so a client fetches the actual
    // bytes here — the first time it needs them (opening the tab, exporting a PDF), not on every
    // broadcast. Runs on a Kestrel request thread like HandleCommand, but this is a pure read
    // against already-persisted state, so it doesn't need the UI-thread dispatch HandleCommand uses.
    private async Task<IResult> HandleGetFile(Guid id)
    {
        var bytes = await _session.GetFileBytesAsync(id);
        if (bytes is null)
        {
            return Results.NotFound();
        }

        var file = _session.Incident.Files.FirstOrDefault(f => f.Id == id);
        return Results.Bytes(bytes, file?.ContentType ?? "application/octet-stream", fileDownloadName: file?.FileName);
    }

    // Exactly one PIN header, matching the host's, is accepted. A missing/duplicated/mismatched header
    // is refused. The comparison is ordinal — the PIN is a short numeric string, not a secret to defend
    // against timing analysis over a LAN it is already sent in cleartext on.
    private bool PinMatches(Microsoft.Extensions.Primitives.StringValues header) =>
        header.Count == 1 && string.Equals(header[0], _pin, StringComparison.Ordinal);

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
