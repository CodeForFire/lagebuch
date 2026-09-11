using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class MainWindowViewModelTests
{
    // FixedClock here (from IncidentSessionTests.cs) requires an explicit timestamp — the original
    // parameterless new FixedClock() does not compile against it.
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MainWindowViewModel New(IFileDialogService? dialogs = null)
    {
        var home = new HomeViewModel(new FakeStore(), new MvFakeMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
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
    public void RequestNewIncident_shows_operator_prompt_collecting_keyword()
    {
        var vm = New();
        vm.RequestNewIncidentCommand.Execute(null);
        Assert.NotNull(vm.PendingPrompt);
        Assert.True(vm.PendingPrompt!.CollectsKeyword);
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new OpenPathDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        vm.RequestOpenFileCommand.Execute(null);
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);

        await vm.ShowMasterDataCommand.ExecuteAsync(null);

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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Schmidt"), "/y.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var home = new HomeViewModel(store, new MvFakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new MvFakeMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0");

        await vm.OpenRecent("/x.fwincident");

        Assert.Null(vm.PendingPrompt);
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        Assert.True(workspace.IsReadOnly);
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
    public MasterDataSet Read(string path) => MasterDataSet.Empty;

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
        ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("A?", false) },
        TruppTypes = new[] { "Angriffstrupp" },
        RadioCallSigns = new[] { "FFB 1/40/1", "Aich 42/1" },
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
