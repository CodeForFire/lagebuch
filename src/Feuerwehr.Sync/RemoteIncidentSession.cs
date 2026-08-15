using System.Text;
using System.Text.Json.Serialization;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.ValueObjects;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Feuerwehr.Sync;

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

    /// <summary>Raised when the connection drops — the UI should disable input and show "verbinde neu…".</summary>
    public event Action? Disconnected;

    /// <summary>Raised after a reconnect + full resync — the UI can re-enable input.</summary>
    public event Action? Reconnected;

    private RemoteIncidentSession(HttpClient http, HubConnection hub, SessionOperator op, Incident initial)
    {
        _http = http;
        _hub = hub;
        Operator = op;
        _incident = initial;
    }

    /// <summary>
    /// Version-handshakes, fetches the initial snapshot, and opens the push channel. Throws
    /// <see cref="VersionMismatchException"/> on a version mismatch and
    /// <see cref="HttpRequestException"/> when the host isn't sharing / is unreachable.
    /// </summary>
    public static async Task<RemoteIncidentSession> ConnectAsync(
        string host, SessionOperator op, string localVersion, int port = SyncProtocol.Port, CancellationToken ct = default)
    {
        var baseUri = new Uri($"http://{host}:{port}");
        var http = new HttpClient { BaseAddress = baseUri };
        try
        {
            var hostVersion = SyncJson.Deserialize<VersionInfo>(await http.GetStringAsync(SyncProtocol.VersionPath, ct)).Version;
            if (hostVersion != localVersion)
                throw new VersionMismatchException(localVersion, hostVersion);

            var initial = SnapshotMapper.FromSnapshot(
                SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(SyncProtocol.SnapshotPath, ct)));

            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(baseUri, SyncProtocol.HubPath))
                .WithAutomaticReconnect()
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                .Build();

            var session = new RemoteIncidentSession(http, hub, op, initial);
            hub.On<IncidentSnapshot>(SyncProtocol.SnapshotMethod, session.OnSnapshot);
            hub.Closed += _ => { session.Disconnected?.Invoke(); return Task.CompletedTask; };
            hub.Reconnected += async _ => { await session.ResyncAsync(); session.Reconnected?.Invoke(); };
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

    public void ToggleChecklistItem(Guid itemId) => Send(new ToggleChecklistItemCommand(itemId));

    public void AssignRole(string role, string personName, string? callSign = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? section = null, string? phone = null) =>
        Send(new AssignRoleCommand(role, personName, callSign, from, to, section, phone));

    public void EndRoleAssignment(Guid assignmentId) => Send(new EndRoleAssignmentCommand(assignmentId));

    public void AddForceUnit(string brigade, int personnelCount, string? callSign = null,
        string? status = null, string? notes = null, int scbaCount = 0) =>
        Send(new AddForceUnitCommand(Op(), brigade, personnelCount, callSign, status, notes, scbaCount));

    public void UpdateForceUnit(Guid unitId, string? status, string? notes) =>
        Send(new UpdateForceUnitCommand(Op(), unitId, status, notes));

    public void AddScbaTrupp(string designation, IEnumerable<TruppMember> members, string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes) =>
        Send(new AddScbaTruppCommand(designation,
            members.Select(m => new TruppMemberDto(m.Role, m.Name)).ToList(),
            callSign, task, maxDurationMinutes, returnPressureBar, pressureControlIntervalMinutes));

    public void StartScbaTrupp(Guid truppId, int startPressure) => Send(new StartScbaTruppCommand(truppId, startPressure));
    public void RecordScbaPressure(Guid truppId, int bar) => Send(new RecordScbaPressureCommand(truppId, bar));
    public void MarkScbaReturned(Guid truppId) => Send(new MarkScbaReturnedCommand(truppId));
    public void SetIncidentNumber(IncidentNumber? number) => Send(new SetIncidentNumberCommand(number?.Value));
    public void SetKeyword(string? keyword) => Send(new SetKeywordCommand(keyword));
    public void SetAddress(string? street, string? district) => Send(new SetAddressCommand(street, district));
    public void SetStatus(string? status) => Send(new SetStatusCommand(status));
    public void Close() => Send(new CloseIncidentCommand(Op()));

    private OperatorDto Op() => new(Operator!.Name, Operator.CallSign);

    // Fire-and-forget: the command is POSTed; the host's broadcast (or a rejection the host swallows)
    // is what the UI ultimately reflects. Connection loss surfaces separately via Disconnected.
    private void Send(SyncCommand command) => _ = SendAsync(command);

    /// <summary>Sends one command to the host. The resulting state arrives via the broadcast, not this call.</summary>
    public async Task SendAsync(SyncCommand command, CancellationToken ct = default)
    {
        using var content = new StringContent(SyncJson.Serialize(command), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(SyncProtocol.CommandPath, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private void OnSnapshot(IncidentSnapshot snapshot)
    {
        _incident = SnapshotMapper.FromSnapshot(snapshot);
        Changed?.Invoke();
    }

    private async Task ResyncAsync() =>
        OnSnapshot(SyncJson.Deserialize<IncidentSnapshot>(await _http.GetStringAsync(SyncProtocol.SnapshotPath)));

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync();
        _http.Dispose();
    }
}
