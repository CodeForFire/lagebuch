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

    // Both join steps (#459) back to back, for tests about what a join does rather than about the
    // dialog in between: reach the device, and join as the operator only if that worked.
    private static async Task JoinAsync(HomeViewModel vm, SessionOperator op, string host, string? pin)
    {
        await vm.ReachDeviceCommand.ExecuteAsync(new DeviceRequest(host, pin));
        if (vm.JoinError is null)
        {
            await vm.JoinDeviceCommand.ExecuteAsync(op);
        }
    }

    private static Task JoinAsync(HomeViewModel vm, (SessionOperator Op, string Host, string? Pin) request) =>
        JoinAsync(vm, request.Op, request.Host, request.Pin);

    [Fact]
    public async Task Successful_join_opens_a_thin_client_workspace()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var vm = Home();
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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
        var request = (new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin);
        await JoinAsync(vm, request);
        Assert.NotNull(vm.JoinError); // stale thumbprint from the setup above -- the actual host cert differs

        vm.ResetTrustedCertificateCommand.Execute(null);

        Assert.Null(vm.JoinError);
        Assert.False(vm.CanResetTrustedCertificate);
        Assert.Null(trust.GetThumbprint("127.0.0.1")); // the stale pin is gone, so the retry below can re-pin it

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;
        await JoinAsync(vm, request);

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
        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", "9999");

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

        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        var request = (Op: new SessionOperator("Client", "RUF 1"), Host: $"127.0.0.1:{port}", Pin: TestHost.DefaultPin);
        await JoinAsync(vm, request);

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

        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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
        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);
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
        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", "9999");

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

        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", "9999");

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

        await JoinAsync(vm, new SessionOperator("Client"), $"127.0.0.1:{port}", TestHost.DefaultPin);

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

        await JoinAsync(vm, new SessionOperator("Client", "RUF 1"), $"127.0.0.1:{host.Port}", TestHost.DefaultPin);

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

        // The Stammdaten are read while the device is only reached (#459), so an unreadable set stops
        // the join before a hub connection exists at all -- nothing is left open to leak (#183), and
        // there is nothing pending to join into afterwards.
        Assert.Equal(0, host.HubConnectionCount);
        Assert.Null(vm.PendingJoinMasterData);
    }

    // #182: the connect dialog (MainWindowViewModel.PendingPrompt) must stay open across a failed
    // join, with an inline error, instead of closing and forcing the operator to retype everything.
    private static MainWindowViewModel MainWindowVm(HomeViewModel home) =>
        new(home, new MasterDataEditorViewModel(new EmptyMasterData(), new NoDialogs(), new NoMasterDataFiles()), new NoDialogs(), "0.1.0");

    // The dialog's first stage as the view drives it: Confirm raises ConnectRequested, which the
    // view routes to ConnectToDeviceCommand.
    private static async Task ReachAsync(MainWindowViewModel vm, OperatorPromptViewModel prompt)
    {
        prompt.ConfirmCommand.Execute(null);
        await vm.ConnectToDeviceCommand.ExecuteAsync(null);
    }

    // The second stage: name the operator and confirm, as the view does on Result.
    private static async Task ConfirmOperatorAsync(MainWindowViewModel vm, OperatorPromptViewModel prompt, string name)
    {
        prompt.OperatorName = name;
        prompt.ConfirmCommand.Execute(null);
        await vm.ConfirmOperatorCommand.ExecuteAsync(null);
    }

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

        await ReachAsync(vm, prompt);

        Assert.Same(prompt, vm.PendingPrompt); // dialog stayed open
        Assert.True(prompt.IsHostStage);       // and still asks for host and PIN
        Assert.Equal("Falsche PIN.", prompt.ErrorMessage);
        Assert.Equal(string.Empty, prompt.Pin);
        Assert.Equal($"127.0.0.1:{port}", prompt.Host); // Host kept, not lost
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

        await ReachAsync(vm, prompt);

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

        await ReachAsync(vm, prompt);
        await ConfirmOperatorAsync(vm, prompt, "Client");

        Assert.Null(vm.PendingPrompt);
        Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        await ((IncidentWorkspaceViewModel)vm.CurrentView!).LeaveAsync();
    }

    // #459: the operator is asked for only once the host is reached, and the suggestions are the
    // host's own personnel and call signs -- this device's roster is not the one the join uses.
    [Fact]
    public async Task Reaching_the_host_asks_for_the_operator_from_the_hosts_own_personnel()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        hostSession.SetKeyword("B3 Wohnung");
        hostSession.SetAddress("Hauptstraße 5", "Nord");
        var hostSet = MasterDataSyncTests.SetWith("Host-Wache", 60) with
        {
            Personnel = new[]
            {
                new Person("Schmidt", "Anna", null, "FFB 12/2", null),
                new Person("Nachbar", "Nora", null, null, null, IsOwn: false),
            },
        };
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0", masterData: hostSet);
        await using var _ = host;

        var local = MasterDataSet.Empty with { Personnel = new[] { new Person("Lokal", "Lena", null, "Lokal 1", null) } };
        var vm = MainWindowVm(Home(masterData: new FixedMasterData(local)));
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        Assert.True(prompt.IsHostStage);
        Assert.False(prompt.AsksForOperator);
        Assert.Empty(prompt.PersonOptions); // nothing to suggest before the host is reached
        prompt.Host = $"127.0.0.1:{port}";
        prompt.Pin = TestHost.DefaultPin;

        await ReachAsync(vm, prompt);

        Assert.True(prompt.IsOperatorStage);
        Assert.True(prompt.AsksForOperator);
        Assert.Equal("B3 Wohnung · Hauptstraße 5, Nord", prompt.JoinedIncidentDisplay);
        Assert.Equal(new[] { "Schmidt, Anna" }, prompt.PersonOptions);
        Assert.Contains("FFB 12/2", prompt.CallSignOptions);
        Assert.DoesNotContain("Lokal 1", prompt.CallSignOptions);

        // Picking the host's person fills in the host's Funkrufname for them.
        prompt.OperatorName = "Schmidt, Anna";
        Assert.Equal("FFB 12/2", prompt.OperatorCallSign);

        await ConfirmOperatorAsync(vm, prompt, "Schmidt, Anna");
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        Assert.Equal("Schmidt, Anna (FFB 12/2)", workspace.OperatorDisplay);
        await workspace.LeaveAsync();
    }

    [Fact]
    public async Task Closing_the_dialog_after_reaching_the_host_opens_nothing_and_forgets_the_host()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        var home = Home();
        var vm = MainWindowVm(home);
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        prompt.Host = $"127.0.0.1:{port}";
        prompt.Pin = TestHost.DefaultPin;
        await ReachAsync(vm, prompt);
        Assert.NotNull(home.PendingJoinMasterData);

        await vm.CancelOperatorCommand.ExecuteAsync(null);

        Assert.Null(vm.PendingPrompt);
        Assert.Same(home, vm.CurrentView);
        Assert.Null(home.PendingJoinMasterData);
        Assert.Null(home.PendingJoinIncident);
    }

    // The host stopped sharing while the name was typed: the reached connection is spent, so the
    // dialog goes back to host and PIN with the reason, instead of pretending the join still stands.
    [Fact]
    public async Task A_host_lost_before_the_operator_was_confirmed_sends_the_dialog_back_to_host_and_pin()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");

        var home = Home();
        var vm = MainWindowVm(home);
        vm.RequestJoinDeviceCommand.Execute(null);
        var prompt = vm.PendingPrompt!;
        prompt.Host = $"127.0.0.1:{port}";
        prompt.Pin = TestHost.DefaultPin;
        await ReachAsync(vm, prompt);
        Assert.True(prompt.IsOperatorStage);

        await host.DisposeAsync();
        await ConfirmOperatorAsync(vm, prompt, "Client");

        Assert.Same(prompt, vm.PendingPrompt);
        Assert.True(prompt.IsHostStage);
        Assert.Contains("nicht möglich", prompt.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("Client", prompt.OperatorName); // kept for the retry
        Assert.Null(home.PendingJoinMasterData);
        Assert.Same(home, vm.CurrentView);
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
        await ReachAsync(vm, prompt);

        Assert.NotNull(vm.PendingPrompt);
        Assert.True(prompt.CertificateChanged);
        Assert.NotNull(prompt.ErrorMessage);

        vm.ResetTrustCommand.Execute(null);

        Assert.Null(prompt.ErrorMessage);
        Assert.False(prompt.CertificateChanged);
        Assert.Same(prompt, vm.PendingPrompt); // dialog still open, ready for a retry

        prompt.Pin = TestHost.DefaultPin; // ReportJoinFailure cleared it
        await ReachAsync(vm, prompt);
        await ConfirmOperatorAsync(vm, prompt, "Client");

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
        Assert.False(vm.ReachDeviceCancelCommand.CanExecute(null));

        var reach = vm.ReachDeviceCommand.ExecuteAsync(new DeviceRequest($"127.0.0.1:{port}", "0000"));

        // Let the connect attempt actually reach the hung TLS handshake before cancelling it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!vm.ReachDeviceCommand.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(vm.ReachDeviceCommand.IsRunning);
        Assert.True(vm.ReachDeviceCancelCommand.CanExecute(null));
        vm.ReachDeviceCancelCommand.Execute(null);
        await reach;

        Assert.False(vm.ReachDeviceCommand.IsRunning);
        Assert.False(vm.ReachDeviceCancelCommand.CanExecute(null));
        Assert.Null(vm.JoinError);
        Assert.Null(vm.PendingJoinMasterData);
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
