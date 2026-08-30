using System.Net;
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
    public Incident Incident => _incident;

    // The client never writes locally, so it is never "read-only" in the editing sense — but a
    // closed incident rejects mutations at the host anyway, and the workspace uses this to grey out.
    public bool IsReadOnly => _incident.State == IncidentState.Closed;

    // This is the joined-client side: autonomous time-driven logging belongs to the host (§ IsRemote).
    public bool IsRemote => true;

    /// <summary>Raised after the cached incident is replaced by a host broadcast (or a resync).</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when the connection drops but automatic reconnect is still trying — the UI should
    /// disable input and show "Verbindung getrennt — verbinde neu…". A successful retry raises
    /// <see cref="Reconnected"/>; giving up raises <see cref="Ended"/>.
    /// </summary>
    public event Action? Disconnected;

    /// <summary>Raised after a reconnect + full resync — the UI can re-enable input.</summary>
    public event Action? Reconnected;

    /// <summary>
    /// Raised when the connection is gone for good — reconnect attempts were exhausted or the host
    /// stopped sharing. The UI returns to Home (§7); nothing further arrives on this session.
    /// </summary>
    public event Action? Ended;

    private RemoteIncidentSession(HttpClient http, HubConnection hub, IUiDispatcher ui, SessionOperator op, Incident initial, string? cacheRoot)
    {
        _http = http;
        _hub = hub;
        _ui = ui;
        Operator = op;
        _incident = initial;
        _cacheRoot = cacheRoot;
    }

    /// <summary>
    /// Version-handshakes, fetches the initial snapshot, and opens the push channel. Throws
    /// <see cref="PinRejectedException"/> when the host refuses the share PIN,
    /// <see cref="VersionMismatchException"/> on a version mismatch, and
    /// <see cref="HttpRequestException"/> when the host isn't sharing / is unreachable.
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
    /// <see cref="GetFileBytesAsync"/>). This project has no platform path knowledge, so callers
    /// supply it (a folder under the app's data/cache dir). Null disables caching — bytes are
    /// re-fetched from the host on every call, which is correct, just not free.
    /// </param>
    /// <param name="ct">Cancels the connect handshake.</param>
    public static async Task<RemoteIncidentSession> ConnectAsync(
        string host, SessionOperator op, string localVersion, IUiDispatcher ui, string? pin = null,
        int port = SyncProtocol.Port, IRetryPolicy? reconnectPolicy = null, string? cacheRoot = null,
        CancellationToken ct = default)
    {
        var baseUri = new Uri($"http://{host}:{port}");
        var http = new HttpClient { BaseAddress = baseUri };
        if (!string.IsNullOrEmpty(pin))
            http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        try
        {
            // The PIN gates every endpoint, so the first request already reflects it: a 401 means the
            // PIN is wrong/missing — reported as such before the version compare (auth precedes content).
            var versionResponse = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute), ct);
            if (versionResponse.StatusCode == HttpStatusCode.Unauthorized)
                throw new PinRejectedException();
            versionResponse.EnsureSuccessStatusCode();

            var hostVersion = SyncJson.Deserialize<VersionInfo>(await versionResponse.Content.ReadAsStringAsync(ct)).Version;
            if (hostVersion != localVersion)
                throw new VersionMismatchException(localVersion, hostVersion);

            var initial = SnapshotMapper.FromSnapshot(
                SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute), ct)));

            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(baseUri, SyncProtocol.HubPath), o =>
                {
                    if (!string.IsNullOrEmpty(pin))
                        o.Headers.Add(SyncProtocol.PinHeader, pin);
                })
                .WithAutomaticReconnect(reconnectPolicy ?? new ReconnectForAWhile())
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                .Build();

            var session = new RemoteIncidentSession(http, hub, ui, op, initial, cacheRoot);
            hub.On<IncidentSnapshot>(SyncProtocol.SnapshotMethod, session.OnSnapshot);
            // Every SignalR callback below arrives on the hub's receive loop, off the UI thread; each is
            // marshalled onto the UI thread because it drives view state (the reconnect banner, the
            // return-Home navigation) exactly as OnSnapshot drives the journal.
            // Reconnecting = transient drop (keep the workspace open, disable input); Closed = the
            // reconnect window ran out or the host went away for good (return to Home).
            hub.Reconnecting += _ => { session._ui.Post(() => session.Disconnected?.Invoke()); return Task.CompletedTask; };
            hub.Reconnected += async _ => { await session.ResyncAsync(); session._ui.Post(() => session.Reconnected?.Invoke()); };
            hub.Closed += _ => { session._ui.Post(() => session.Ended?.Invoke()); return Task.CompletedTask; };
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

    public void AssignRole(string role, string personName, string? callSign = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? section = null, string? phone = null) =>
        Send(new AssignRoleCommand(Op(), role, personName, callSign, from, to, section, phone));

    public void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null) =>
        Send(new TransferRoleCommand(Op(), assignmentId, newPersonName, newCallSign, newPhone));

    public void EditRolePhone(Guid assignmentId, string? phone) =>
        Send(new EditRolePhoneCommand(Op(), assignmentId, phone));

    public void AddForceUnit(string brigade, int personnelCount, string? callSign = null,
        string? status = null, string? notes = null, int scbaCount = 0, int officerCount = 0) =>
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

    public void AddScbaTrupp(string designation, IEnumerable<TruppMember> members, int entryPressure,
        int? truppNumber = null,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes) =>
        Send(new AddScbaTruppCommand(designation,
            members.Select(m => new TruppMemberDto(m.Role, m.Name)).ToList(),
            callSign, task, maxDurationMinutes, returnPressureBar, pressureControlIntervalMinutes,
            entryPressure, truppNumber));

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
    public void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning) { }

    public void Close() => Send(new CloseIncidentCommand(Op()));

    // Unlike every other mutation, this is a real upload — genuinely awaited (per IIncidentSession's
    // doc comment) rather than fire-and-forget, so the caller can show a spinner and catch a
    // rejection (over the size cap, unsupported type, closed incident, or a network failure here).
    public async Task AddFileAsync(string fileName, string contentType, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength > IncidentFile.MaxSizeBytes)
            throw new ArgumentException(
                $"Datei ist größer als das Limit von {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB.", nameof(bytes));
        await SendAsync(new AddFileCommand(Op(), fileName, contentType, bytes));
    }

    // On-demand pull (§5): the cached incident carries only file metadata (from the snapshot), so
    // bytes are fetched from the host the first time they're needed and cached locally afterwards —
    // mirroring how a join fetches GET /snapshot once rather than having it pushed continuously.
    public async Task<byte[]?> GetFileBytesAsync(Guid fileId)
    {
        var file = _incident.Files.FirstOrDefault(f => f.Id == fileId);
        if (file is null)
            return null;

        var cachePath = CachePathFor(fileId, file.FileName);
        if (cachePath is not null && File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute));
        }
        catch (HttpRequestException)
        {
            return null; // host unreachable — degrade quietly, same as a missing local file
        }
        if (!response.IsSuccessStatusCode)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (cachePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(cachePath, bytes);
        }
        return bytes;
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
