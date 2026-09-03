using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

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
        Assert.Equal("A?", opened.ChecklistAufbau.Items[0].Text); // checklist template seeded
    }

    [Fact]
    public void NewIncident_with_keyword_suggests_a_date_time_stichwort_filename()
    {
        var store = new FakeStore();
        var dialogs = new CapturingSaveDialogs();
        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), dialogs, new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), "B3P"));

        // The Einsatznummer is unknown at creation (#69) -- the filename is date + time + Stichwort.
        Assert.Equal("20260622-0900-B3P.fwincident", dialogs.LastSuggestedName);
        Assert.NotNull(opened);
    }

    [Fact]
    public void NewIncident_without_keyword_falls_back_to_a_timestamp_only_filename()
    {
        var dialogs = new CapturingSaveDialogs();
        var vm = new HomeViewModel(
            new FakeStore(),
            new FakeMasterData(),
            new FakeRecent(),
            dialogs,
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0");

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), null));

        Assert.Equal("20260622-0900.fwincident", dialogs.LastSuggestedName);
    }

    [Fact]
    public void NewIncident_passes_the_last_known_folder_as_the_initial_folder()
    {
        var dialogs = new CapturingSaveDialogs();
        var lastFolder = new FakeLastSaveFolderStore { Saved = "/einsaetze/2026" };
        var vm = new HomeViewModel(
            new FakeStore(),
            new FakeMasterData(),
            new FakeRecent(),
            dialogs,
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0",
            lastSaveFolder: lastFolder);

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), "B3P"));

        Assert.Equal("/einsaetze/2026", dialogs.LastInitialFolder);
    }

    [Fact]
    public void NewIncident_remembers_the_folder_it_saved_to()
    {
        var dialogs = new CapturingSaveDialogs { ReturnPath = "/einsaetze/2027/20260622-0900-B3P.fwincident" };
        var lastFolder = new FakeLastSaveFolderStore();
        var vm = new HomeViewModel(
            new FakeStore(),
            new FakeMasterData(),
            new FakeRecent(),
            dialogs,
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0",
            lastSaveFolder: lastFolder);

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), "B3P"));

        // The stored folder is whatever Path.GetDirectoryName yields on this OS, so derive
        // the expectation from the same input instead of hardcoding a separator flavor.
        Assert.Equal(Path.GetDirectoryName(dialogs.ReturnPath), lastFolder.Saved);
    }

    [Fact]
    public void RecentFiles_is_sorted_by_filename_descending_regardless_of_open_order()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260101-0900-A.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260301-0900-B.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260201-0900-C.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

        var recent = new FakeRecent();
        recent.Add("/20260101-0900-A.fwincident");
        recent.Add("/20260301-0900-B.fwincident");
        recent.Add("/20260201-0900-C.fwincident");

        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        Assert.Equal(
            new[] { "20260301-0900-B.fwincident", "20260201-0900-C.fwincident", "20260101-0900-A.fwincident" },
            vm.RecentFiles.Select(f => f.FileName));
    }

    [Fact]
    public void Newly_opened_incident_is_inserted_into_sorted_position_not_just_at_front()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260101-0900-A.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260301-0900-B.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

        var recent = new FakeRecent();
        recent.Add("/20260101-0900-A.fwincident");
        recent.Add("/20260301-0900-B.fwincident");

        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260201-0900-C.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        vm.OpenRecentCommand.Execute("/20260201-0900-C.fwincident");

        Assert.Equal(
            new[] { "20260301-0900-B.fwincident", "20260201-0900-C.fwincident", "20260101-0900-A.fwincident" },
            vm.RecentFiles.Select(f => f.FileName));
    }

    [Fact]
    public void RecentFiles_marks_closed_incidents_and_leaves_open_ones_unmarked()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var closed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/closed.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        closed.Close();
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/open.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        var vm = new HomeViewModel(
            new ThrowingStore("Datei kaputt."),
            new FakeMasterData(),
            new FakeRecent(),
            new FakeDialogs(),
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0");
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenRecentCommand.Execute("/gone.fwincident");

        Assert.Null(opened);
        Assert.NotNull(vm.OpenError);
        Assert.Contains("gone.fwincident", vm.OpenError!, StringComparison.Ordinal);
        Assert.Contains("Datei kaputt.", vm.OpenError!, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenFile_of_an_unreadable_file_reports_instead_of_crashing()
    {
        var vm = new HomeViewModel(
            new ThrowingStore("Datei kaputt."),
            new FakeMasterData(),
            new FakeRecent(),
            new OpenReturningDialogs(),
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0");
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
        var vm = new HomeViewModel(
            new ThrowingStore("kaputt"),
            new FakeMasterData(),
            recent,
            new FakeDialogs(),
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0");

        vm.OpenRecentCommand.Execute("/gone.fwincident");

        Assert.Empty(recent.GetRecent());
        Assert.DoesNotContain(vm.RecentFiles, f => f.Path == "/gone.fwincident");
    }

    [Fact]
    public void A_successful_open_clears_a_previous_error()
    {
        var store = new SelectivelyThrowingStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        store.ResetLoadCount();

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.Equal(1, store.LoadCount);
    }
}

internal sealed class FakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("A?", false) },
        TruppTypes = new[] { "Angriffstrupp" },
    };

    public void Save(MasterDataSet set)
    {
    }
}

internal sealed class FakeRecent : IRecentFilesStore
{
    private readonly List<string> _list = new();

    public IReadOnlyList<string> GetRecent() => _list;

    public void Add(string path)
    {
        _list.Remove(path);
        _list.Insert(0, path);
    }
}

internal sealed class FakeLastSaveFolderStore : ILastSaveFolderStore
{
    public string? Saved { get; set; }

    public string? GetLastFolder() => Saved;

    public void SetLastFolder(string folder) => Saved = folder;
}

// PickOpenAsync that returns a real path (the base FakeDialogs returns null).
internal sealed class OpenReturningDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>("/x.fwincident");

    public Task<string?> PickOpenAsync() => Task.FromResult<string?>("/x.fwincident");

    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);

    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

    public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

    public Task OpenFileAsync(string path) => Task.CompletedTask;

    public Task OpenUrlAsync(string url) => Task.CompletedTask;

    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}

// Captures the suggested filename and initial folder passed to PickSaveAsync.
internal sealed class CapturingSaveDialogs : IFileDialogService
{
    public string? LastSuggestedName { get; private set; }

    public string? LastInitialFolder { get; private set; }

    public string ReturnPath { get; set; } = "/x.fwincident";

    public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null)
    {
        LastSuggestedName = suggestedFileName;
        LastInitialFolder = initialFolder;
        return Task.FromResult<string?>(ReturnPath);
    }

    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);

    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

    public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

    public Task OpenFileAsync(string path) => Task.CompletedTask;

    public Task OpenUrlAsync(string url) => Task.CompletedTask;

    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}

// Every Load fails, standing in for a moved, truncated, or too-new file.
internal sealed class ThrowingStore : IIncidentStore
{
    private readonly string _message;

    public ThrowingStore(string message) => _message = message;

    public void Save(string path, Incident incident)
    {
    }

    public Task FlushAsync() => Task.CompletedTask;

    public Incident Load(string path) => throw new InvalidOperationException(_message);

    public IncidentState? TryReadState(string path) => null;

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Combine(path, storageFileName);

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }
}

// Loads what was saved; anything else throws — lets one test fail an open, then succeed.
internal sealed class SelectivelyThrowingStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();

    public void Save(string path, Incident incident) => _saved[path] = incident;

    public Task FlushAsync() => Task.CompletedTask;

    public Incident Load(string path) =>
        _saved.TryGetValue(path, out var i) ? i : throw new InvalidOperationException("Datei kaputt.");

    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Combine(path, storageFileName);

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }
}

// Counts Load calls to guard against the old double-load regression.
internal sealed class CountingStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();

    public int LoadCount { get; private set; }

    public void ResetLoadCount() => LoadCount = 0;

    public void Save(string path, Incident incident) => _saved[path] = incident;

    public Task FlushAsync() => Task.CompletedTask;

    public Incident Load(string path)
    {
        LoadCount++;
        return _saved[path];
    }

    // A passive peek, not a load — must not count against the load-once guard.
    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Combine(path, storageFileName);

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }
}
