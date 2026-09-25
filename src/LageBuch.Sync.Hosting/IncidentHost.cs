using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Files;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
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
    private readonly string _masterDataJson;
    private readonly int _protocolVersion;
    private readonly int _minimumProtocolVersion;
    private readonly PinRateLimiter _rateLimiter = new();
    private X509Certificate2? _cert;
    private WebApplication? _app;
    private IHubContext<IncidentHub>? _hub;

    public IncidentHost(
        LocalIncidentSession session,
        IClock clock,
        string appVersion,
        IUiDispatcher ui,
        string pin,
        MasterDataSet? masterData = null,
        int protocolVersion = SyncProtocol.ProtocolVersion,
        int minimumProtocolVersion = SyncProtocol.MinimumProtocolVersion)
    {
        _session = session;
        _clock = clock;
        _appVersion = appVersion;
        _pin = pin;
        _ui = ui;

        // Overridable for tests only, the way ConnectAsync takes a reconnectPolicy. The app always
        // takes the defaults: a host advertising anything other than what this build actually speaks
        // is a lie. Tests need it because with MinimumProtocolVersion at its floor there is otherwise
        // no way to construct a legitimately-too-old peer, which would leave the 426 path below
        // shipping untested until the first release that raises the floor — during an Einsatz.
        _protocolVersion = protocolVersion;
        _minimumProtocolVersion = minimumProtocolVersion;

        // Serialized once, here, rather than per request: the Stammdaten editor is a top-level view
        // that an open workspace replaces, so the host's set provably cannot change while sharing.
        // Caching it makes that guarantee structural rather than incidental (#183). Null means a
        // host that has never imported anything — it serves an empty set, and its clients run
        // empty too, because the host is the master unconditionally.
        _masterDataJson = MasterDataJson.Serialize(masterData ?? MasterDataSet.Empty);
    }

    public bool IsRunning => _app is not null;

    public async Task StartAsync(IPAddress bindAddress, int port = SyncProtocol.Port, CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        // Serve TLS with a fresh self-signed cert minted per share session; the client pins it via
        // Trust-on-First-Use (§ P0 #2) rather than the OS trust store.
        (_cert, _) = SyncCertificate.Generate();
        builder.WebHost.UseKestrel(o =>
        {
            // "Bind everything" is requested as either wildcard depending on caller history/tests;
            // ListenAnyIP binds a dual-stack (IPv4+IPv6) socket and falls back to IPv4-only itself
            // if the platform doesn't support IPv6, so callers don't need their own retry logic.
            // A specific address (e.g. loopback, used by every test) still binds exactly that.
            if (bindAddress.Equals(IPAddress.Any) || bindAddress.Equals(IPAddress.IPv6Any))
            {
                o.ListenAnyIP(port, l => l.UseHttps(_cert));
            }
            else
            {
                o.Listen(bindAddress, port, l => l.UseHttps(_cert));
            }
        });

        // Keep the hub's JSON aligned with SyncJson: enums as strings, web (camelCase) naming.
        builder.Services.AddSignalR().AddJsonProtocol(o =>
            o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Bind the polymorphic SyncCommand body with the same enum-as-string contract as the client.
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();

        // Brute-force gate (§ P0 #3): a source IP inside its backoff window is refused with 429 +
        // Retry-After before it even reaches the PIN comparison.
        app.Use(async (context, next) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (_rateLimiter.ShouldThrottle(ip, out var retryAfter))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }

            await next();
        });

        // The join gate (§ #64): every request — the version/snapshot/command HTTP calls and the hub's
        // negotiate/transport requests — must carry the share PIN in SyncProtocol.PinHeader. Rejecting
        // here, before routing, keeps every endpoint and the hub gated with one check. The PIN now
        // travels over TLS (not cleartext), so a LAN sniffer no longer sees it; the gate still stops
        // uninvited joins that know or guess it.
        app.Use(async (context, next) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!PinMatches(context.Request.Headers[SyncProtocol.PinHeader]))
            {
                _rateLimiter.RecordFailure(ip);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            _rateLimiter.RecordSuccess(ip);
            await next();
        });

        // Wire-contract gate (§7): a peer announces the protocol revision it speaks in
        // SyncProtocol.ProtocolHeader, and anything below the oldest revision this host still
        // understands is refused here rather than later, on a command it cannot parse. Deliberately
        // after the PIN gate — auth precedes content, so an uninvited peer never learns which
        // revisions this host speaks.
        //
        // The test is one-sided: below the floor is refused, above the ceiling is not. A peer
        // claiming something newer has already read /version, seen this host's range and chosen to
        // speak down to it; refusing it here would reject a client its own handshake had just
        // approved, and it would find out only on its second request. The host receives one number
        // and cannot evaluate the overlap anyway — the client is the end that decides.
        //
        // SyncProtocol.VersionPath is exempt because it *is* the negotiation endpoint: a peer that
        // cannot read it can never learn why it was refused, or which of the two devices to update.
        // It sits behind the PIN gate already, and carries nothing but two constants and a version
        // string, so exempting it discloses nothing. Note this means a peer with the right PIN but an
        // unusable protocol can poll /version unthrottled — correct, a mismatch is not a brute-force
        // attempt, and moving this block above the PIN check to "fix" that would break the rule above.
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals(new PathString(SyncProtocol.VersionPath), StringComparison.OrdinalIgnoreCase)
                && !ProtocolAccepted(context.Request.Headers[SyncProtocol.ProtocolHeader], _minimumProtocolVersion))
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;

                // The only failure in this pipeline with a body: 401 and 429 are answered by a client
                // that knows what they mean, while this one can reach a human through a generic error
                // banner. Charset spelled out because WriteAsync encodes UTF-8 but does not say so,
                // and the message carries an umlaut.
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Dieses Gerät ist zu alt für den Host. Bitte dieses Gerät aktualisieren.");
                return;
            }

            await next();
        });

        app.MapHub<IncidentHub>(SyncProtocol.HubPath);
        app.MapGet(
            SyncProtocol.VersionPath,
            () => Results.Json(new VersionInfo(_appVersion, _protocolVersion, _minimumProtocolVersion), SyncJson.Options));
        app.MapGet(SyncProtocol.SnapshotPath, () => Results.Json(SnapshotMapper.ToSnapshot(_session.Incident), SyncJson.Options));

        // Results.Content, not Results.Json: the payload is already serialized JSON text, and
        // Results.Json would re-encode it into a JSON string literal. MasterDataJson.Serialize uses
        // UnsafeRelaxedJsonEscaping, so umlauts in e.g. Wachen names travel as raw UTF-8 bytes rather
        // than \uXXXX escapes — pass the encoding explicitly so the response's charset says so too,
        // matching /snapshot's Results.Json (which emits charset=utf-8 by default).
        app.MapGet(SyncProtocol.MasterDataPath, () => Results.Content(_masterDataJson, "application/json", Encoding.UTF8));
        app.MapPost(SyncProtocol.CommandPath, HandleCommand);
        app.MapGet(SyncProtocol.FilesRouteTemplate, HandleGetFile);
        app.MapPut(SyncProtocol.FilesRouteTemplate, HandleUploadFile);

        _hub = app.Services.GetRequiredService<IHubContext<IncidentHub>>();
        _session.Changed += OnSessionChanged; // the host's own edits reach clients too
        await app.StartAsync(cancellationToken);
        _app = app;
    }

    private async Task<IResult> HandleCommand(SyncCommand command)
    {
        // A Kestrel request thread runs this. Apply on the UI thread so the host's authoritative
        // Incident is only ever mutated there (matching solo mode) and the Changed it raises reaches
        // the host's own Avalonia-bound views — which reject an off-thread mutation. An AddFileCommand
        // carries metadata only (issue #167 P1 #2) — the attachment's bytes arrive separately via
        // HandleUploadFile — and a RemoveFileCommand's byte cleanup below runs the same way, off the
        // UI thread, once the metadata mutation and the broadcasted snapshot have already landed.
        //
        // Every command reaching this endpoint comes from a peer — the host's own UI edits go
        // straight through its local session, never over HTTP. Closing the incident is the host's
        // call alone (#465): a joined device, or an older client that still offers the button,
        // must not be able to end the Einsatz for everyone.
        if (command is CloseIncidentCommand)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var removedFile = await _ui.InvokeAsync(() => CommandApplier.Apply(command, _session.Incident, _clock));

            var result = await _ui.InvokeAsync(() =>
            {
                // Enqueue persist + raise the session's Changed, which refreshes the host's own UI and,
                // through OnSessionChanged, broadcasts the new snapshot to every client — the same path
                // a host edit takes (§5), so a client's contribution appears live on the host too.
                // SaveExternalChange only queues the write (issue #167 P0 #1: IncidentStore's
                // background writer owns the actual SQLite I/O), so this dispatch is a snapshot copy,
                // not a full save.
                _session.SaveExternalChange();
                return Results.Json(SnapshotMapper.ToSnapshot(_session.Incident), SyncJson.Options);
            });

            // The other half of RemoveFile's metadata/bytes split: CommandApplier only removed the
            // domain record, so the attachment's bytes are cleaned up here — a pure disk delete
            // against already-persisted state, like HandleUploadFile/HandleGetFile, so it doesn't
            // need the UI-thread dispatch the mutation above uses. Best-effort (see
            // IIncidentFileStore.DeleteBytesAsync) — never blocks or fails the metadata removal that
            // already landed.
            if (command is RemoveFileCommand && removedFile is not null)
            {
                await _session.DeleteFileBytesAsync(IncidentFile.StorageFileName(removedFile.Id, removedFile.FileName));
            }

            return result;
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
        return Results.Bytes(bytes, file?.ContentType ?? IncidentFile.DefaultMimeType, fileDownloadName: file?.FileName);
    }

    // The other half of the metadata/bytes split (issue #167 P1 #2): a client PUTs the raw attachment
    // bytes here, keyed by the id its AddFileCommand already registered. A pure disk write against
    // already-persisted domain state — like HandleGetFile, this doesn't need the UI-thread dispatch
    // HandleCommand uses, and unlike the old inline byte write, it streams straight to disk (never a
    // whole-file byte[] in memory here or in IncidentFileStore.SaveStreamAsync).
    private async Task<IResult> HandleUploadFile(Guid id, HttpRequest request, CancellationToken cancellationToken)
    {
        var file = _session.Incident.Files.FirstOrDefault(f => f.Id == id);
        if (file is null)
        {
            return Results.NotFound();
        }

        if (request.ContentLength is { } contentLength && contentLength > IncidentFile.MaxSizeBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Belt-and-suspenders for a body sent without Content-Length (or one that lies about it):
        // Kestrel aborts the read with a 413 once the body exceeds this, even without the check above.
        var maxBodySizeFeature = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (maxBodySizeFeature is { IsReadOnly: false })
        {
            maxBodySizeFeature.MaxRequestBodySize = IncidentFile.MaxSizeBytes;
        }

        await _session.SaveFileStreamAsync(IncidentFile.StorageFileName(file.Id, file.FileName), request.Body, cancellationToken);
        return Results.NoContent();
    }

    // Exactly one PIN header, matching the host's, is accepted. A missing/duplicated/mismatched header
    // is refused. The comparison is ordinal — the PIN is a short numeric string, not a secret to defend
    // against timing analysis over a LAN it is already carried over TLS.
    private bool PinMatches(Microsoft.Extensions.Primitives.StringValues header) =>
        header.Count == 1 && string.Equals(header[0], _pin, StringComparison.Ordinal);

    // No header at all is a pre-negotiation peer (v0.6.1 or older), which by construction speaks
    // SyncProtocol.LegacyProtocolVersion and is served. Anything present must be exactly one header
    // and must parse: a duplicated or garbled one is refused rather than silently coalesced, the same
    // rule PinMatches applies. Indexed after the count check because StringValues converts implicitly
    // to both string and string[], which makes passing it to TryParse directly ambiguous.
    private static bool ProtocolAccepted(Microsoft.Extensions.Primitives.StringValues header, int minimum) =>
        header.Count == 0
        || (header.Count == 1
            && int.TryParse(header[0], System.Globalization.CultureInfo.InvariantCulture, out var claimed)
            && claimed >= minimum);

    private void OnSessionChanged() => _ = Broadcast(SnapshotMapper.ToSnapshot(_session.Incident));

    private Task Broadcast(IncidentSnapshot snapshot) =>
        _hub is null ? Task.CompletedTask : _hub.Clients.All.SendAsync(SyncProtocol.SnapshotMethod, snapshot);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _session.Changed -= OnSessionChanged;
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
            _app = null;
            _hub = null;
            _cert?.Dispose();
            _cert = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
