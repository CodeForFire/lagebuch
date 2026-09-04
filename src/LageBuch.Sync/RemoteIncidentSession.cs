using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.ValueObjects;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LageBuch.Sync;

/// <summary>
/// A thin client onto another device's hosted incident (§4). It holds no authority: every mutation
/// is POSTed to the host as a command and nothing is written locally — the cached <see cref="Incident"/>
/// is only ever replaced by a host broadcast, so this device renders exactly what the host says.
/// On a dropped connection it raises <see cref="Disconnected"/>; on reconnect it re-fetches the full
/// snapshot rather than attempting incremental catch-up (§7).
/// </summary>
public sealed class RemoteIncidentSession : IIncidentSession, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HubConnection _hub;
    private readonly IUiDispatcher _ui;
    private readonly string? _cacheRoot;
    private Incident _incident;

    public SessionOperator? Operator { get; }

    /// <summary>
    /// The host's Stammdaten, verbatim, in the <c>MasterDataJson</c> interchange format (#183).
    /// Deliberately left unparsed here: this project references only LageBuch.Domain, while
    /// MasterDataSet lives in LageBuch.Persistence — AppLogic, which references both, owns the
    /// parse. Never null: <see cref="ConnectAsync"/> either fetches the payload or throws.
    /// </summary>
    public string HostMasterDataJson { get; }

    public Incident Incident => _incident;

    // The client never writes locally, so it is never "read-only" in the editing sense — but a
    // closed incident rejects mutations at the host anyway, and the workspace uses this to grey out.
    public bool IsReadOnly => _incident.State == IncidentState.Closed;

    // This is the joined-client side: autonomous time-driven logging belongs to the host (§ IsRemote).
    public bool IsRemote => true;

    /// <summary>Raised after the cached incident is replaced by a host broadcast (or a resync).</summary>
    [SuppressMessage("Design", "CA1003", Justification = "In-process fire-and-forget event with C#-only subscribers; see IIncidentSession.Changed.")]
    public event Action? Changed;

    /// <summary>
    /// Raised when the connection drops but automatic reconnect is still trying — the UI should
    /// disable input and show "Verbindung getrennt — verbinde neu…". A successful retry raises
    /// <see cref="Reconnected"/>; giving up raises <see cref="Ended"/>.
    /// </summary>
    [SuppressMessage("Design", "CA1003", Justification = "In-process fire-and-forget event with C#-only subscribers; see IIncidentSession.Changed.")]
    public event Action? Disconnected;

    /// <summary>Raised after a reconnect + full resync — the UI can re-enable input.</summary>
    [SuppressMessage("Design", "CA1003", Justification = "In-process fire-and-forget event with C#-only subscribers; see IIncidentSession.Changed.")]
    public event Action? Reconnected;

    /// <summary>
    /// Raised when the connection is gone for good — reconnect attempts were exhausted or the host
    /// stopped sharing. The UI returns to Home (§7); nothing further arrives on this session.
    /// </summary>
    [SuppressMessage("Design", "CA1003", Justification = "In-process fire-and-forget event with C#-only subscribers; see IIncidentSession.Changed.")]
    public event Action? Ended;

    private RemoteIncidentSession(
        HttpClient http,
        HubConnection hub,
        IUiDispatcher ui,
        SessionOperator op,
        Incident initial,
        string? cacheRoot,
        string hostMasterDataJson)
    {
        _http = http;
        _hub = hub;
        _ui = ui;
        Operator = op;
        _incident = initial;
        _cacheRoot = cacheRoot;
        HostMasterDataJson = hostMasterDataJson;
    }

    /// <summary>
    /// Version-handshakes, fetches the initial snapshot, and opens the push channel. Throws
    /// <see cref="PinRejectedException"/> when the host refuses the share PIN (either a wrong/missing
    /// PIN, i.e. a 401, or a rate-limited one, i.e. a 429 after too many failed attempts),
    /// <see cref="VersionMismatchException"/> on a version mismatch,
    /// <see cref="CertificateChangedException"/> when the host presents a certificate that differs
    /// from the one previously trusted for that address, and
    /// <see cref="HttpRequestException"/> when the host isn't sharing / is unreachable — including
    /// when the Stammdaten fetch itself fails, which surfaces the same way.
    /// </summary>
    /// <param name="host">The host's Tailscale/LAN address to dial.</param>
    /// <param name="op">This device's operator, attributed on every command it sends.</param>
    /// <param name="localVersion">This device's app version, compared against the host's.</param>
    /// <param name="ui">Dispatcher used to marshal SignalR callbacks onto the UI thread.</param>
    /// <param name="pin">The host's share PIN, if it requires one.</param>
    /// <param name="port">The host's port — <see cref="SyncProtocol.Port"/> unless overridden (tests).</param>
    /// <param name="reconnectPolicy">Overrides the default reconnect policy (tests only).</param>
    /// <param name="cacheRoot">
    /// Folder to cache pulled attachment bytes in, keyed by incident and file id (see
    /// <see cref="GetFileStreamAsync"/>). This project has no platform path knowledge, so callers
    /// supply it (a folder under the app's data/cache dir). Null disables caching — bytes are
    /// re-fetched from the host on every call, which is correct, just not free.
    /// </param>
    /// <param name="trustStore">
    /// Optional store of trusted TLS thumbprints, keyed by host address, driving Trust-on-First-Use:
    /// on first contact the presented certificate's thumbprint is saved and accepted; on a later
    /// connect the host's certificate is accepted only if its thumbprint still matches the saved one,
    /// otherwise <see cref="CertificateChangedException"/> is thrown. When null the host's (self-signed)
    /// certificate is accepted as-is, preserving the pre-TOFU behavior.
    /// </param>
    /// <param name="ct">Cancels the connect handshake.</param>
    public static async Task<RemoteIncidentSession> ConnectAsync(
        string host,
        SessionOperator op,
        string localVersion,
        IUiDispatcher ui,
        string? pin = null,
        int port = SyncProtocol.Port,
        IRetryPolicy? reconnectPolicy = null,
        string? cacheRoot = null,
        ITrustStore? trustStore = null,
        CancellationToken ct = default)
    {
        var baseUri = new Uri($"https://{host}:{port}");

        // A single handler backs both the HttpClient and the SignalR hub connection, so they agree on
        // TLS validation. With a trust store, pin the presented cert via Trust-on-First-Use; without
        // one, accept any cert (the host serves a fresh self-signed cert per share session, so it is
        // never in the OS trust store — accepting it is what keeps the pre-TOFU path working). A
        // certificate that differs from the previously-trusted one throws CertificateChangedException
        // from inside the callback; the connect await surfaces it (§ P0 #2).
#pragma warning disable CA2000 // The handler's lifetime is taken on by the HttpClient below (disposed in DisposeAsync after the hub), so an unconditional using would dispose it while the hub's long-lived transport was still using it.
        var handler = new HttpClientHandler { CheckCertificateRevocationList = true };
        if (trustStore is not null)
        {
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null)
                {
                    return false;
                }

                var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
                var known = trustStore.GetThumbprint(host);
                if (known is null)
                {
                    trustStore.SaveThumbprint(host, thumbprint);
                    return true;
                }

                if (string.Equals(known, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                throw new CertificateChangedException(host);
            };
        }
        else
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
#pragma warning restore CA2000
        var http = new HttpClient(handler) { BaseAddress = baseUri };
        if (!string.IsNullOrEmpty(pin))
        {
            http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        }

        try
        {
            // The PIN gates every endpoint, so the first request already reflects it: a 401 means the
            // PIN is wrong/missing — reported as such before the version compare (auth precedes content).
            // A cert that differs from the trusted one makes the TLS handshake fail: .NET wraps the
            // CertificateChangedException the callback threw in an HttpRequestException, so unwrap and
            // rethrow it so the cert change surfaces as its typed exception, not an opaque HTTP error.
            HttpResponseMessage versionResponse;
            try
            {
                versionResponse = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute), ct);
            }
            catch (HttpRequestException ex) when (FindInner<CertificateChangedException>(ex) is { } certChanged)
            {
                throw certChanged;
            }

            if (versionResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new PinRejectedException();
            }

            if (versionResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = versionResponse.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                throw new PinRejectedException($"Zu viele Fehlversuche. Bitte {retryAfter:F0}s warten.");
            }

            versionResponse.EnsureSuccessStatusCode();

            var hostVersion = SyncJson.Deserialize<VersionInfo>(await versionResponse.Content.ReadAsStringAsync(ct)).Version;
            if (hostVersion != localVersion)
            {
                throw new VersionMismatchException(localVersion, hostVersion);
            }

            // The host is the Stammdaten master (#183). Pulled on the same HttpClient as everything
            // else, so the PIN header and the Trust-on-First-Use certificate pin apply unchanged.
            // Deliberately not re-fetched on reconnect: the host caches its serialized set at
            // StartAsync, and both that cached copy and this client's workspace hold the same
            // MasterDataSet as an immutable value fixed at open — the Stammdaten editor stays
            // reachable throughout, but an edit made there produces a new value, it doesn't mutate
            // the one already handed out. A resync round trip here would buy nothing.
            var hostMasterDataJson = await http.GetStringAsync(
                new Uri(SyncProtocol.MasterDataPath, UriKind.RelativeOrAbsolute), ct);

            var initial = SnapshotMapper.FromSnapshot(
                SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute), ct)));

            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(baseUri, SyncProtocol.HubPath), o =>
                {
                    if (!string.IsNullOrEmpty(pin))
                    {
                        o.Headers.Add(SyncProtocol.PinHeader, pin);
                    }

                    o.HttpMessageHandlerFactory = _ => handler;
                })
                .WithAutomaticReconnect(reconnectPolicy ?? new ReconnectForAWhile())
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                .Build();

            var session = new RemoteIncidentSession(http, hub, ui, op, initial, cacheRoot, hostMasterDataJson);
            hub.On<IncidentSnapshot>(SyncProtocol.SnapshotMethod, session.OnSnapshot);

            // Every SignalR callback below arrives on the hub's receive loop, off the UI thread; each is
            // marshalled onto the UI thread because it drives view state (the reconnect banner, the
            // return-Home navigation) exactly as OnSnapshot drives the journal.
            // Reconnecting = transient drop (keep the workspace open, disable input); Closed = the
            // reconnect window ran out or the host went away for good (return to Home).
            hub.Reconnecting += _ =>
            {
                session._ui.Post(() => session.Disconnected?.Invoke());
                return Task.CompletedTask;
            };
            hub.Reconnected += async _ =>
            {
                await session.ResyncAsync();
                session._ui.Post(() => session.Reconnected?.Invoke());
            };
            hub.Closed += _ =>
            {
                session._ui.Post(() => session.Ended?.Invoke());
                return Task.CompletedTask;
            };
            await hub.StartAsync(ct);
            return session;
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    // --- IIncidentSession mutation surface: every call is a fire-and-forget command to the host;
    //     the resulting state arrives via the broadcast, never from these calls. ---
    public void AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null) =>
        Send(new AddJournalEntryCommand(Op(), direction, text, from, to));

    public void EditJournalEntry(Guid entryId, string text) =>
        Send(new EditJournalEntryCommand(Op(), entryId, text));

    public void ToggleChecklistItem(Guid itemId) => Send(new ToggleChecklistItemCommand(Op(), itemId));

    public void AssignRole(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null) =>
        Send(new AssignRoleCommand(Op(), role, personName, callSign, from, to, section, phone));

    public void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null) =>
        Send(new TransferRoleCommand(Op(), assignmentId, newPersonName, newCallSign, newPhone));

    public void EditRolePhone(Guid assignmentId, string? phone) =>
        Send(new EditRolePhoneCommand(Op(), assignmentId, phone));

    public void AddForceUnit(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0) =>
        Send(new AddForceUnitCommand(Op(), brigade, personnelCount, callSign, status, notes, scbaCount, officerCount));

    public void UpdateForceUnit(Guid unitId, string? status, string? notes) =>
        Send(new UpdateForceUnitCommand(Op(), unitId, status, notes));

    public void UpdateForceStrength(Guid unitId, int officerCount, int personnelCount, int scbaCount) =>
        Send(new UpdateForceStrengthCommand(Op(), unitId, officerCount, personnelCount, scbaCount));

    public void RemoveForceUnit(Guid unitId) =>
        Send(new RemoveForceUnitCommand(Op(), unitId));

    public void AddTask(string text, string? assignee, TaskImportance importance, TaskUrgency urgency, int timerMinutes) =>
        Send(new AddTaskCommand(Op(), text, assignee ?? string.Empty, importance, urgency, timerMinutes));

    public void SetTaskCompleted(Guid taskId, bool isDone) =>
        Send(new SetTaskCompletedCommand(Op(), taskId, isDone));

    public void AddScbaTrupp(
        string designation,
        IEnumerable<TruppMember> members,
        int entryPressure,
        int? truppNumber = null,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes) =>
        Send(new AddScbaTruppCommand(
            designation,
            members.Select(m => new TruppMemberDto(m.Role, m.Name)).ToList(),
            callSign,
            task,
            maxDurationMinutes,
            returnPressureBar,
            pressureControlIntervalMinutes,
            entryPressure,
            truppNumber));

    public void StartScbaTrupp(Guid truppId) => Send(new StartScbaTruppCommand(truppId));

    public void RecordScbaPressure(Guid truppId, int bar) => Send(new RecordScbaPressureCommand(truppId, bar));

    public void WithdrawScbaTrupp(Guid truppId) => Send(new WithdrawScbaTruppCommand(truppId));

    public void MarkScbaRemoved(Guid truppId) => Send(new MarkScbaRemovedCommand(truppId));

    public void SetIncidentNumber(IncidentNumber? number) => Send(new SetIncidentNumberCommand(number?.Value));

    public void SetKeyword(string? keyword) => Send(new SetKeywordCommand(keyword));

    public void SetAddress(string? street, string? district) => Send(new SetAddressCommand(street, district));

    public void SetStatus(string? status) => Send(new SetStatusCommand(status));

    // No-op: incident-level timers (the ILS reminder) are host-authoritative and never built on a
    // joined client (IncidentWorkspaceViewModel gates the reminder on !IsRemote), so this is unreachable
    // here. The host's persisted timer state still rides the broadcast snapshot as read-only display.
    public void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning)
    {
    }

    public void Close() => Send(new CloseIncidentCommand(Op()));

    // Unlike every other mutation, this is a real upload — genuinely awaited (per IIncidentSession's
    // doc comment) rather than fire-and-forget, so the caller can show a spinner and catch a
    // rejection (over the size cap, unsupported type, closed incident, or a network failure here).
    // Issue #167 P1 #2: bytes no longer ride the AddFileCommand JSON — the client generates the file
    // id, registers metadata via the usual command, then PUTs the raw bytes as a second request, so
    // the base64/JSON inflation and the host's UI-thread block (issue #167 P1 #1) both go away.
    public async Task AddFileAsync(string fileName, string contentType, Stream content, long sizeBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (sizeBytes > IncidentFile.MaxSizeBytes)
        {
            throw new ArgumentException(
                $"Datei ist größer als das Limit von {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB.", nameof(sizeBytes));
        }

        var fileId = Guid.NewGuid();
        await SendAsync(new AddFileCommand(Op(), fileId, fileName, contentType, sizeBytes), cancellationToken);

        using var body = new StreamContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await _http.PutAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // On-demand pull (§5): the cached incident carries only file metadata (from the snapshot), so
    // bytes are fetched from the host the first time they're needed and cached locally afterwards —
    // mirroring how a join fetches GET /snapshot once rather than having it pushed continuously. The
    // caller owns and disposes the returned stream (issue #167 P1: nothing here buffers a whole
    // attachment in memory — a cache hit opens the cached file directly, a cache miss streams the
    // download straight to the cache file before opening it, and with caching disabled the live HTTP
    // response stream is handed back as-is).
    public async Task<Stream?> GetFileStreamAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = _incident.Files.FirstOrDefault(f => f.Id == fileId);
        if (file is null)
        {
            return null;
        }

        var cachePath = CachePathFor(fileId, file.FileName);
        if (cachePath is not null && File.Exists(cachePath))
        {
            return File.OpenRead(cachePath);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null; // host unreachable — degrade quietly, same as a missing local file
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        if (cachePath is null)
        {
            // Caching disabled — hand the live response stream straight to the caller rather than
            // buffering it anywhere.
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await using (var cacheStream = File.Create(cachePath))
        {
            await response.Content.CopyToAsync(cacheStream, cancellationToken);
        }

        return File.OpenRead(cachePath);
    }

    // One subfolder per incident, so a stale cache entry from a previously joined incident can
    // never collide with this one's file ids.
    private string? CachePathFor(Guid fileId, string fileName) => _cacheRoot is null
        ? null
        : Path.Combine(_cacheRoot, _incident.Id.ToString(), IncidentFile.StorageFileName(fileId, fileName));

    public void RenameFile(Guid fileId, string? displayName) => Send(new RenameFileCommand(fileId, displayName));

    public void AddCoBuilding(string name, int floorCount, int apartmentsPerFloor) =>
        Send(new AddCoBuildingCommand(Op(), name, floorCount, apartmentsPerFloor));

    public void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor) =>
        Send(new UpdateCoBuildingStructureCommand(Op(), buildingId, floorCount, apartmentsPerFloor));

    public void RemoveCoBuilding(Guid buildingId) =>
        Send(new RemoveCoBuildingCommand(Op(), buildingId));

    public void RecordCoValue(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue) =>
        Send(new RecordCoValueCommand(Op(), buildingId, floorOrdinal, apartmentNumber, coValue));

    public void SetDwellingStatus(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status) =>
        Send(new SetDwellingStatusCommand(Op(), buildingId, floorOrdinal, apartmentNumber, status));

    public void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, bool? keyAvailable) =>
        Send(new UpdateDwellingDetailsCommand(buildingId, floorOrdinal, apartmentNumber, residentName, keyAvailable));

    public void SetFloorDescription(Guid buildingId, int floorOrdinal, string? description) =>
        Send(new SetFloorDescriptionCommand(buildingId, floorOrdinal, description));

    public void SetApartmentLabel(Guid buildingId, int apartmentNumber, string? label) =>
        Send(new SetApartmentLabelCommand(buildingId, apartmentNumber, label));

    private OperatorDto Op() => new(Operator!.Name, Operator.CallSign);

    // Fire-and-forget: the command is POSTed; the host's broadcast (or a rejection the host swallows)
    // is what the UI ultimately reflects. Connection loss surfaces separately via Disconnected.
    private void Send(SyncCommand command) => _ = SendAsync(command);

    /// <summary>Sends one command to the host. The resulting state arrives via the broadcast, not this call.</summary>
    public async Task SendAsync(SyncCommand command, CancellationToken ct = default)
    {
        using var content = new StringContent(SyncJson.Serialize(command), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content, ct);
        response.EnsureSuccessStatusCode();
    }

    // Arrives on SignalR's receive loop. Swap the cached incident and raise Changed on the UI thread:
    // the subscribers (EtbViewModel.Sync et al.) mutate Avalonia-bound collections, which Avalonia
    // rejects off-thread — so a broadcast raised here would otherwise never reach the view.
    // Arrives on SignalR's receive loop. Swap the cached incident and raise Changed on the UI thread:
    // the subscribers (EtbViewModel.Sync et al.) mutate Avalonia-bound collections, which Avalonia
    // rejects off-thread — so a broadcast raised here would otherwise never reach the view.
    private void OnSnapshot(IncidentSnapshot snapshot) => _ui.Post(() =>
    {
        _incident = SnapshotMapper.FromSnapshot(snapshot);
        Changed?.Invoke();
    });

    private async Task ResyncAsync() =>
        OnSnapshot(SyncJson.Deserialize<IncidentSnapshot>(await _http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute))));

    // .NET wraps an exception thrown inside ServerCertificateCustomValidationCallback in an
    // HttpRequestException, keeping it as an inner cause rather than letting it propagate as-is; walk
    // the inner chain so the typed CertificateChangedException can be surfaced to the caller.
    private static TException? FindInner<TException>(Exception ex)
        where TException : Exception
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is TException t)
            {
                return t;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync();
        _http.Dispose();
    }

    // SignalR's default policy gives up after ~30s; on a callout a device's mobile data can blip for
    // longer than that, and dumping the user back to Home over a brief outage is worse than waiting.
    // Retry every few seconds for a couple of minutes, then give up (→ Closed → Ended → Home).
    private sealed class ReconnectForAWhile : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.ElapsedTime < TimeSpan.FromMinutes(2) ? TimeSpan.FromSeconds(3) : null;
    }
}
