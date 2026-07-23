using Feuerwehr.App.ViewModels;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.App.Tests;

// Reuse simple fakes (App.Tests is a fresh assembly — define minimal fakes here).
internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _d = new();
    public void Save(string path, Incident incident) => _d[path] = incident;
    public Incident Load(string path) => _d[path];
    public IncidentState? TryReadState(string path) => _d.TryGetValue(path, out var i) ? i.State : null;
}
internal sealed class FakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplate = new[] { "A?" },
        TruppTypes = new[] { "Angriffstrupp" },
        RadioCallSigns = new[] { "FFB 1/40/1", "Aich 42/1" },
    };
    public void Save(MasterDataSet set) { }
}
internal sealed class FakeRecent : IRecentFilesStore
{
    private readonly List<string> _l = new();
    public IReadOnlyList<string> GetRecent() => _l;
    public void Add(string path) { _l.Remove(path); _l.Insert(0, path); }
}
internal sealed class FakeDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string s) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
}
internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
}
internal sealed class NoopTicker : ITicker
{
    public IDisposable Subscribe(Action onTick) => new Sub();
    private sealed class Sub : IDisposable { public void Dispose() { } }
}
internal sealed class NoopAlarmService : IAlarmService
{
    public void Start() { }
    public void Stop() { }
}

public class MainWindowViewModelTests
{
    private static MainWindowViewModel New()
    {
        var home = new HomeViewModel(new FakeStore(), new FakeMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(), new NoopTicker(), new NoopAlarmService());
        return new MainWindowViewModel(home, new MasterDataEditorViewModel(new FakeMasterData()));
    }

    [Fact]
    public void Starts_on_home()
    {
        var vm = New();
        Assert.IsType<HomeViewModel>(vm.CurrentView);
        Assert.Null(vm.PendingPrompt);
    }

    [Fact]
    public void RequestNewIncident_shows_operator_prompt_collecting_ils()
    {
        var vm = New();
        vm.RequestNewIncidentCommand.Execute(null);
        Assert.NotNull(vm.PendingPrompt);
        Assert.True(vm.PendingPrompt!.CollectsIlsNumber);
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

    [Fact]
    public void RequestOpenFile_opens_readonly_without_prompt()
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var home = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new OpenPathDialogs(), clock, new NoopTicker(), new NoopAlarmService());
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new FakeMasterData()));

        vm.RequestOpenFileCommand.Execute(null);

        Assert.Null(vm.PendingPrompt); // no operator prompt for open
        var workspace = Assert.IsType<IncidentWorkspaceViewModel>(vm.CurrentView);
        Assert.True(workspace.IsReadOnly);
    }

    [Fact]
    public void OpenRecent_opens_readonly_without_prompt()
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var home = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new NoopTicker(), new NoopAlarmService());
        var vm = new MainWindowViewModel(home, new MasterDataEditorViewModel(new FakeMasterData()));

        vm.OpenRecent("/x.fwincident");

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
}

internal sealed class OpenPathDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string s) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
}
