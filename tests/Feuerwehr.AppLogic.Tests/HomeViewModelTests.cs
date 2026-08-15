using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

internal sealed class FakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplate = new[] { "A?" },
        TruppTypes = new[] { "Angriffstrupp" },
    };
    public void Save(MasterDataSet set) { }
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
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, dialogs, new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), null));

        Assert.NotNull(opened);
        Assert.False(opened!.IsReadOnly);
        Assert.Contains("/x.fwincident", recent.GetRecent());
        Assert.Equal("A?", opened.Checklist.Items[0].Text); // checklist template seeded
    }

    [Fact]
    public void NewIncident_with_number_suggests_a_filename_with_the_literal_number()
    {
        var store = new FakeStore();
        var dialogs = new CapturingSaveDialogs();
        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), dialogs, new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        var number = new IncidentNumber("B 1.2 260715 1297");
        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), number));

        // Spaces in the Einsatznummer are kept literally in the filename; no date suffix.
        Assert.Equal("Einsatz B 1.2 260715 1297.fwincident", dialogs.LastSuggestedName);
        Assert.NotNull(opened);
        Assert.Equal("B 1.2 260715 1297", opened!.IncidentNumberInput);
    }

    [Fact]
    public void NewIncident_without_number_falls_back_to_a_plain_filename()
    {
        // Defense-in-depth: the operator prompt enforces a number before this command can be
        // invoked for real, but HomeViewModel itself should stay total rather than crash on null.
        var dialogs = new CapturingSaveDialogs();
        var vm = new HomeViewModel(new FakeStore(), new FakeMasterData(), new FakeRecent(), dialogs,
            new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), null));

        Assert.Equal("Einsatz.fwincident", dialogs.LastSuggestedName);
    }

    [Fact]
    public void RecentFiles_marks_closed_incidents_and_leaves_open_ones_unmarked()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var closed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/closed.fwincident", Array.Empty<string>());
        closed.Close();
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/open.fwincident", Array.Empty<string>());

        var recent = new FakeRecent();
        recent.Add("/open.fwincident");
        recent.Add("/closed.fwincident");

        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        Assert.True(vm.RecentFiles.Single(f => f.Path == "/closed.fwincident").IsClosed);
        Assert.False(vm.RecentFiles.Single(f => f.Path == "/open.fwincident").IsClosed);
        Assert.Equal("closed.fwincident", vm.RecentFiles.Single(f => f.Path == "/closed.fwincident").FileName);
    }

    [Fact]
    public void OpenRecent_of_closed_incident_opens_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        seed.Close();

        var recent = new FakeRecent();
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());

        // Dialog returns the seeded path so OpenFile has something to open.
        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new OpenReturningDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenFileCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.True(opened!.IsReadOnly);
    }

    [Fact]
    public void OpenRecent_of_an_unreadable_file_reports_instead_of_crashing()
    {
        // A recent entry that has since been moved, truncated, or written by a newer build. The
        // Home screen has to survive it: an Einsatz is exactly the moment not to lose the app.
        var vm = new HomeViewModel(new ThrowingStore("Datei kaputt."), new FakeMasterData(), new FakeRecent(),
            new FakeDialogs(), new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenRecentCommand.Execute("/gone.fwincident");

        Assert.Null(opened);
        Assert.NotNull(vm.OpenError);
        Assert.Contains("gone.fwincident", vm.OpenError!);
        Assert.Contains("Datei kaputt.", vm.OpenError!);
    }

    [Fact]
    public void OpenFile_of_an_unreadable_file_reports_instead_of_crashing()
    {
        var vm = new HomeViewModel(new ThrowingStore("Datei kaputt."), new FakeMasterData(), new FakeRecent(),
            new OpenReturningDialogs(), new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenFileCommand.Execute(null);

        Assert.Null(opened);
        Assert.NotNull(vm.OpenError);
    }

    [Fact]
    public void A_failed_open_does_not_pollute_the_recent_list()
    {
        // The path never became a usable Einsatz, so promoting it to "zuletzt verwendet" would
        // just hand the user a button that fails again.
        var recent = new FakeRecent();
        var vm = new HomeViewModel(new ThrowingStore("kaputt"), new FakeMasterData(), recent,
            new FakeDialogs(), new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        vm.OpenRecentCommand.Execute("/gone.fwincident");

        Assert.Empty(recent.GetRecent());
        Assert.DoesNotContain(vm.RecentFiles, f => f.Path == "/gone.fwincident");
    }

    [Fact]
    public void A_successful_open_clears_a_previous_error()
    {
        var store = new SelectivelyThrowingStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        vm.OpenRecentCommand.Execute("/gone.fwincident");
        Assert.NotNull(vm.OpenError);

        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.Null(vm.OpenError);
    }

    [Fact]
    public void OpenRecent_loads_store_once()
    {
        var store = new CountingStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        store.ResetLoadCount();

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
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
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
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
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}

// Every Load fails, standing in for a moved, truncated, or too-new file.
internal sealed class ThrowingStore : IIncidentStore
{
    private readonly string _message;
    public ThrowingStore(string message) => _message = message;
    public void Save(string path, Incident incident) { }
    public Incident Load(string path) => throw new InvalidOperationException(_message);
    public IncidentState? TryReadState(string path) => null;
}

// Loads what was saved; anything else throws — lets one test fail an open, then succeed.
internal sealed class SelectivelyThrowingStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();
    public void Save(string path, Incident incident) => _saved[path] = incident;
    public Incident Load(string path) =>
        _saved.TryGetValue(path, out var i) ? i : throw new InvalidOperationException("Datei kaputt.");
    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;
}

// Counts Load calls to guard against the old double-load regression.
internal sealed class CountingStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();
    public int LoadCount { get; private set; }
    public void ResetLoadCount() => LoadCount = 0;
    public void Save(string path, Incident incident) => _saved[path] = incident;
    public Incident Load(string path) { LoadCount++; return _saved[path]; }
    // A passive peek, not a load — must not count against the load-once guard.
    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;
}
