using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;

namespace Feuerwehr.Sync.Hosting.Tests;

/// <summary>
/// The "Mit Gerät verbinden" join flow at the ViewModel layer (#52 §6/§7): a successful join opens a
/// thin-client workspace, and the two expected failures — a version mismatch and an unreachable /
/// not-sharing host — surface as a Home banner without throwing.
/// </summary>
public class HomeViewModelJoinTests
{
    private static HomeViewModel Home() =>
        new(new InMemoryStore(), new EmptyMasterData(), new NoRecentFiles(), new NoDialogs(),
            new FixedClock(), new NoTicker(), new NoAlarm(), new NoopIncidentHostController(), "1.0.0");

    private static LocalIncidentSession HostSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(new InMemoryStore(), clock,
            new SessionOperator("Host", "FFB 1"), "/x.fwincident", new[] { "Punkt A" });

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
        Assert.Contains("2.0.0", vm.JoinError); // names the host version it refused
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
        Assert.Contains($"127.0.0.1:{port}", vm.JoinError); // names the device it couldn't reach
    }
}
