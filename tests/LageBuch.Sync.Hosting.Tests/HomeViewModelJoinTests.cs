using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The "Mit Gerät verbinden" join flow at the ViewModel layer (#52 §6/§7): a successful join opens a
/// thin-client workspace, and the expected failures — a version mismatch, a wrong PIN, an
/// unreachable / not-sharing host, and a changed TLS certificate — surface as a Home banner without
/// throwing.
/// </summary>
public class HomeViewModelJoinTests
{
    private static HomeViewModel Home(ITrustStore? trust = null, IMasterDataProvider? masterData = null) =>
        new(
            new InMemoryStore(),
            masterData ?? new EmptyMasterData(),
            new NoRecentFiles(),
            new NoDialogs(),
            new FixedClock(),
            new NoTicker(),
            new NoAlarm(),
            new NoopIncidentHostController(),
            "1.0.0",
            trustStore: trust);

    private static LocalIncidentSession HostSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            new[] { ("Punkt A", false) },
            Array.Empty<(string, bool)>());

    [Fact]
    public async Task Successful_join_opens_a_thin_client_workspace()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var vm = Home();
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.Null(vm.JoinError);
        Assert.NotNull(opened);
        Assert.False(opened!.CanExport);           // a client can't export the host's file
        Assert.False(opened.CanContinueEditing);   // nor resume a local file it doesn't own
        await opened.LeaveAsync();
    }

    [Fact]
    public async Task Joined_workspace_uses_the_hosts_master_data_not_the_local_one()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(
            HostSession(clock), clock, "1.0.0", masterData: MasterDataSyncTests.SetWith("Host-Wache", 60));
        await using var _ = host;

        // This device's own Stammdaten says something different, including a different Rückzugsdruck.
        var local = new FixedMasterData(MasterDataSyncTests.SetWith("Client-Wache", 50));
        var vm = Home(masterData: local);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.Null(vm.JoinError);
        Assert.NotNull(opened);

        // Pickers come from the host.
        Assert.Contains("Host-Wache", opened!.Forces.BrigadeOptions);
        Assert.DoesNotContain("Client-Wache", opened.Forces.BrigadeOptions);

        // And so does the safety-relevant Atemschutz setting: a Trupp registered from this client
        // is created with the host's Rückzugsdruck, not this device's (#183).
        Assert.Equal(60, opened.Scba.NewReturnPressureBar);

        await opened.LeaveAsync();
    }

    [Fact]
    public async Task Joining_does_not_touch_the_local_master_data()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(
            HostSession(clock), clock, "1.0.0", masterData: MasterDataSyncTests.SetWith("Host-Wache", 60));
        await using var _ = host;

        var local = new FixedMasterData(MasterDataSyncTests.SetWith("Client-Wache", 50));
        var vm = Home(masterData: local);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        // Session-scoped adoption: nothing is written back, so leaving the workspace is the whole
        // restore path and this device's own Stammdaten cannot be lost.
        Assert.False(local.SaveCalled);
        Assert.Equal(new[] { "Client-Wache" }, local.Get().Brigades);

        await opened!.LeaveAsync();
    }

    [Fact]
    public async Task Successful_first_join_trusts_and_caches_the_host_certificate()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var trust = new InMemoryTrustStore();
        var vm = Home(trust);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.Null(vm.JoinError);
        Assert.NotNull(opened);
        Assert.Single(trust.Thumbprints); // TOFU: first use recorded the host's cert
        await opened!.LeaveAsync();
    }

    [Fact]
    public async Task A_cert_that_differs_from_the_trusted_thumbprint_shows_a_banner()
    {
        var trust = new InMemoryTrustStore();
        trust.SaveThumbprint("127.0.0.1", "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");

        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var vm = Home(trust);
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.False(opened);
        Assert.NotNull(vm.JoinError);
        Assert.Contains("geändert", vm.JoinError, StringComparison.Ordinal); // the cert-changed message
        Assert.True(vm.CanResetTrustedCertificate); // #181: the banner alone left nobody a way out
    }

    [Fact]
    public async Task Resetting_trust_after_a_cert_change_clears_the_banner_and_lets_a_retry_join()
    {
        var trust = new InMemoryTrustStore();
        trust.SaveThumbprint("127.0.0.1", "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");

        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var vm = Home(trust);
        vm.WorkspaceOpened = _ => { };
        var request = new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin);
        await vm.JoinDeviceCommand.ExecuteAsync(request);
        Assert.NotNull(vm.JoinError); // stale thumbprint from the setup above -- the actual host cert differs

        vm.ResetTrustedCertificateCommand.Execute(null);

        Assert.Null(vm.JoinError);
        Assert.False(vm.CanResetTrustedCertificate);
        Assert.Null(trust.GetThumbprint("127.0.0.1")); // the stale pin is gone, so the retry below can re-pin it

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;
        await vm.JoinDeviceCommand.ExecuteAsync(request);

        Assert.Null(vm.JoinError);
        Assert.NotNull(opened);
        await opened!.LeaveAsync();
    }

    [Fact]
    public async Task Non_certificate_failures_do_not_offer_a_trust_reset()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        var vm = Home(new InMemoryTrustStore());
        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", "9999"));

        Assert.NotNull(vm.JoinError);
        Assert.False(vm.CanResetTrustedCertificate);
    }

    [Fact]
    public async Task Version_mismatch_shows_a_banner_and_opens_nothing()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "2.0.0"); // host newer
        await using var _ = host;

        var vm = Home(); // this device is "1.0.0"
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.False(opened);
        Assert.NotNull(vm.JoinError);
        Assert.Contains("2.0.0", vm.JoinError, StringComparison.Ordinal); // names the host version it refused
    }

    [Fact]
    public async Task Wrong_pin_shows_a_banner_and_opens_nothing()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        var vm = Home();
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", "9999"));

        Assert.False(opened);
        Assert.Equal("Falsche PIN.", vm.JoinError);
    }

    [Fact]
    public async Task Unreachable_host_shows_a_banner_and_opens_nothing()
    {
        var port = TestHost.FreeTcpPort(); // nothing is listening here
        var vm = Home();
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.False(opened);
        Assert.NotNull(vm.JoinError);
        Assert.Contains($"127.0.0.1:{port}", vm.JoinError, StringComparison.Ordinal); // names the device it couldn't reach
    }

    [Theory]
    [InlineData("{ not json")] // truncated JSON -- JsonDocument.Parse itself throws JsonException
    [InlineData("[]")] // well-formed JSON, wrong root kind -- TryGetProperty throws InvalidOperationException
    [InlineData("""{"personnel":[{"firstName":"Max"}]}""")] // well-formed, missing required field -- GetProperty("lastName") throws KeyNotFoundException
    public async Task A_corrupt_master_data_payload_aborts_the_join_without_opening_a_workspace(string masterDataBody)
    {
        var clock = new FixedClock();
        await using var host = await BadMasterDataHost.StartAsync(HostSession(clock).Incident, masterDataBody: masterDataBody);

        var vm = Home();
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{host.Port}", TestHost.DefaultPin));

        // The version handshake guarantees an identical build on both ends, so an unreadable
        // payload means corruption or something past the TOFU pin — abort, don't degrade into it.
        // This must hold for every shape above, not just truncated JSON: JsonDocument.Parse only
        // ever throws JsonException, but MasterDataJson.ParseRoot's TryGetProperty/GetProperty calls
        // throw InvalidOperationException or KeyNotFoundException on a well-formed-but-wrong-shape
        // document, and a hole in the catch here would either leak the hub connection below or let
        // the exception escape onto the UI thread and kill the app mid-Einsatz.
        Assert.Null(opened);
        Assert.NotNull(vm.JoinError);
        Assert.Contains("Stammdaten", vm.JoinError, StringComparison.Ordinal);

        // Like every other non-certificate failure kind, this one leaves nothing to reset trust for
        // (#181) -- a stale "Vertrauen zurücksetzen" button after a Stammdaten failure would offer
        // the user an action that cannot help them.
        Assert.False(vm.CanResetTrustedCertificate);

        // The property that actually matters: ConnectAsync had already opened the hub connection
        // before the parse failed, so the banner alone doesn't prove anything -- the same assertions
        // above would pass just as well if the session were leaked. Only the server observing the
        // connection close proves it was torn down rather than abandoned (#183).
        await host.WaitForClientDisconnectAsync(TimeSpan.FromSeconds(10));
    }
}
