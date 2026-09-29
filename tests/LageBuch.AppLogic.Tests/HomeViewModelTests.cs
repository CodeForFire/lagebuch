using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class HomeViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    // The rail is built from Stammdaten now, so a test reaches its checklist through the nav
    // items rather than a fixed ChecklistAufbau property.
    private static ChecklistViewModel FirstChecklist(IncidentWorkspaceViewModel vm) =>
        (ChecklistViewModel)vm.NavItems.First(i => i.IsChecklist).Content;

    [Fact]
    public void NewIncident_opens_workspace_and_adds_to_recent()
    {
        var store = new FakeStore();
        var recent = new FakeRecent();
        var dialogs = new FakeDialogs(); // PickSaveAsync returns "/x.fwincident"
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, dialogs, new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller")));

        Assert.NotNull(opened);
        Assert.False(opened!.IsReadOnly);
        Assert.Contains("/x.fwincident", recent.GetRecent());
        Assert.Equal("A?", FirstChecklist(opened).Items[0].Text); // checklist template seeded
    }

    [Fact]
    public void NewIncident_suggests_a_date_time_filename_and_starts_without_head_data()
    {
        var store = new FakeStore();
        var dialogs = new CapturingSaveDialogs();
        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), dialogs, new FixedClock(T0), new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller")));

        // Neither the Einsatznummer nor the Stichwort is known at creation (#69) -- both are
        // entered later through the Einsatzdaten dialog -- so the filename is date + time only.
        Assert.Equal("20260622-0900.fwincident", dialogs.LastSuggestedName);
        Assert.NotNull(opened);
        Assert.Equal("Unbenannter Einsatz", opened!.HeroText);
        Assert.Null(store.Load("/x.fwincident").Keyword);
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

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller")));

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

        vm.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller")));

        // The stored folder is whatever Path.GetDirectoryName yields on this OS, so derive
        // the expectation from the same input instead of hardcoding a separator flavor.
        Assert.Equal(Path.GetDirectoryName(dialogs.ReturnPath), lastFolder.Saved);
    }

    [Fact]
    public void RecentFiles_is_sorted_by_filename_descending_regardless_of_open_order()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260101-0900-A.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260301-0900-B.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260201-0900-C.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260101-0900-A.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260301-0900-B.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

        var recent = new FakeRecent();
        recent.Add("/20260101-0900-A.fwincident");
        recent.Add("/20260301-0900-B.fwincident");

        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/20260201-0900-C.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        var closed = TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/closed.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        closed.Close();
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/open.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

        var recent = new FakeRecent();
        recent.Add("/open.fwincident");
        recent.Add("/closed.fwincident");

        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        Assert.True(vm.RecentFiles.Single(f => f.Path == "/closed.fwincident").IsClosed);
        Assert.False(vm.RecentFiles.Single(f => f.Path == "/open.fwincident").IsClosed);
        Assert.Equal("closed.fwincident", vm.RecentFiles.Single(f => f.Path == "/closed.fwincident").FileName);
    }

    // #291: the probes open each file's database, so they run after the list is up. Capturing the
    // work instead of running it pins what the first frame shows -- no timing involved.
    [Fact]
    public void RecentFiles_is_populated_and_sorted_before_any_state_probe_runs()
    {
        var store = new StateProbeStore { ["/20260101-0900-A.fwincident"] = IncidentState.Closed };
        var recent = new FakeRecent("/20260101-0900-A.fwincident", "/20260301-0900-B.fwincident", "/20260201-0900-C.fwincident");
        Action? probe = null;

        var vm = HomeWithRecent(recent, store, runInBackground: work => probe = work);

        var expectedOrder = new[] { "20260301-0900-B.fwincident", "20260201-0900-C.fwincident", "20260101-0900-A.fwincident" };
        Assert.Equal(expectedOrder, vm.RecentFiles.Select(f => f.FileName));
        Assert.All(vm.RecentFiles, f => Assert.False(f.IsClosed));
        Assert.Equal(0, store.Probes);
        Assert.NotNull(probe);

        probe();

        Assert.Equal(expectedOrder, vm.RecentFiles.Select(f => f.FileName));
        Assert.True(vm.RecentFiles.Single(f => f.Path == "/20260101-0900-A.fwincident").IsClosed);
        Assert.False(vm.RecentFiles.Single(f => f.Path == "/20260301-0900-B.fwincident").IsClosed);
    }

    [Fact]
    public void A_state_probe_that_throws_leaves_the_row_unmarked()
    {
        var store = new StateProbeStore { ["/closed.fwincident"] = IncidentState.Closed };
        store.Throws.Add("/broken.fwincident");

        var vm = HomeWithRecent(new FakeRecent("/broken.fwincident", "/closed.fwincident"), store);

        Assert.False(vm.RecentFiles.Single(f => f.Path == "/broken.fwincident").IsClosed);
        Assert.True(vm.RecentFiles.Single(f => f.Path == "/closed.fwincident").IsClosed);
    }

    [Fact]
    public void A_late_state_probe_does_not_bring_back_a_removed_row()
    {
        var store = new StateProbeStore { ["/closed.fwincident"] = IncidentState.Closed };
        Action? probe = null;
        var vm = HomeWithRecent(new FakeRecent("/open.fwincident", "/closed.fwincident"), store, runInBackground: work => probe = work);

        vm.RemoveRecentCommand.Execute("/closed.fwincident");
        probe!();

        Assert.Equal(new[] { "/open.fwincident" }, vm.RecentFiles.Select(f => f.Path));
    }

    [Fact]
    public void OpenRecent_of_closed_incident_opens_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

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
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        vm.OpenRecentCommand.Execute("/gone.fwincident");
        Assert.NotNull(vm.OpenError);

        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.Null(vm.OpenError);
    }

    private static HomeViewModel HomeWithRecent(FakeRecent recent, IIncidentStore? store = null, IFileDialogService? dialogs = null, Action<Action>? runInBackground = null) =>
        new(
            store ?? new FakeStore(),
            new FakeMasterData(),
            recent,
            dialogs ?? new FakeDialogs(),
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0",
            runInBackground: runInBackground);

    [Fact]
    public void Removing_a_recent_entry_drops_it_from_the_list_and_the_store()
    {
        var recent = new FakeRecent("/b.fwincident", "/a.fwincident");
        var vm = HomeWithRecent(recent);

        vm.RemoveRecentCommand.Execute("/a.fwincident");

        Assert.Equal(new[] { "/b.fwincident" }, recent.GetRecent());
        Assert.Equal(new[] { "/b.fwincident" }, vm.RecentFiles.Select(f => f.Path));
        Assert.Null(vm.RecentFilesError);
    }

    // The button says "Aus der Liste entfernen", not "Löschen": the Einsatz itself must survive.
    [Fact]
    public void Removing_a_recent_entry_leaves_the_incident_file_on_disk()
    {
        var path = Path.Join(Path.GetTempPath(), $"remove-{Guid.NewGuid():N}.fwincident");
        File.WriteAllText(path, "Einsatz");
        try
        {
            var vm = HomeWithRecent(new FakeRecent(path));

            vm.RemoveRecentCommand.Execute(path);

            Assert.Empty(vm.RecentFiles);
            Assert.True(File.Exists(path));
            Assert.Equal("Einsatz", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_recent_entry_that_cannot_be_removed_stays_listed_with_the_reason()
    {
        var vm = HomeWithRecent(new FakeRecent("/a.fwincident") { FailRemove = true });

        vm.RemoveRecentCommand.Execute("/a.fwincident");

        Assert.Single(vm.RecentFiles);
        Assert.Equal("Nicht entfernt: Zugriff verweigert.", vm.RecentFilesError);
    }

    [Fact]
    public void A_listed_entry_that_fails_to_open_offers_removal_in_the_banner()
    {
        var vm = HomeWithRecent(new FakeRecent("/gone.fwincident"), new ThrowingStore("Datei kaputt."));

        vm.OpenRecentCommand.Execute("/gone.fwincident");

        Assert.NotNull(vm.OpenError);
        Assert.Equal("/gone.fwincident", vm.FailedRecentPath);
    }

    // A file picked through the dialog was never on the list, so there is nothing to take off it.
    [Fact]
    public void A_picked_file_that_fails_to_open_offers_no_removal()
    {
        var vm = HomeWithRecent(new FakeRecent(), new ThrowingStore("Datei kaputt."), new OpenReturningDialogs());

        vm.OpenFileCommand.Execute(null);

        Assert.NotNull(vm.OpenError);
        Assert.Null(vm.FailedRecentPath);
    }

    [Fact]
    public void Removing_the_entry_that_failed_to_open_clears_the_banner()
    {
        var recent = new FakeRecent("/gone.fwincident", "/other.fwincident");
        var vm = HomeWithRecent(recent, new ThrowingStore("Datei kaputt."));
        vm.OpenRecentCommand.Execute("/gone.fwincident");

        vm.RemoveRecentCommand.Execute(vm.FailedRecentPath);

        Assert.Null(vm.OpenError);
        Assert.Null(vm.FailedRecentPath);
        Assert.Equal(new[] { "/other.fwincident" }, recent.GetRecent());
    }

    // The banner is about a different file, so taking some other row off leaves it standing.
    [Fact]
    public void Removing_another_entry_keeps_the_banner()
    {
        var vm = HomeWithRecent(new FakeRecent("/gone.fwincident", "/other.fwincident"), new ThrowingStore("Datei kaputt."));
        vm.OpenRecentCommand.Execute("/gone.fwincident");

        vm.RemoveRecentCommand.Execute("/other.fwincident");

        Assert.NotNull(vm.OpenError);
        Assert.Equal("/gone.fwincident", vm.FailedRecentPath);
    }

    [Fact]
    public void OpenRecent_loads_store_once()
    {
        var store = new CountingStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        store.ResetLoadCount();

        var vm = new HomeViewModel(store, new FakeMasterData(), new FakeRecent(), new FakeDialogs(), clock, new FakeTicker(), new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");
        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.Equal(1, store.LoadCount);
    }

    private static HomeViewModel HomeWithLastConnection(LastConnection? last, FakeLastConnectionStore? store = null) =>
        new(
            new FakeStore(),
            new FakeMasterData(),
            new FakeRecent(),
            new FakeDialogs(),
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0",
            lastConnection: store ?? new FakeLastConnectionStore { Saved = last });

    [Fact]
    public void Home_shows_the_last_connection_with_its_stichwort_and_time()
    {
        var vm = HomeWithLastConnection(new LastConnection("elw-1", "B3 Wohnung", T0));

        Assert.True(vm.HasLastConnection);
        Assert.Equal("elw-1", vm.LastConnection?.Host);
        Assert.Equal("B3 Wohnung · 22.06.2026 09:00", vm.LastConnectionDetail);
    }

    [Fact]
    public void Home_leaves_the_stichwort_out_while_the_host_has_none()
    {
        var vm = HomeWithLastConnection(new LastConnection("elw-1", null, T0));

        Assert.Equal("22.06.2026 09:00", vm.LastConnectionDetail);
    }

    [Fact]
    public void Home_shows_no_last_connection_when_this_device_never_joined()
    {
        var vm = HomeWithLastConnection(null);

        Assert.False(vm.HasLastConnection);
        Assert.Null(vm.LastConnectionDetail);
    }

    [Fact]
    public void Neu_verbinden_asks_the_shell_to_open_the_join_dialog()
    {
        var vm = HomeWithLastConnection(new LastConnection("elw-1", null, T0));
        var requested = false;
        vm.ReconnectRequested = () => requested = true;

        vm.ReconnectCommand.Execute(null);

        Assert.True(requested);
    }

    [Fact]
    public void Forgetting_the_last_connection_hides_the_card_and_clears_the_store()
    {
        var store = new FakeLastConnectionStore { Saved = new LastConnection("elw-1", null, T0, "5393") };
        var vm = HomeWithLastConnection(null, store);

        vm.ForgetLastConnectionCommand.Execute(null);

        Assert.False(vm.HasLastConnection);
        Assert.Null(store.Saved);
        Assert.Null(vm.LastConnectionError);
    }

    [Fact]
    public void A_connection_that_cannot_be_deleted_stays_on_the_card_with_the_reason()
    {
        var store = new FakeLastConnectionStore { Saved = new LastConnection("elw-1", null, T0, "5393"), FailClear = true };
        var vm = HomeWithLastConnection(null, store);

        vm.ForgetLastConnectionCommand.Execute(null);

        Assert.True(vm.HasLastConnection);
        Assert.Equal("Nicht gelöscht: Zugriff verweigert.", vm.LastConnectionError);
    }
}

internal sealed class FakeLastConnectionStore : ILastConnectionStore
{
    public LastConnection? Saved { get; set; }

    public bool FailClear { get; init; }

    public LastConnection? GetLast() => Saved;

    public void SetLast(LastConnection connection) => Saved = connection;

    public void Clear()
    {
        if (FailClear)
        {
            throw new UnauthorizedAccessException("Zugriff verweigert.");
        }

        Saved = null;
    }
}

internal sealed class FakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => MasterDataSet.Empty with
    {
        Roles = new[] { new Role("EL") },
        ChecklistTemplates = ChecklistTemplate.AufbauAbbau(new[] { new ChecklistTemplateItem("A?", false) }, null),
        TruppTypes = new[] { new TruppType("Angriffstrupp") },
    };

    public void Save(MasterDataSet set)
    {
    }
}

internal sealed class FakeRecent : IRecentFilesStore
{
    private readonly List<string> _list;

    public FakeRecent(params string[] paths) => _list = new List<string>(paths);

    public bool FailRemove { get; init; }

    public IReadOnlyList<string> GetRecent() => _list;

    public void Add(string path)
    {
        _list.Remove(path);
        _list.Insert(0, path);
    }

    public void Remove(string path)
    {
        if (FailRemove)
        {
            throw new UnauthorizedAccessException("Zugriff verweigert.");
        }

        _list.Remove(path);
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

    public Task OpenMailAsync(string address) => Task.CompletedTask;

    public Task OpenPhoneAsync(string number) => Task.CompletedTask;

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

    public Task OpenMailAsync(string address) => Task.CompletedTask;

    public Task OpenPhoneAsync(string number) => Task.CompletedTask;

    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}

// Answers only TryReadState, from a fixed table, and counts the calls; a path in Throws stands in
// for a store that breaks its own null-on-failure contract.
internal sealed class StateProbeStore : IIncidentStore
{
    private readonly Dictionary<string, IncidentState> _states = new();

    public IncidentState this[string path]
    {
        set => _states[path] = value;
    }

    public HashSet<string> Throws { get; } = new();

    public int Probes { get; private set; }

    public void Save(string path, Incident incident)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Incident Load(string path) => throw new InvalidOperationException("Nicht geladen.");

    public IncidentState? TryReadState(string path)
    {
        Probes++;
        if (Throws.Contains(path))
        {
            throw new IOException("Datei gesperrt.");
        }

        return _states.TryGetValue(path, out var state) ? state : null;
    }

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Join(path, storageFileName);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }

    public event Action? SaveSucceeded
    {
        add { }
        remove { }
    }
}

// Every Load fails, standing in for a moved, truncated, or too-new file.
internal sealed class ThrowingStore : IIncidentStore
{
    private readonly string _message;

    public ThrowingStore(string message) => _message = message;

    public void Save(string path, Incident incident)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Incident Load(string path) => throw new InvalidOperationException(_message);

    public IncidentState? TryReadState(string path) => null;

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Join(path, storageFileName);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }

    public event Action? SaveSucceeded
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

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Incident Load(string path) =>
        _saved.TryGetValue(path, out var i) ? i : throw new InvalidOperationException("Datei kaputt.");

    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Join(path, storageFileName);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }

    public event Action? SaveSucceeded
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

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Incident Load(string path)
    {
        LoadCount++;
        return _saved[path];
    }

    // A passive peek, not a load — must not count against the load-once guard.
    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string ResolveFileDiskPath(string path, string storageFileName) => Path.Join(path, storageFileName);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }

    public event Action? SaveSucceeded
    {
        add { }
        remove { }
    }
}
