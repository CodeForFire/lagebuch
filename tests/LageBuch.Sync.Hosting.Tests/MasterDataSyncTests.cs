using System.Net;
using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The host is the Stammdaten master (#183): it serves its own set at /masterdata behind the same
/// PIN gate as every other route, and a joining client picks that up instead of its local one.
/// </summary>
public class MasterDataSyncTests
{
    private static LocalIncidentSession HostSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    /// <summary>A recognisable set: the brigade names the source device, the Rückzugsdruck is the
    /// safety-relevant setting a joined client must not diverge on.</summary>
    internal static MasterDataSet SetWith(string brigade, int returnPressureBar) =>
        MasterDataSet.Empty with
        {
            Brigades = new[] { brigade },
            Vehicles = new[] { new Vehicle(brigade, "FFB 1/40/1", 9) },
            Settings = IncidentSettings.Defaults with { ReturnPressureBar = returnPressureBar },
        };

    private static HttpClient Client(int port, string pin)
    {
        var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        return http;
    }

    [Fact]
    public async Task Host_serves_its_master_data()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, masterData: SetWith("Löschzug Fürstenfeldbruck", 60));
        await using var _ = host;

        using var http = Client(port, TestHost.DefaultPin);
        var set = MasterDataJson.Parse(
            await http.GetStringAsync(new Uri(SyncProtocol.MasterDataPath, UriKind.RelativeOrAbsolute)));

        Assert.Equal(new[] { "Löschzug Fürstenfeldbruck" }, set.Brigades);
        Assert.Equal(new Vehicle("Löschzug Fürstenfeldbruck", "FFB 1/40/1", 9), Assert.Single(set.Vehicles));
        Assert.Equal(60, set.Settings.ReturnPressureBar);
    }

    [Fact]
    public async Task Master_data_endpoint_rejects_a_wrong_pin()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, masterData: SetWith("Löschzug Fürstenfeldbruck", 60));
        await using var _ = host;

        using var http = Client(port, "9999");
        var response = await http.GetAsync(new Uri(SyncProtocol.MasterDataPath, UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Host_without_master_data_serves_an_empty_set()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock);
        await using var _ = host;

        using var http = Client(port, TestHost.DefaultPin);
        var set = MasterDataJson.Parse(
            await http.GetStringAsync(new Uri(SyncProtocol.MasterDataPath, UriKind.RelativeOrAbsolute)));

        Assert.True(set.IsEmpty);

        // Settings always ride along, so even an unconfigured host and its clients agree on
        // Einsatzzeiten and Rückzugsdruck — the part that actually matters operationally.
        Assert.Equal(IncidentSettings.Defaults, set.Settings);
    }

    [Fact]
    public async Task Connected_client_exposes_the_hosts_master_data()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, masterData: SetWith("Löschzug Fürstenfeldbruck", 60));
        await using var _ = host;

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            TestHost.DefaultPin,
            port);

        var set = MasterDataJson.Parse(client.HostMasterDataJson);

        Assert.Equal(new[] { "Löschzug Fürstenfeldbruck" }, set.Brigades);
        Assert.Equal(60, set.Settings.ReturnPressureBar);
    }
}
