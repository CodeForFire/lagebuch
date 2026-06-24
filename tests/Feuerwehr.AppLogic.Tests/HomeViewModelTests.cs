using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

internal sealed class FakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => new(
        Roles: new[] { "EL" }, Status: Array.Empty<string>(), Equipment: Array.Empty<string>(),
        Districts: Array.Empty<string>(), RadioCallSigns: Array.Empty<string>(),
        Streets: Array.Empty<Street>(), ChecklistTemplate: new[] { "A?" });
}

internal sealed class FakeRecent : IRecentFilesStore
{
    private readonly List<string> _list = new();
    public IReadOnlyList<string> GetRecent() => _list;
    public void Add(string path) { _list.Remove(path); _list.Insert(0, path); }
}

public class HomeViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void NewIncident_opens_workspace_and_adds_to_recent()
    {
        var store = new FakeStore();
        var recent = new FakeRecent();
        var dialogs = new FakeDialogs(); // PickSaveAsync returns "/x.fwincident"
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, dialogs, new FixedClock(T0), new FakeTicker());

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), null));

        Assert.NotNull(opened);
        Assert.False(opened!.IsReadOnly);
        Assert.Contains("/x.fwincident", recent.GetRecent());
        Assert.Equal("A?", opened.Checklist.Items[0].Text); // checklist template seeded
    }

    [Fact]
    public void NewIncident_with_ils_sets_number_and_suggests_filename()
    {
        var store = new FakeStore();
        var dialogs = new CapturingSaveDialogs();
        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), dialogs, new FixedClock(T0), new FakeTicker());

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        var ils = IlsNumber.Parse("1234");
        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), ils));

        Assert.Equal("Einsatz-1234.fwincident", dialogs.LastSuggestedName);
        Assert.NotNull(opened);
        Assert.Equal("1234", opened!.IlsNumberDisplay);
    }

    [Fact]
    public void OpenRecent_of_closed_incident_opens_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        seed.Close(clock);

        var recent = new FakeRecent();
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker());
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.NotNull(opened);
        Assert.True(opened!.IsReadOnly);
    }

    [Fact]
    public void OpenRecent_of_open_incident_opens_readonly_without_dead_end()
    {
        // Previously double-tapping a recent OPEN incident dead-ended (no workspace opened).
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker());
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.NotNull(opened);
        Assert.True(opened!.IsReadOnly);
        Assert.True(opened.CanContinueEditing); // still open → upgradable
    }

    [Fact]
    public void OpenFile_opens_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());

        // Dialog returns the seeded path so OpenFile has something to open.
        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new OpenReturningDialogs(), clock, new FakeTicker());
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenFileCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.True(opened!.IsReadOnly);
    }

    [Fact]
    public void OpenRecent_loads_store_once()
    {
        var store = new CountingStore();
        var clock = new FixedClock(T0);
        IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        store.ResetLoadCount();

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker());
        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.Equal(1, store.LoadCount);
    }
}

// PickOpenAsync that returns a real path (the base FakeDialogs returns null).
internal sealed class OpenReturningDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string suggestedFileName) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);
}

// Captures the suggested filename passed to PickSaveAsync.
internal sealed class CapturingSaveDialogs : IFileDialogService
{
    public string? LastSuggestedName { get; private set; }
    public Task<string?> PickSaveAsync(string suggestedFileName)
    {
        LastSuggestedName = suggestedFileName;
        return Task.FromResult<string?>("/x.fwincident");
    }
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);
}

// Counts Load calls to guard against the old double-load regression.
internal sealed class CountingStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();
    public int LoadCount { get; private set; }
    public void ResetLoadCount() => LoadCount = 0;
    public void Save(string path, Incident incident) => _saved[path] = incident;
    public Incident Load(string path) { LoadCount++; return _saved[path]; }
}
