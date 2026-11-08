using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Tests;

public sealed class DispatchInboxViewModelTests : IDisposable
{
    private static readonly TimeZoneInfo Cet = TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "CET", "CET");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 17, 42, 0, TimeSpan.FromHours(1));
    private static readonly Vehicle Hlf = new("Wache 2", "Florian Musterstadt 2/40/1", 9, VehicleType: "HLF 20");

    private readonly string _folder = Directory.CreateTempSubdirectory("dispatch-vm-").FullName;
    private readonly FakeInbox _store;
    private readonly FakeTicker _ticker = new();

    public DispatchInboxViewModelTests() => _store = new FakeInbox { Folder = _folder };

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private DispatchInboxViewModel Inbox(FakeDialogs? dialogs = null) =>
        new(_store, new VehicleMasterData(), dialogs ?? new FakeDialogs(), new FixedClock(Now), _ticker, zone: Cet);

    private void Drop(string name = "alarm.html", string? content = null)
    {
        var path = Path.Join(_folder, name);
        File.WriteAllText(path, content ?? DispatchSamples.Html());
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-20).UtcDateTime);
    }

    [Fact]
    public void An_alarm_already_in_the_folder_is_offered_at_start()
    {
        Drop();

        var offer = Inbox().Offer;

        Assert.NotNull(offer);
        Assert.Equal("THL 2 T2917", offer.Title);
        Assert.Equal("Hauptstraße 12, 12345 Musterstadt Nord · alarmiert 17:17 · Einsatznummer " + DispatchSamples.IncidentNumber, offer.Summary);
    }

    [Fact]
    public void An_alarm_arriving_later_is_offered_on_the_next_scan()
    {
        var inbox = Inbox();
        Drop();

        for (var i = 1; i < DispatchInboxViewModel.TicksPerScan; i++)
        {
            _ticker.Fire();
        }

        Assert.Null(inbox.Offer);
        _ticker.Fire();
        Assert.NotNull(inbox.Offer);
    }

    [Fact]
    public void Only_units_naming_a_stammdaten_vehicle_start_ticked()
    {
        Drop();

        var units = Inbox().Offer!.Units;

        Assert.Equal(
            [("Florian Musterstadt Wache 2", false), ("Florian Musterstadt 1", false), ("Florian Musterstadt 2/40/1", true)],
            units.Select(u => (u.Name, u.IsSelected)));
        Assert.Equal("Wache 2 · HLF 20", units[2].Detail);
        Assert.Equal("nicht in den Stammdaten", units[0].Detail);
    }

    [Fact]
    public void The_import_carries_the_units_as_ticked_right_now()
    {
        Drop();
        var inbox = Inbox();
        inbox.Offer!.Units[0].IsSelected = true;
        inbox.Offer.Units[2].IsSelected = false;

        var import = inbox.CurrentImport();

        Assert.Equal(["Florian Musterstadt Wache 2"], import!.Units.Select(u => u.Name));
    }

    [Fact]
    public void A_rescan_finding_the_same_alarm_keeps_the_ticks()
    {
        Drop();
        var inbox = Inbox();
        var offer = inbox.Offer;
        offer!.Units[0].IsSelected = true;

        inbox.Scan();

        Assert.Same(offer, inbox.Offer);
    }

    [Fact]
    public void Dismissing_answers_the_alarm_for_good()
    {
        Drop();
        var inbox = Inbox();

        inbox.DismissCommand.Execute(null);
        inbox.Scan();

        Assert.Null(inbox.Offer);
        Assert.Null(Inbox().Offer);
    }

    [Fact]
    public void Accepting_asks_the_shell_and_answers_nothing_yet()
    {
        Drop();
        var inbox = Inbox();
        var asked = 0;
        inbox.CreateRequested = () => asked++;

        inbox.AcceptCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.NotNull(inbox.Offer);
        Assert.Empty(_store.Handled);
    }

    [Fact]
    public void A_taken_alarm_is_not_offered_again()
    {
        Drop();
        var inbox = Inbox();

        inbox.MarkTaken(inbox.Offer!.Alarm);
        inbox.Scan();

        Assert.Null(inbox.Offer);
    }

    [Fact]
    public async Task Picking_a_folder_remembers_it_and_looks_into_it()
    {
        _store.Folder = null;
        var inbox = Inbox(new FakeDialogs { FolderPath = _folder });
        Drop();
        Assert.Null(inbox.Offer);

        await inbox.PickFolderCommand.ExecuteAsync(null);

        Assert.Equal(_folder, _store.Folder);
        Assert.Equal(_folder, inbox.Folder);
        Assert.NotNull(inbox.Offer);
    }

    [Fact]
    public async Task A_cancelled_folder_picker_changes_nothing()
    {
        var inbox = Inbox(new FakeDialogs { FolderPath = null });

        await inbox.PickFolderCommand.ExecuteAsync(null);

        Assert.Equal(_folder, inbox.Folder);
    }

    [Fact]
    public void Clearing_the_folder_switches_alarm_import_off()
    {
        Drop();
        var inbox = Inbox();

        inbox.ClearFolderCommand.Execute(null);

        Assert.Null(_store.Folder);
        Assert.False(inbox.HasFolder);
        Assert.Null(inbox.Offer);
    }

    [Fact]
    public void A_folder_that_has_gone_is_reported_on_the_home_screen()
    {
        _store.Folder = Path.Join(_folder, "fehlt");

        Assert.StartsWith("Alarm-Ordner nicht lesbar", Inbox().Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Disposing_stops_the_polling()
    {
        var inbox = Inbox();

        inbox.Dispose();

        Assert.Equal(0, _ticker.SubscriberCount);
    }

    // The whole way through the Home screen: the alarm becomes an incident with its head data, and
    // is not offered again once the incident exists.
    [Fact]
    public void Taking_up_the_alarm_on_the_home_screen_creates_the_incident_from_it()
    {
        Drop();
        var dialogs = new FakeDialogs();
        var store = new FakeStore();
        var home = new HomeViewModel(store, new VehicleMasterData(), new FakeRecent(), dialogs, new FixedClock(Now), _ticker, new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0", dispatch: Inbox());
        IncidentWorkspaceViewModel? opened = null;
        home.WorkspaceOpened = ws => opened = ws;

        var import = home.Dispatch!.CurrentImport();
        home.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), import));

        Assert.NotNull(opened);
        Assert.Equal("20261108-1717 THL 2 T2917" + IncidentFileExtension.Current, dialogs.LastSuggestedSaveName);
        var incident = store.Load("/x.lagebuch");
        Assert.Equal(DispatchSamples.IncidentNumber, incident.IncidentNumber?.Value);
        Assert.Equal("THL 2 T2917", incident.Keyword);
        Assert.Equal("Hauptstraße", incident.Street);
        Assert.NotNull(incident.Location);
        Assert.Contains(incident.Journal, e => e.Direction == EtbDirection.Incoming && e.Text.StartsWith("Alarmierung 17:17", StringComparison.Ordinal));
        Assert.Equal(["Florian Musterstadt 2/40/1"], incident.Forces.Select(f => f.CallSign));
        Assert.Null(home.Dispatch.Offer);
        Assert.Single(_store.Handled);
    }

    [Fact]
    public void A_cancelled_save_picker_leaves_the_alarm_on_offer()
    {
        Drop();
        var home = new HomeViewModel(new FakeStore(), new VehicleMasterData(), new FakeRecent(), new FakeDialogs { SavePath = null }, new FixedClock(Now), _ticker, new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0", dispatch: Inbox());

        home.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator("Müller"), home.Dispatch!.CurrentImport()));

        Assert.NotNull(home.Dispatch.Offer);
        Assert.Empty(_store.Handled);
    }

    [Fact]
    public void Without_alarm_import_wired_the_home_screen_has_none()
    {
        var home = new HomeViewModel(new FakeStore(), new VehicleMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(Now), _ticker, new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0");

        Assert.Null(home.Dispatch);
    }

    private (MainWindowViewModel Shell, FakeStore Store) Shell()
    {
        var store = new FakeStore();
        var home = new HomeViewModel(store, new VehicleMasterData(), new FakeRecent(), new FakeDialogs(), new FixedClock(Now), _ticker, new FakeAlarmService(), new NoopIncidentHostController(), "1.0.0", dispatch: Inbox());
        return (new MainWindowViewModel(home, new MasterDataEditorViewModel(new VehicleMasterData(), new FakeDialogs(), new NoFiles()), new FakeDialogs(), "0.1.0"), store);
    }

    private static void ConfirmOperator(MainWindowViewModel shell)
    {
        shell.PendingPrompt!.OperatorName = "Müller";
        shell.PendingPrompt.ConfirmCommand.Execute(null);
        shell.ConfirmOperatorCommand.Execute(null);
    }

    [Fact]
    public void Einsatz_anlegen_asks_for_the_lagebuchfuehrer_and_opens_the_incident_from_the_alarm()
    {
        Drop();
        var (shell, store) = Shell();
        var home = Assert.IsType<HomeViewModel>(shell.CurrentView);

        home.Dispatch!.AcceptCommand.Execute(null);
        Assert.NotNull(shell.PendingPrompt);
        ConfirmOperator(shell);

        Assert.IsType<IncidentWorkspaceViewModel>(shell.CurrentView);
        Assert.Equal(DispatchSamples.IncidentNumber, store.Load("/x.lagebuch").IncidentNumber?.Value);
    }

    [Fact]
    public void A_plain_new_incident_ignores_the_alarm_on_offer()
    {
        Drop();
        var (shell, store) = Shell();

        shell.RequestNewIncidentCommand.Execute(null);
        ConfirmOperator(shell);

        Assert.Null(store.Load("/x.lagebuch").IncidentNumber);
        Assert.Empty(_store.Handled);
    }

    [Fact]
    public void An_alarm_arriving_during_an_einsatz_is_only_a_notice_in_the_shell()
    {
        var (shell, _) = Shell();
        shell.RequestNewIncidentCommand.Execute(null);
        ConfirmOperator(shell);
        Assert.Null(shell.DispatchNotice);

        Drop();
        for (var i = 0; i < DispatchInboxViewModel.TicksPerScan; i++)
        {
            _ticker.Fire();
        }

        Assert.IsType<IncidentWorkspaceViewModel>(shell.CurrentView);
        Assert.Equal("Neuer Alarm: THL 2 T2917 – auf der Startseite anlegen", shell.DispatchNotice);
    }

    // Production scans on a pool thread and posts the result back; nothing is offered before the post.
    [Fact]
    public void The_scan_runs_in_the_background_and_its_result_is_posted_to_the_ui()
    {
        Drop();
        var background = new Queue<Action>();
        var ui = new QueueingDispatcher();
        var inbox = new DispatchInboxViewModel(_store, new VehicleMasterData(), new FakeDialogs(), new FixedClock(Now), _ticker, ui, background.Enqueue, Cet);

        Assert.Null(inbox.Offer);
        background.Dequeue()();
        Assert.Null(inbox.Offer);
        ui.RunAll();
        Assert.NotNull(inbox.Offer);
    }

    [Fact]
    public void A_store_that_cannot_be_read_is_reported_not_thrown()
    {
        _store.Failure = new IOException("Datenträger voll");

        Assert.Equal("Alarm-Ordner: Datenträger voll", Inbox().Error);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_saved_is_reported_and_not_taken()
    {
        var inbox = Inbox(new FakeDialogs { FolderPath = "/neu" });
        _store.Failure = new UnauthorizedAccessException("Zugriff verweigert");

        await inbox.PickFolderCommand.ExecuteAsync(null);

        Assert.Equal(_folder, inbox.Folder);
        Assert.Equal("Alarm-Ordner nicht gespeichert: Zugriff verweigert", inbox.Error);
    }

    [Fact]
    public void An_answer_that_cannot_be_saved_is_reported()
    {
        Drop();
        var inbox = Inbox();
        _store.Failure = new IOException("Datenträger voll");

        inbox.DismissCommand.Execute(null);

        Assert.Null(inbox.Offer);
        Assert.Equal("Alarm nicht als erledigt gespeichert: Datenträger voll", inbox.Error);
    }

    [Fact]
    public void An_alarm_without_stichwort_or_street_still_reads_cleanly()
    {
        var xml = DispatchSamples.Xml(street: " ", requested: " ", vehicles: " ").Replace("[ THL 2 T2917 ]", "[ ]", StringComparison.Ordinal).Replace("[ THL 2 VU eCall ]", "[ ]", StringComparison.Ordinal);
        Drop(content: xml);

        var offer = Inbox().Offer!;

        Assert.Equal("Alarm", offer.Title);
        Assert.Equal("12345 Musterstadt Nord · alarmiert 17:17 · Einsatznummer " + DispatchSamples.IncidentNumber, offer.Summary);
        Assert.False(offer.HasUnits);
    }

    [Fact]
    public void An_alarm_without_any_address_starts_with_its_time()
    {
        Drop(content: DispatchSamples.Xml(street: " ", place: " "));

        Assert.StartsWith("alarmiert 17:17 · ", Inbox().Offer!.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_announces_the_notice_when_an_alarm_comes_in_during_an_einsatz()
    {
        var (shell, _) = Shell();
        shell.RequestNewIncidentCommand.Execute(null);
        ConfirmOperator(shell);
        var changes = new List<string?>();
        shell.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Drop();
        for (var i = 0; i < DispatchInboxViewModel.TicksPerScan; i++)
        {
            _ticker.Fire();
        }

        Assert.Contains(nameof(MainWindowViewModel.DispatchNotice), changes);
    }

    private sealed class QueueingDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _posted = new();

        public void Post(Action action) => _posted.Enqueue(action);

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());

        public void RunAll()
        {
            while (_posted.Count > 0)
            {
                _posted.Dequeue()();
            }
        }
    }

    private sealed class VehicleMasterData : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with { Vehicles = [Hlf] };

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class FakeInbox : IDispatchInboxStore
    {
        public string? Folder { get; set; }

        public Dictionary<string, DateTimeOffset> Handled { get; } = new(StringComparer.Ordinal);

        /// <summary>When set, every read and write after the folder throws it, like a full or locked disk.</summary>
        public Exception? Failure { get; set; }

        public string? GetFolder() => Folder;

        public void SetFolder(string? folder) => Folder = Failure is null ? folder : throw Failure;

        public IReadOnlySet<string> GetHandled() => Failure is null ? Handled.Keys.ToHashSet(StringComparer.Ordinal) : throw Failure;

        public void MarkHandled(string key, DateTimeOffset at) => Handled[key] = Failure is null ? at : throw Failure;
    }
}
