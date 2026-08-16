using System.Net;
using System.Text;
using Feuerwehr.AppLogic;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Sync.Hosting.Tests;

public class IncidentHostTests
{
    private static async Task<IncidentSnapshot> GetSnapshotAsync(HttpClient http) =>
        SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(SyncProtocol.SnapshotPath));

    [Fact]
    public async Task Host_serves_version_and_snapshot_and_applies_a_posted_command()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(new InMemoryStore(), clock,
            new SessionOperator("Host", "FFB 1"), "/x.fwincident", new[] { "Punkt A" });
        await using var host = new IncidentHost(session, clock, "1.2.3", new ImmediateUiDispatcher());
        var port = TestHost.FreeTcpPort();
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
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher());
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var command = new AddJournalEntryCommand(new OperatorDto("Client", null),
            EtbDirection.Internal, "zu spät", null, null);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");

        var response = await http.PostAsync(SyncProtocol.CommandPath, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
