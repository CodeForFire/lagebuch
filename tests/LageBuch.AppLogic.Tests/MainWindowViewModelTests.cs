using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Tests;

public class MainWindowViewModelTests
{
    // FixedClock here (from IncidentSessionTests.cs) requires an explicit timestamp — the original
    // parameterless new FixedClock() does not compile against it.
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    // A trust store is required here (not just cosmetic): CancelJoin_aborts_a_stuck_connection_attempt
    // below drives a real RemoteIncidentSession.ConnectAsync against a loopback listener, and
    // ConnectAsync has no accept-any fallback -- it needs a real ITrustStore to even attempt the TLS
    // handshake it's cancelling. A fresh temp-backed JsonTrustStore per HomeViewModel keeps every
    // test's TOFU state isolated from the others.
    private static MainWindowViewModel New(IFileDialogService? dialogs = null)
    {
        var trustStore = new JsonTrustStore(Path.Join(Path.GetTempPath(), $"trust-{Guid.NewGuid():N}.json"));
        var home = new HomeViewModel(new FakeStore(), new MvFakeMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0", trustStore: trustStore);
        return new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), dialogs ?? new FakeDialogs(), "0.1.0");
    }

    [Fact]
    public void Starts_on_home()
    {
        var vm = New();
        Assert.IsType<HomeViewModel>(vm.CurrentView);
        Assert.Null(vm.PendingPrompt);
    }

    [Fact]
    public void RequestNewIncident_shows_operator_prompt()
    {
        // Only who documents is asked here; the Stichwort (and every other head datum) is entered
        // afterwards through the workspace's Einsatzdaten dialog.
        var vm = New();
        vm.RequestNewIncidentCommand.Execute(null);
        Assert.NotNull(vm.PendingPrompt);
        Assert.False(vm.PendingPrompt!.CollectsHost);
    }

    [Fact]
    public void RequestNewIncident_prompt_offers_the_master_data_call_signs()
    {
        var vm = New();
        vm.RequestNewIncidentCommand.Execute(null);
        Assert.Equal(new[] { "FFB 1/40/1", "Aich 42/1" }, vm.PendingPrompt!.CallSignOptions);
    }

    [Fact]
    public void Confirming_operator_for_new_navigates_to_workspace()
    {
        var vm = New();
        vm.RequestNewIncidentCommand.Execute(null);
        vm.PendingPrompt!.OperatorName = "Müller";
        vm.PendingPrompt.ConfirmCommand.Execute(null);
        vm.ConfirmOperatorCommand.Execute(null);

        Assert.Null(vm.PendingPrompt);
        Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
    }

    [Fact]
    public void Cancelling_operator_returns_to_home_without_workspace()
    {
        var vm = New();
        vm.RequestNewIncidentCommand.Execute(null);
        vm.CancelOperatorCommand.Execute(null);
        Assert.Null(vm.PendingPrompt);
        Assert.IsType<HomeViewModel>(vm.CurrentView);
    }

    // #196: the join dialog's own Cancel button -- not a Home-page banner behind it -- must be able
    // to abort a stuck connect attempt. A bare TCP listener accepts the connection but never drives
    // the socket, hanging the TLS handshake exactly like a host that's reachable but unresponsive.
    //
    // Aborting the attempt must not also close the dialog: a cancelled join and a successful one both
    // leave HomeViewModel.JoinError null, so ConfirmOperatorAsync cannot tell them apart from that
    // alone -- it needs to know the abort was user-requested (via CancelJoinCommand) and keep the
    // prompt (and everything the operator already typed) up for a retry with adjusted Host/PIN.
    [Fact]
    public async Task CancelJoin_aborts_a_stuck_connection_attempt_without_closing_the_dialog()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var vm = New();
        vm.RequestJoinDeviceCommand.Execute(null);
        vm.PendingPrompt!.Host = $"127.0.0.1:{port}";
        vm.PendingPrompt.Pin = "0000";
        vm.PendingPrompt.OperatorName = "Client";
        vm.PendingPrompt.ConfirmCommand.Execute(null);

        var confirming = vm.ConfirmOperatorCommand.ExecuteAsync(null);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.PendingPrompt is { IsBusy: false } && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(vm.PendingPrompt!.IsBusy);

        vm.CancelJoinCommand.Execute(null);
        await confirming;

        var prompt = vm.PendingPrompt;
        Assert.NotNull(prompt); // dialog stays up -- only the connection attempt was aborted
        Assert.False(prompt!.IsBusy);
        Assert.Null(prompt.ErrorMessage); // a user-initiated cancel is not a failure
        Assert.Equal($"127.0.0.1:{port}", prompt.Host); // fields survive for an immediate retry
        Assert.Equal("0000", prompt.Pin);
        Assert.Equal("Client", prompt.OperatorName);
        Assert.IsType<HomeViewModel>(vm.CurrentView);
    }

    [Fact]
    public void RequestOpenFile_opens_readonly_without_prompt()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new OpenPathDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        vm.RequestOpenFileCommand.Execute(null);

        Assert.Null(vm.PendingPrompt); // no operator prompt for open
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        Assert.True(workspace.IsReadOnly);
    }

    // Review follow-up to #280: IncidentWorkspaceViewModel subscribes to the app-lifetime
    // IIncidentStore singleton's SaveFailed/SaveSucceeded in its constructor and previously only
    // unsubscribed via its own "ZUR STARTSEITE" -> LeaveAsync path. The top command bar's ÜBERSICHT
    // button reaches GoHomeCommand directly, bypassing that -- without NavigateAwayAsync draining the
    // outgoing workspace first, it would keep reacting to the store forever after being dropped.
    [Fact]
    public async Task GoHome_from_an_open_workspace_unsubscribes_it_from_the_store()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new OpenPathDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        vm.RequestOpenFileCommand.Execute(null);
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);

        await vm.GoHomeCommand.ExecuteAsync(null);

        Assert.IsType<HomeViewModel>(vm.CurrentView);
        store.RaiseSaveFailed(new InvalidOperationException("zu spät"));
        Assert.Null(workspace.PersistenceError); // left workspace must not react to the store anymore
    }

    // Same gap, reached via the top bar's STAMMDATEN button instead of ÜBERSICHT.
    [Fact]
    public async Task ShowMasterData_from_an_open_workspace_unsubscribes_it_from_the_store()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new OpenPathDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        vm.RequestOpenFileCommand.Execute(null);
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);
        workspace.PendingConfirm!.ConfirmCommand.Execute(null); // #463: leaving for Stammdaten asks first

        Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView);
        store.RaiseSaveFailed(new InvalidOperationException("zu spät"));
        Assert.Null(workspace.PersistenceError);
    }

    // And via ÖFFNEN/NEUER EINSATZ/VERBINDEN: opening a different incident while one is already open,
    // instead of leaving through the current workspace's own affordance first.
    [Fact]
    public async Task Opening_a_second_incident_drains_and_unsubscribes_the_first_workspace_from_the_store()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        TestSession.StartNew(store, clock, new SessionOperator("Schmidt"), "/y.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new OpenPathDialogs("/y.fwincident"), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        await vm.OpenRecent("/x.fwincident");
        var firstWorkspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);

        await vm.RequestOpenFileCommand.ExecuteAsync(null);
        var secondWorkspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        Assert.NotSame(firstWorkspace, secondWorkspace);

        store.RaiseSaveFailed(new InvalidOperationException("veraltet"));
        Assert.Null(firstWorkspace.PersistenceError); // old workspace no longer reacts
        Assert.Equal(
            "Speichern fehlgeschlagen: veraltet — Änderungen werden NICHT gesichert.",
            secondWorkspace.PersistenceError);
    }

    [Fact]
    public async Task OpenRecent_opens_readonly_without_prompt()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        await vm.OpenRecent("/x.fwincident");

        Assert.Null(vm.PendingPrompt);
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        Assert.True(workspace.IsReadOnly);
    }

    // #279 P1 finding: neither navigate-home path disposed the outgoing workspace, so its ticker
    // subscriptions (Scba/Tasks/Reminder) and every child's _session.Changed handler outlived the
    // navigation -- an abandoned workspace kept ticking, and a stray session mutation kept reaching
    // view models nobody could see anymore.
    private static (MainWindowViewModel Vm, FakeTicker Ticker) NewWithTicker()
    {
        var ticker = new FakeTicker();
        var home = new HomeViewModel(new FakeStore(), new MvFakeMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(T0), ticker, new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");
        return (vm, ticker);
    }

    private static IncidentWorkspaceViewModel OpenNewIncident(MainWindowViewModel vm)
    {
        vm.RequestNewIncidentCommand.Execute(null);
        vm.PendingPrompt!.OperatorName = "Müller";
        vm.PendingPrompt.ConfirmCommand.Execute(null);
        vm.ConfirmOperatorCommand.Execute(null);
        return Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
    }

    // The workspace keeps its session private; reaching in here is what lets these tests raise
    // Changed the same way a remote host broadcast (or another local module) would -- directly on
    // the session, not through a child's own command (which also calls back into the workspace's
    // OnChanged and would render regardless of subscription state, defeating the point of the test).
    private static LocalIncidentSession GetSession(IncidentWorkspaceViewModel workspace)
    {
        var field = typeof(IncidentWorkspaceViewModel).GetField(
            "_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (LocalIncidentSession)field!.GetValue(workspace)!;
    }

    [Fact]
    public void GoHome_disposes_the_outgoing_workspace()
    {
        var (vm, ticker) = NewWithTicker();
        var workspace = OpenNewIncident(vm);
        var session = GetSession(workspace);
        var etb = workspace.Etb;
        var entriesBefore = etb.Entries.Count;

        vm.GoHomeCommand.Execute(null);

        Assert.IsType<HomeViewModel>(vm.CurrentView);
        Assert.Equal(0, ticker.SubscriberCount); // Scba/Tasks/Reminder unsubscribed

        // The workspace is gone, but its children live on until GC -- a session mutation must no
        // longer reach them (Sync() would otherwise render the new entry).
        session.AddJournalEntry(EtbDirection.System, "Nach dem Verlassen erfasst");
        Assert.Equal(entriesBefore, etb.Entries.Count);
    }

    [Fact]
    public void LeaveToHome_from_the_workspace_disposes_it_too()
    {
        var (vm, ticker) = NewWithTicker();
        var workspace = OpenNewIncident(vm);
        var session = GetSession(workspace);
        var etb = workspace.Etb;
        var entriesBefore = etb.Entries.Count;

        // LeaveToHomeCommand is what the workspace's own "leave" affordance invokes -- it raises
        // GoHomeRequested, the same path a joined client's Ended host event drives.
        workspace.LeaveToHomeCommand.Execute(null);

        Assert.IsType<HomeViewModel>(vm.CurrentView);
        Assert.Equal(0, ticker.SubscriberCount);

        session.AddJournalEntry(EtbDirection.System, "Nach dem Verlassen erfasst");
        Assert.Equal(entriesBefore, etb.Entries.Count);
    }

    [Fact]
    public void ShowMasterData_navigates_to_the_editor()
    {
        var vm = New();
        vm.ShowMasterDataCommand.Execute(null);
        Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView);
    }

    [Fact]
    public void Leaving_a_clean_editor_navigates_without_a_prompt()
    {
        var vm = New();
        vm.ShowMasterDataCommand.Execute(null);
        vm.GoHomeCommand.Execute(null);
        Assert.IsType<HomeViewModel>(vm.CurrentView);
    }

    [Fact]
    public void Leaving_a_dirty_editor_prompts_and_stays_until_confirmed()
    {
        var vm = New();
        vm.ShowMasterDataCommand.Execute(null);
        var editor = Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView);
        editor.Sections.OfType<EditableListSection>().First().AddCommand.Execute(null); // make dirty

        vm.GoHomeCommand.Execute(null);
        Assert.NotNull(editor.PendingConfirm);
        Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView); // still on editor

        editor.PendingConfirm!.ConfirmCommand.Execute(null);
        Assert.IsType<HomeViewModel>(vm.CurrentView);
    }

    [Fact]
    public void A_second_nav_command_while_the_discard_prompt_is_up_does_not_stack_another()
    {
        var vm = New();
        vm.ShowMasterDataCommand.Execute(null);
        var editor = Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView);
        editor.Sections.OfType<EditableListSection>().First().AddCommand.Execute(null); // make dirty

        vm.GoHomeCommand.Execute(null);
        var firstPrompt = editor.PendingConfirm;
        Assert.NotNull(firstPrompt);

        vm.RequestOpenFileCommand.Execute(null); // a second nav attempt while the prompt is up

        Assert.Same(firstPrompt, editor.PendingConfirm); // still the same dialog, not a second one
        Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView); // navigation did not proceed
    }

    // #463: STAMMDATEN leaves the incident, so it asks first -- a stray tap mid-Einsatz must not
    // throw the Lagebuchführer out of the incident.
    private static MainWindowViewModel NewWithHost(IIncidentHostController host)
    {
        var home = new HomeViewModel(new FakeStore(), new MvFakeMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), host, "1.0.0");
        return new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");
    }

    [Fact]
    public async Task Show_master_data_from_an_open_incident_asks_first()
    {
        var vm = New();
        var workspace = OpenNewIncident(vm);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);

        Assert.Same(workspace, vm.CurrentView);
        Assert.Equal("STAMMDATEN ÖFFNEN", workspace.PendingConfirm!.ConfirmLabel);
    }

    [Fact]
    public async Task Confirming_the_prompt_opens_master_data()
    {
        var vm = New();
        var workspace = OpenNewIncident(vm);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);
        workspace.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.IsType<MasterDataEditorViewModel>(vm.CurrentView);
    }

    [Fact]
    public async Task Cancelling_the_prompt_keeps_the_incident_open()
    {
        var vm = New();
        var workspace = OpenNewIncident(vm);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);
        workspace.PendingConfirm!.CancelCommand.Execute(null);

        Assert.Same(workspace, vm.CurrentView);
        Assert.Null(workspace.PendingConfirm);
    }

    [Fact]
    public async Task Going_home_from_an_unshared_incident_does_not_ask()
    {
        var vm = New();
        OpenNewIncident(vm);

        await vm.GoHomeCommand.ExecuteAsync(null);

        Assert.IsType<HomeViewModel>(vm.CurrentView);
    }

    [Fact]
    public async Task Show_master_data_while_sharing_does_not_leave_the_incident()
    {
        var host = new FakeHostController();
        var vm = NewWithHost(host);
        var workspace = OpenNewIncident(vm);
        await workspace.ToggleSharingCommand.ExecuteAsync(null);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);
        Assert.Equal("FREIGABE BEENDEN", workspace.PendingConfirm!.ConfirmLabel);
        workspace.PendingConfirm.ConfirmCommand.Execute(null);

        Assert.Same(workspace, vm.CurrentView); // blocked: the confirm only ends the sharing
        Assert.False(workspace.IsSharing);
        Assert.True(host.StopCalled);
    }

    [Fact]
    public async Task Going_home_while_sharing_asks_first_and_stops_sharing_on_confirm()
    {
        var host = new FakeHostController();
        var vm = NewWithHost(host);
        var workspace = OpenNewIncident(vm);
        await workspace.ToggleSharingCommand.ExecuteAsync(null);

        await vm.GoHomeCommand.ExecuteAsync(null);
        Assert.Same(workspace, vm.CurrentView);
        Assert.False(host.StopCalled);

        workspace.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.IsType<HomeViewModel>(vm.CurrentView);
        Assert.True(host.StopCalled);
        Assert.False(host.IsHosting);
    }

    [Fact]
    public async Task A_second_navigation_while_the_leave_prompt_is_up_does_not_stack_another()
    {
        var vm = New();
        var workspace = OpenNewIncident(vm);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);
        var firstPrompt = workspace.PendingConfirm;
        await vm.GoHomeCommand.ExecuteAsync(null);

        Assert.Same(firstPrompt, workspace.PendingConfirm);
        Assert.Same(workspace, vm.CurrentView);
    }

    [Fact]
    public void ShowAbout_shows_the_about_overlay_without_touching_navigation()
    {
        var dialogs = new FakeDialogs();
        var vm = New(dialogs);

        vm.ShowAboutCommand.Execute(null);

        var about = Assert.IsType<AboutViewModel>(vm.PendingAbout);
        Assert.Equal("0.1.0", about.Version);
        Assert.Null(vm.PendingPrompt); // no operator prompt involved
        Assert.IsType<HomeViewModel>(vm.CurrentView); // navigation unchanged
    }

    [Fact]
    public async Task The_about_overlay_opens_links_through_the_shared_dialog_service()
    {
        var dialogs = new FakeDialogs();
        var vm = New(dialogs);

        vm.ShowAboutCommand.Execute(null);
        var about = Assert.IsType<AboutViewModel>(vm.PendingAbout);

        await about.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(about.RepositoryUrl, dialogs.LastOpenedUrl);
    }

    [Fact]
    public void Closing_the_about_overlay_clears_it()
    {
        var vm = New();

        vm.ShowAboutCommand.Execute(null);
        Assert.NotNull(vm.PendingAbout);
        vm.PendingAbout!.CloseCommand.Execute(null);

        Assert.Null(vm.PendingAbout);
    }
}

// NoFiles/OpenPathDialogs/MvFakeMasterData are specific to these tests and either don't exist, or
// aren't data-compatible, elsewhere in this project (see Task 5's "Interfaces" note). FakeStore,
// FakeRecent, FakeDialogs, FixedClock, FakeTicker, FakeAlarmService are reused from
// IncidentSessionTests.cs / HomeViewModelTests.cs / IncidentWorkspaceViewModelTests.cs /
// ReminderViewModelTests.cs — all internal and already visible project-wide in this assembly.
internal sealed class NoFiles : IMasterDataFileService
{
    public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

    public void Write(string path, MasterDataSet set)
    {
    }
}

// Distinctly named because HomeViewModelTests.cs's own FakeMasterData doesn't set RadioCallSigns,
// which RequestNewIncident_prompt_offers_the_master_data_call_signs below asserts on.
internal sealed class MvFakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplates = ChecklistTemplate.AufbauAbbau(new[] { new ChecklistTemplateItem("A?", false) }, null),
        TruppTypes = new[] { new TruppType("Angriffstrupp") },
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9), new Vehicle("Aich", "Aich 42/1", 6) },
    };

    public void Save(MasterDataSet set)
    {
    }
}

internal sealed class OpenPathDialogs : IFileDialogService
{
    private readonly string _path;

    public OpenPathDialogs(string path = "/x.fwincident") => _path = path;

    public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>(_path);

    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(_path);

    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);

    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);

    public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

    public Task OpenFileAsync(string path) => Task.CompletedTask;

    public Task OpenUrlAsync(string url) => Task.CompletedTask;

    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}
