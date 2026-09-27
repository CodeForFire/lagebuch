using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The "Mit Gerät verbinden" join flow at the ViewModel layer (#52 §6/§7): a successful join opens a
/// thin-client workspace, and the expected failures — an incompatible wire contract, a wrong PIN, an
/// unreachable / not-sharing host, and a changed TLS certificate — surface as a Home banner without
/// throwing.
/// </summary>
public class HomeViewModelJoinTests
{
    // trust defaults to a fresh InMemoryTrustStore rather than null: RemoteIncidentSession.ConnectAsync
    // requires a trust store (there is no accept-any fallback any more), so every join test needs a
    // real one -- passing an explicit instance is only necessary for tests asserting on its contents.
    private static HomeViewModel Home(ITrustStore? trust = null, IMasterDataProvider? masterData = null, ILastConnectionStore? lastConnection = null, FixedClock? clock = null) =>
        new(
            new InMemoryStore(),
            masterData ?? new EmptyMasterData(),
            new NoRecentFiles(),
            new NoDialogs(),
            clock ?? new FixedClock(),
            new NoTicker(),
            new NoAlarm(),
            new NoopIncidentHostController(),
            "1.0.0",
            trustStore: trust ?? new InMemoryTrustStore(),
            lastConnection: lastConnection);

    private static LocalIncidentSession HostSession(FixedClock clock) =>
        TestSession.StartNew(
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

        // Pickers come from the host: the FAHRZEUG dropdown lists the host's Stammdaten vehicle.
        Assert.Contains(opened!.Forces.VehicleOptions, v => v.Wache == "Host-Wache");
        Assert.DoesNotContain(opened.Forces.VehicleOptions, v => v.Wache == "Client-Wache");

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
    public async Task A_newer_app_version_on_the_host_still_joins_when_the_protocol_matches()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "2.0.0"); // host newer
        await using var _ = host;

        var vm = Home(); // this device is "1.0.0"
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        // Two devices on different releases is the normal state of a volunteer fleet, not a fault.
        Assert.True(opened);
        Assert.Null(vm.JoinError);
    }

    [Fact]
    public async Task Incompatible_protocol_shows_a_banner_naming_which_device_to_update()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(
            HostSession(clock),
            clock,
            "2.0.0",
            protocolVersion: SyncProtocol.ProtocolVersion + 5,
            minimumProtocolVersion: SyncProtocol.ProtocolVersion + 5);
        await using var _ = host;

        var vm = Home(); // this device is "1.0.0"
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.False(opened);
        Assert.NotNull(vm.JoinError);
        Assert.Contains("Bitte dieses Gerät aktualisieren", vm.JoinError, StringComparison.Ordinal);
        Assert.Contains("2.0.0", vm.JoinError, StringComparison.Ordinal); // and still names the host build
    }

    [Fact]
    public async Task Successful_join_remembers_the_host_the_stichwort_and_the_time()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        hostSession.SetKeyword("B3 Wohnung");
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;

        var lastConnection = new InMemoryLastConnectionStore();
        var clientClock = new FixedClock { Now = new DateTimeOffset(2026, 9, 24, 14, 5, 0, TimeSpan.FromHours(2)) };
        var vm = Home(lastConnection: lastConnection, clock: clientClock);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        var request = new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);
        await vm.JoinDeviceCommand.ExecuteAsync(request);

        Assert.Null(vm.JoinError);
        var expected = new LastConnection(request.Host, "B3 Wohnung", clientClock.Now, TestHost.DefaultPin);
        Assert.Equal(expected, lastConnection.GetLast());
        Assert.Equal(expected, vm.LastConnection);
        Assert.Equal("B3 Wohnung · 24.09.2026 14:05", vm.LastConnectionDetail);
        Assert.Equal($"verbunden mit {request.Host}", opened!.ConnectedHostText);
        Assert.True(opened.ShowConnectedHost);
        await opened.LeaveAsync();
    }

    [Fact]
    public async Task A_join_still_opens_when_the_last_connection_cannot_be_saved()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var vm = Home(lastConnection: new FailingLastConnectionStore());
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        Assert.Null(vm.JoinError);
        Assert.NotNull(opened);
        Assert.True(vm.HasLastConnection); // still shown for the rest of this run
        Assert.Equal("Nicht gespeichert: Kein Speicherplatz.", vm.LastConnectionError);
        await opened!.LeaveAsync();
    }

    [Fact]
    public async Task A_stichwort_set_on_the_host_after_joining_updates_the_last_connection()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;

        var lastConnection = new InMemoryLastConnectionStore();
        var vm = Home(lastConnection: lastConnection);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;
        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));
        Assert.Null(vm.LastConnection?.Keyword);

        var updated = new TaskCompletionSource();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.LastConnection) && vm.LastConnection?.Keyword is not null)
            {
                updated.TrySetResult();
            }
        };
        hostSession.SetKeyword("TH Person eingeklemmt");
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("TH Person eingeklemmt", lastConnection.GetLast()?.Keyword);
        await opened!.LeaveAsync();
    }

    [Fact]
    public async Task A_forgotten_connection_stays_forgotten_while_the_session_runs_on()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;

        var lastConnection = new InMemoryLastConnectionStore();
        var vm = Home(lastConnection: lastConnection);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;
        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin));

        vm.ForgetLastConnectionCommand.Execute(null);

        // The workspace subscribed to the session after Home did, so once its header shows the
        // new Stichwort, Home's handler has already seen the same broadcast.
        var arrived = new TaskCompletionSource();
        opened!.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IncidentWorkspaceViewModel.KeywordDisplay))
            {
                arrived.TrySetResult();
            }
        };
        hostSession.SetKeyword("TH Person eingeklemmt");
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.HasLastConnection);
        Assert.Null(lastConnection.GetLast());
        await opened.LeaveAsync();
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
    public async Task A_failed_join_does_not_remember_the_connection()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        var lastConnection = new InMemoryLastConnectionStore();
        var vm = Home(lastConnection: lastConnection);

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", "9999"));

        Assert.NotNull(vm.JoinError);
        Assert.Null(lastConnection.GetLast());
        Assert.False(vm.HasLastConnection);
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
        await using var host = await StubHost.StartAsync(HostSession(clock).Incident, masterDataBody: masterDataBody);

        var vm = Home();
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await vm.JoinDeviceCommand.ExecuteAsync(
            new JoinRequest(new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{host.Port}", TestHost.DefaultPin));

        // The handshake guarantees a compatible wire contract on both ends, so an unreadable
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

    // #182: the connect dialog (MainWindowViewModel.PendingPrompt) must stay open across a failed
    // join, with an inline error, instead of closing and forcing the operator to retype everything.
    private static MainWindowViewModel MainWindowVm(HomeViewModel home) =>
        new(home, new MasterDataEditorViewModel(new EmptyMasterData(), new NoDialogs(), new NoMasterDataFiles()), new NoDialogs(), "0.1.0");

    [Fact]
    public async Task Wrong_pin_keeps_the_join_dialog_open_with_the_error_and_clears_only_the_pin()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        var home = Home();
        var vm = MainWindowVm(home);
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        prompt.Host = $"127.0.0.1:{port}";
        prompt.Pin = "9999";
        prompt.OperatorName = "Client";
        prompt.ConfirmCommand.Execute(null);

        await vm.ConfirmOperatorCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingPrompt); // dialog stayed open
        Assert.Same(prompt, vm.PendingPrompt);
        Assert.Equal("Falsche PIN.", prompt.ErrorMessage);
        Assert.Equal(string.Empty, prompt.Pin);
        Assert.Equal($"127.0.0.1:{port}", prompt.Host); // Host/Name kept, not lost
        Assert.Equal("Client", prompt.OperatorName);
        Assert.Null(home.JoinError); // ownership moved to the dialog -- no duplicate Home banner
    }

    [Fact]
    public void RequestJoinDevice_prefills_the_last_used_host_and_pin()
    {
        var lastConnection = new InMemoryLastConnectionStore();
        lastConnection.SetLast(new LastConnection("elw-1:5859", "B3 Wohnung", new FixedClock().Now, "5393"));
        var vm = MainWindowVm(Home(lastConnection: lastConnection));

        vm.RequestJoinDeviceCommand.Execute(null);

        Assert.Equal("elw-1:5859", vm.PendingPrompt!.Host);
        Assert.Equal("5393", vm.PendingPrompt.Pin);
    }

    // The host started sharing anew since, with a new PIN: one rejected attempt, then the dialog
    // asks for the PIN and keeps everything else.
    [Fact]
    public async Task A_stale_remembered_pin_is_rejected_once_and_cleared_from_the_dialog()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        var lastConnection = new InMemoryLastConnectionStore();
        lastConnection.SetLast(new LastConnection($"127.0.0.1:{port}", "B3 Wohnung", clock.Now, "5393"));
        var vm = MainWindowVm(Home(lastConnection: lastConnection));
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        prompt.OperatorName = "Client";
        prompt.ConfirmCommand.Execute(null);

        await vm.ConfirmOperatorCommand.ExecuteAsync(null);

        Assert.Same(prompt, vm.PendingPrompt);
        Assert.Equal("Falsche PIN.", prompt.ErrorMessage);
        Assert.Equal(string.Empty, prompt.Pin);
        Assert.Equal($"127.0.0.1:{port}", prompt.Host);
    }

    [Fact]
    public void Neu_verbinden_on_home_opens_the_join_dialog_with_the_last_host_and_pin()
    {
        var lastConnection = new InMemoryLastConnectionStore();
        lastConnection.SetLast(new LastConnection("elw-1:5859", "B3 Wohnung", new FixedClock().Now, "5393"));
        var home = Home(lastConnection: lastConnection);
        var vm = MainWindowVm(home);

        home.ReconnectCommand.Execute(null);

        Assert.NotNull(vm.PendingPrompt);
        Assert.True(vm.PendingPrompt!.CollectsHost);
        Assert.Equal("elw-1:5859", vm.PendingPrompt.Host);
        Assert.Equal("5393", vm.PendingPrompt.Pin);
    }

    [Fact]
    public void RequestJoinDevice_starts_empty_when_nothing_was_ever_joined()
    {
        var vm = MainWindowVm(Home(lastConnection: new InMemoryLastConnectionStore()));

        vm.RequestJoinDeviceCommand.Execute(null);

        Assert.Equal(string.Empty, vm.PendingPrompt!.Host);
    }

    [Fact]
    public async Task A_successful_join_still_closes_the_dialog()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var vm = MainWindowVm(Home());
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        prompt.Host = $"127.0.0.1:{port}";
        prompt.Pin = TestHost.DefaultPin;
        prompt.OperatorName = "Client";
        prompt.ConfirmCommand.Execute(null);

        await vm.ConfirmOperatorCommand.ExecuteAsync(null);

        Assert.Null(vm.PendingPrompt);
        Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        await ((IncidentWorkspaceViewModel)vm.CurrentView!).LeaveAsync();
    }

    [Fact]
    public async Task Resetting_trust_from_inside_the_dialog_clears_the_banner_and_lets_a_retry_join()
    {
        var trust = new InMemoryTrustStore();
        trust.SaveThumbprint("127.0.0.1", "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");

        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var home = Home(trust);
        var vm = MainWindowVm(home);
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        prompt.Host = $"127.0.0.1:{port}";
        prompt.Pin = TestHost.DefaultPin;
        prompt.OperatorName = "Client";
        prompt.ConfirmCommand.Execute(null);
        await vm.ConfirmOperatorCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingPrompt);
        Assert.True(prompt.CertificateChanged);
        Assert.NotNull(prompt.ErrorMessage);

        vm.ResetTrustCommand.Execute(null);

        Assert.Null(prompt.ErrorMessage);
        Assert.False(prompt.CertificateChanged);
        Assert.Same(prompt, vm.PendingPrompt); // dialog still open, ready for a retry

        prompt.Pin = TestHost.DefaultPin; // ReportJoinFailure cleared it
        prompt.ConfirmCommand.Execute(null);
        await vm.ConfirmOperatorCommand.ExecuteAsync(null);

        Assert.Null(vm.PendingPrompt);
        Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        await ((IncidentWorkspaceViewModel)vm.CurrentView!).LeaveAsync();
    }

    // #167 P1: a stuck join (host reachable but never completing the handshake) previously had no
    // way out but the network layer's own multi-second timeout. IncludeCancelCommand wires an
    // explicit abort — this pins that cancelling clears IsRunning and leaves no error banner up,
    // since the user asked for this, it isn't a failure.
    [Fact]
    public async Task Cancelling_a_stuck_join_clears_running_state_without_a_banner()
    {
        // A bare listener accepts the TCP connection but never drives the socket, so the client's
        // TLS handshake hangs indefinitely — exactly the "stuck" case the cancel button is for.
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var vm = Home();
        var opened = false;
        vm.WorkspaceOpened = _ => opened = true;

        // The cancel affordance only makes sense while a join is actually in flight.
        Assert.False(vm.JoinDeviceCancelCommand.CanExecute(null));

        var request = new JoinRequest(new SessionOperator("Client"), $"127.0.0.1:{port}", "0000");
        var join = vm.JoinDeviceCommand.ExecuteAsync(request);

        // Let the connect attempt actually reach the hung TLS handshake before cancelling it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!vm.JoinDeviceCommand.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(vm.JoinDeviceCommand.IsRunning);
        Assert.True(vm.JoinDeviceCancelCommand.CanExecute(null));
        vm.JoinDeviceCancelCommand.Execute(null);
        await join;

        Assert.False(vm.JoinDeviceCommand.IsRunning);
        Assert.False(vm.JoinDeviceCancelCommand.CanExecute(null));
        Assert.Null(vm.JoinError);
        Assert.False(opened);
    }
}

internal sealed class NoMasterDataFiles : IMasterDataFileService
{
    public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

    public void Write(string path, MasterDataSet set)
    {
    }
}
