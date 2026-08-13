using System.Net;
using System.Net.Sockets;
using System.Text;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.Sync.Hosting.Tests;

public class IncidentHostTests
{
    private sealed class InMemoryStore : IIncidentStore
    {
        private readonly Dictionary<string, Incident> _byPath = new();
        public void Save(string path, Incident incident) => _byPath[path] = incident;
        public Incident Load(string path) => _byPath[path];
        public IncidentState? TryReadState(string path) => _byPath.TryGetValue(path, out var i) ? i.State : null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<IncidentSnapshot> GetSnapshotAsync(HttpClient http) =>
        SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(SyncProtocol.SnapshotPath));

    [Fact]
    public async Task Host_serves_version_and_snapshot_and_applies_a_posted_command()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(new InMemoryStore(), clock,
            new SessionOperator("Host", "FFB 1"), "/x.fwincident", new[] { "Punkt A" });
        await using var host = new IncidentHost(session, clock, "1.2.3");
        var port = FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        // Version handshake.
        var version = SyncJson.Deserialize<VersionInfo>(await http.GetStringAsync(SyncProtocol.VersionPath));
        Assert.Equal("1.2.3", version.Version);

        // Initial snapshot reflects the hosted incident.
        var before = await GetSnapshotAsync(http);
        Assert.DoesNotContain(before.Journal, e => e.Text == "Von der Einsatzstelle");

        // A client posts a command; the host applies it with the client's operator and the host clock.
        var command = new AddJournalEntryCommand(new OperatorDto("Client", "RUF 1"),
            EtbDirection.Incoming, "Von der Einsatzstelle", "Leitstelle", "ELW");
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var response = await http.PostAsync(SyncProtocol.CommandPath, content);
        response.EnsureSuccessStatusCode();

        var after = await GetSnapshotAsync(http);
        var entry = Assert.Single(after.Journal, e => e.Text == "Von der Einsatzstelle");
        Assert.Equal("Client (RUF 1)", entry.EnteredBy); // attributed to the device, not the host
    }

    [Fact]
    public async Task Host_rejects_a_command_against_a_closed_incident_with_400()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(new InMemoryStore(), clock,
            new SessionOperator("Host", "FFB 1"), "/x.fwincident", Array.Empty<string>());
        session.Close();
        await using var host = new IncidentHost(session, clock, "1.0.0");
        var port = FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var command = new AddJournalEntryCommand(new OperatorDto("Client", null),
            EtbDirection.Internal, "zu spät", null, null);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");

        var response = await http.PostAsync(SyncProtocol.CommandPath, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
