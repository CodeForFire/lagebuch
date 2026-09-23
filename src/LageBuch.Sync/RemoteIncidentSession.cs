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
    /// <summary>
    /// Default cap for the attachment cache (issue #167 P2: the disk cache in
    /// <see cref="GetFileBytesAsync"/> had no eviction at all). 500 MB comfortably covers a normal
    /// incident's attachments while bounding what a device that has joined many incidents over
    /// time accumulates on disk.
    /// </summary>
    public const long DefaultCacheMaxBytes = 500L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private readonly HubConnection _hub;
    private readonly IUiDispatcher _ui;
    private readonly string? _cacheRoot;
    private readonly long _cacheMaxBytes;
    private readonly object _cacheEvictionGate = new();

    // Guards the (_incident, _lastRevision) pair, which must move together: the guard below is a
    // read-modify-write, so Interlocked on the field alone would not do. It is contended for real,
    // not defensively — ImmediateUiDispatcher.Post runs inline, so under it "the UI thread" is
    // whichever thread called, and SignalR's receive loop, a command's response and the reconcile
    // poll genuinely race here. (A long is not atomic on the 32-bit Android head either.)
    private readonly object _applyGate = new();
    private Incident _incident;
    private long _lastRevision;

    public SessionOperator? Operator { get; private set; }

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
        HttpClientHandler handler,
        HubConnection hub,
        IUiDispatcher ui,
        SessionOperator op,
        Incident initial,
        long initialRevision,
        string? cacheRoot,
        long cacheMaxBytes,
        string hostMasterDataJson)
    {
        _http = http;
        _handler = handler;
        _hub = hub;
        _ui = ui;
        Operator = op;
        _incident = initial;
        _lastRevision = initialRevision;
        _cacheRoot = cacheRoot;
        _cacheMaxBytes = cacheMaxBytes;
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
    /// <param name="trustStore">
    /// Store of trusted TLS thumbprints, keyed by host address, driving Trust-on-First-Use: on first
    /// contact the presented certificate's thumbprint is saved and accepted; on a later connect the
    /// host's certificate is accepted only if its thumbprint still matches the saved one, otherwise
    /// <see cref="CertificateChangedException"/> is thrown. Required — every caller must supply one
    /// (both app heads always do via <c>JsonTrustStore</c>); there is no "accept any certificate"
    /// fallback, so a joined session is never unpinned.
    /// </param>
    /// <param name="pin">The host's share PIN, if it requires one.</param>
    /// <param name="port">The host's port — <see cref="SyncProtocol.Port"/> unless overridden (tests).</param>
    /// <param name="reconnectPolicy">Overrides the default reconnect policy (tests only).</param>
    /// <param name="cacheRoot">
    /// Folder to cache pulled attachment bytes in, keyed by incident and file id (see
    /// <see cref="GetFileBytesAsync"/>). This project has no platform path knowledge, so callers
    /// supply it (a folder under the app's data/cache dir). Null disables caching — bytes are
    /// re-fetched from the host on every call, which is correct, just not free.
    /// </param>
    /// <param name="cacheMaxBytes">
    /// Size cap for <paramref name="cacheRoot"/> across every incident cached there; once a newly
    /// cached file pushes the total over this, the oldest files (by last-write time) are deleted
    /// until it is back under. Defaults to <see cref="DefaultCacheMaxBytes"/>; irrelevant when
    /// <paramref name="cacheRoot"/> is null.
    /// </param>
    /// <param name="ct">Cancels the connect handshake.</param>
    public static async Task<RemoteIncidentSession> ConnectAsync(
        string host,
        SessionOperator op,
        string localVersion,
        IUiDispatcher ui,
        ITrustStore trustStore,
        string? pin = null,
        int port = SyncProtocol.Port,
        IRetryPolicy? reconnectPolicy = null,
        string? cacheRoot = null,
        long cacheMaxBytes = DefaultCacheMaxBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trustStore);

        var baseUri = new Uri($"https://{host}:{port}");

        // A single handler backs both the HttpClient and the SignalR hub connection, so they agree on
        // TLS validation: pin the presented cert via Trust-on-First-Use. A certificate that differs
        // from the previously-trusted one throws CertificateChangedException from inside the callback;
        // the connect await surfaces it (§ P0 #2).
        var handler = new HttpClientHandler { CheckCertificateRevocationList = true };
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

        // disposeHandler: false — this class owns the handler explicitly (DisposeAsync disposes it
        // after the hub, since the hub's long-lived transport also uses it via HttpMessageHandlerFactory
        // below) rather than relying on HttpClient's default cascade, so ownership is one clear line
        // instead of implicit via a constructor flag.
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = baseUri };
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

            // Kept as the snapshot, not just the mapped Incident: its revision seeds _lastRevision, so
            // the client starts level with the host instead of treating everything up to the joined-at
            // revision as new.
            var initialSnapshot = SyncJson.Deserialize<IncidentSnapshot>(
                await http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute), ct));
            var initial = SnapshotMapper.FromSnapshot(initialSnapshot);

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

            var session = new RemoteIncidentSession(
                http, handler, hub, ui, op, initial, initialSnapshot.Revision, cacheRoot, cacheMaxBytes, hostMasterDataJson);
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
                await session.ResyncAsync(SnapshotOrigin.Rebaseline);
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
            handler.Dispose();
            throw;
        }
    }

    // --- IIncidentSession mutation surface: every call is a fire-and-forget command to the host;
    //     the resulting state arrives via the broadcast, never from these calls. ---
    // The new operator is only a client-side fact: each later command carries it through Op(), so
    // the host learns nothing beyond the logged handover itself (#469).
    public void ChangeOperator(SessionOperator op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (Operator == op)
        {
            return;
        }

        Send(new ChangeOperatorCommand(Op(), new OperatorDto(op.Name, op.CallSign)));
        Operator = op;
    }

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
        int officerCount = 0,
        int zugfuehrerCount = 0) =>
        Send(new AddForceUnitCommand(Op(), brigade, personnelCount, callSign, status, notes, scbaCount, officerCount, zugfuehrerCount));

    public void UpdateForceUnit(Guid unitId, string? status, string? notes) =>
        Send(new UpdateForceUnitCommand(Op(), unitId, status, notes));

    public void UpdateForceStrength(Guid unitId, int officerCount, int personnelCount, int scbaCount, int zugfuehrerCount = 0) =>
        Send(new UpdateForceStrengthCommand(Op(), unitId, officerCount, personnelCount, scbaCount, zugfuehrerCount));

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

    public void SetScbaSafetyTrupp(Guid truppId, Guid? safetyTruppId) =>
        Send(new SetScbaSafetyTruppCommand(truppId, safetyTruppId));

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
    public async Task AddFileAsync(string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength > IncidentFile.MaxSizeBytes)
        {
            throw new ArgumentException(
                $"Datei ist größer als das Limit von {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB.", nameof(bytes));
        }

        var fileId = Guid.NewGuid();
        await SendAsync(new AddFileCommand(Op(), fileId, fileName, contentType, bytes.LongLength), cancellationToken);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await _http.PutAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // On-demand pull (§5): the cached incident carries only file metadata (from the snapshot), so
    // bytes are fetched from the host the first time they're needed and cached locally afterwards —
    // mirroring how a join fetches GET /snapshot once rather than having it pushed continuously.
    public async Task<byte[]?> GetFileBytesAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = _incident.Files.FirstOrDefault(f => f.Id == fileId);
        if (file is null)
        {
            return null;
        }

        var cachePath = CachePathFor(fileId, file.FileName);
        if (cachePath is not null && File.Exists(cachePath))
        {
            try
            {
                return await File.ReadAllBytesAsync(cachePath, cancellationToken);
            }
            catch (IOException)
            {
                // An eviction pass can delete this file between the Exists check and the read; fall
                // through and re-fetch from the host rather than failing the whole call.
            }
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

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (cachePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
            EvictOldestCacheEntriesOverCap();
        }

        return bytes;
    }

    // One subfolder per incident, so a stale cache entry from a previously joined incident can
    // never collide with this one's file ids.
    private string? CachePathFor(Guid fileId, string fileName) => _cacheRoot is null
        ? null
        : Path.Join(_cacheRoot, _incident.Id.ToString(), IncidentFile.StorageFileName(fileId, fileName));

    /// <summary>
    /// Trims the cache back under <see cref="_cacheMaxBytes"/>, oldest by last-write time first.
    /// <para>
    /// Scoped to the whole cache root, not just this incident's subfolder: the unbounded growth is
    /// a device accumulating the attachments of every incident it has ever joined, and nothing
    /// deletes a previous incident's folder when that session ends. Last-write rather than
    /// last-read, because reads do not touch mtime — so this is "oldest written", which for a cache
    /// that is written once and then read is the same thing often enough to be worth the simplicity.
    /// </para>
    /// </summary>
    private void EvictOldestCacheEntriesOverCap()
    {
        if (_cacheRoot is null || !Directory.Exists(_cacheRoot))
        {
            return;
        }

        // Serialize eviction passes: two concurrent downloads would otherwise each enumerate and
        // sum the same tree before either deleted anything, and both would then delete against a
        // total that was already stale.
        lock (_cacheEvictionGate)
        {
            var files = new DirectoryInfo(_cacheRoot)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            var totalBytes = files.Sum(f => f.Length);

            foreach (var file in files)
            {
                if (totalBytes <= _cacheMaxBytes)
                {
                    break;
                }

                var reclaimed = file.Length;
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort: a file another handle is using right now, or one that is
                    // read-only, simply stays -- and stays counted, so the pass carries on to the
                    // entries it can actually reclaim instead of stopping on a total that only
                    // looks like it is under the cap. The next pass gets another chance at it.
                    continue;
                }

                totalBytes -= reclaimed;
            }
        }
    }

    public void RenameFile(Guid fileId, string? displayName) => Send(new RenameFileCommand(fileId, displayName));

    // Genuinely awaited, like AddFileAsync — not the fire-and-forget Send() used elsewhere in this
    // file — so the caller can catch a rejection (closed incident, unknown id) and surface it.
    public Task RemoveFileAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        SendAsync(new RemoveFileCommand(Op(), fileId), cancellationToken);

    public void AddCoBuilding(string name, int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0) =>
        Send(new AddCoBuildingCommand(Op(), name, floorCount, apartmentsPerFloor, undergroundFloorCount));

    public void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0) =>
        Send(new UpdateCoBuildingStructureCommand(Op(), buildingId, floorCount, apartmentsPerFloor, undergroundFloorCount));

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

    public void SetApartmentLabel(Guid buildingId, int floorOrdinal, int apartmentNumber, string? label) =>
        Send(new SetApartmentLabelCommand(buildingId, floorOrdinal, apartmentNumber, label));

    public void SetApartmentCount(Guid buildingId, int floorOrdinal, int count) =>
        Send(new SetApartmentCountCommand(Op(), buildingId, floorOrdinal, count));

    public void RemoveDwellings(Guid buildingId, int floorOrdinal, IReadOnlyList<int> apartmentNumbers) =>
        Send(new RemoveDwellingsCommand(Op(), buildingId, floorOrdinal, apartmentNumbers));

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

    /// <summary>Where a snapshot came from, which decides whether the revision guard applies.</summary>
    private enum SnapshotOrigin
    {
        /// <summary>
        /// A hub broadcast, or the body a command's own round trip returned. Newer-only: a copy of a
        /// revision already applied is dropped, which is what makes two broadcasts racing each other —
        /// or a broadcast and a command response carrying the same revision — harmless (#295).
        /// </summary>
        Push,

        /// <summary>
        /// A full re-fetch taken because the host's revision disagreed with ours, and it turned out to
        /// be <em>lower</em>: the host restarted sharing and is counting from zero again. Accepted
        /// as-is, because newer-only would otherwise ignore that host forever.
        /// </summary>
        Rebaseline,
    }

    // Arrives on SignalR's receive loop. Swap the cached incident and raise Changed on the UI thread:
    // the subscribers (EtbViewModel.Sync et al.) mutate Avalonia-bound collections, which Avalonia
    // rejects off-thread — so a broadcast raised here would otherwise never reach the view.
    private void OnSnapshot(IncidentSnapshot snapshot) => ApplySnapshot(snapshot, SnapshotOrigin.Push);

    private void ApplySnapshot(IncidentSnapshot snapshot, SnapshotOrigin origin) => _ui.Post(() =>
    {
        lock (_applyGate)
        {
            if (origin == SnapshotOrigin.Push && snapshot.Revision <= _lastRevision)
            {
                return;
            }

            _incident = SnapshotMapper.FromSnapshot(snapshot);
            _lastRevision = snapshot.Revision;
        }

        // Outside the lock: subscribers re-enter this object and touch the UI, and holding a lock
        // across that is how a deadlock gets built.
        Changed?.Invoke();
    });

    private async Task ResyncAsync(SnapshotOrigin origin, CancellationToken ct = default) =>
        ApplySnapshot(
            SyncJson.Deserialize<IncidentSnapshot>(
                await _http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute), ct)),
            origin);

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
        // The hub's transport uses _handler (via HttpMessageHandlerFactory) for as long as it's
        // running, so it must be torn down first; only then are the HttpClient and the handler it
        // doesn't own (disposeHandler: false, above) both disposed here explicitly.
        await _hub.DisposeAsync();
        _http.Dispose();
        _handler.Dispose();
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
