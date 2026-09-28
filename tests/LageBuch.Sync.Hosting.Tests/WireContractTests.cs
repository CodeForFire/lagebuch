using System.Globalization;
using System.Net;
using System.Text;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The part of the wire contract a joined client relies on, asserted identically against the real
/// <see cref="IncidentHost"/> and against <see cref="ScriptedSnapshotHost"/>.
/// <para>
/// The scripted host exists because the real one cannot be made to lose or delay a broadcast, and a
/// client test is only as good as its stand-in is faithful. Running one set of assertions over both
/// is what stops the stand-in drifting from the real thing unnoticed — which it did once already,
/// serving <c>/revision</c> without the epoch.
/// </para>
/// </summary>
public class WireContractTests
{
    public static TheoryData<string> Hosts => new() { "real", "scripted" };

    private static async Task<ContractHost> StartAsync(string kind)
    {
        if (kind == "real")
        {
            var clock = new FixedClock();
            var session = TestSession.StartNew(
                new InMemoryStore(),
                clock,
                new SessionOperator("Host", "FFB 1"),
                "/x.fwincident",
                Array.Empty<(string, bool)>(),
                Array.Empty<(string, bool)>());
            var (host, port) = await TestHost.StartAsync(session, clock);
            return new ContractHost(Client(port), session.Close, host);
        }

        var scripted = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.BaseSnapshot());
        return new ContractHost(Client(scripted.Port), () => scripted.RejectCommandsWith = "Abgelehnt.", scripted);
    }

    private static HttpClient Client(int port)
    {
        var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, TestHost.DefaultPin);
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        return http;
    }

    private static async Task<T> GetAsync<T>(HttpClient http, string path) =>
        SyncJson.Deserialize<T>(await http.GetStringAsync(new Uri(path, UriKind.RelativeOrAbsolute)));

    private static async Task<HttpResponseMessage> PostCommandAsync(HttpClient http)
    {
        using var content = new StringContent(
            SyncJson.Serialize<SyncCommand>(new AddJournalEntryCommand(
                new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Meldung", null, null)),
            Encoding.UTF8,
            "application/json");
        return await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task Version_advertises_this_builds_protocol_range(string kind)
    {
        await using var host = await StartAsync(kind);

        var version = await GetAsync<VersionInfo>(host.Http, SyncProtocol.VersionPath);

        Assert.Equal(SyncProtocol.ProtocolVersion, version.Protocol);
        Assert.Equal(SyncProtocol.MinimumProtocolVersion, version.MinProtocol);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task Revision_and_snapshot_name_the_same_position(string kind)
    {
        await using var host = await StartAsync(kind);

        var position = await GetAsync<RevisionInfo>(host.Http, SyncProtocol.RevisionPath);
        var snapshot = await GetAsync<IncidentSnapshot>(host.Http, SyncProtocol.SnapshotPath);

        Assert.NotEqual(Guid.Empty, position.Epoch);
        Assert.Equal(position.Epoch, snapshot.Epoch);
        Assert.Equal(position.Revision, snapshot.Revision);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_command_is_answered_with_the_snapshot_at_the_hosts_position(string kind)
    {
        await using var host = await StartAsync(kind);

        using var response = await PostCommandAsync(host.Http);
        response.EnsureSuccessStatusCode();
        var answered = SyncJson.Deserialize<IncidentSnapshot>(await response.Content.ReadAsStringAsync());
        var position = await GetAsync<RevisionInfo>(host.Http, SyncProtocol.RevisionPath);

        Assert.Equal(position.Epoch, answered.Epoch);
        Assert.Equal(position.Revision, answered.Revision);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_refused_command_is_answered_400_with_its_reason_as_a_json_string(string kind)
    {
        await using var host = await StartAsync(kind);
        host.RefuseCommands();

        using var response = await PostCommandAsync(host.Http);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(SyncJson.Deserialize<string>(body)));
    }

    private sealed class ContractHost(HttpClient http, Action refuseCommands, IAsyncDisposable owner) : IAsyncDisposable
    {
        public HttpClient Http { get; } = http;

        /// <summary>Makes every following command a refusal: a closed Einsatz, or the scripted reason.</summary>
        public void RefuseCommands() => refuseCommands();

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await owner.DisposeAsync();
        }
    }
}
