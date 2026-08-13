using System.Text;
using System.Text.Json.Serialization;
using Feuerwehr.Domain;
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
public sealed class RemoteIncidentSession : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HubConnection _hub;
    private Incident _incident;

    public SessionOperator Operator { get; }
    public Incident Incident => _incident;

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
