using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// What a global shortcut does to the workspace (#544). The key handling itself is the view's and
// is covered headless in GlobalShortcutTests; these pin the decisions behind it.
public class WorkspaceShortcutTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly KeyChord CtrlTab = new(ShortcutKey.Tab, Ctrl: true);
    private static readonly KeyChord CtrlShiftTab = new(ShortcutKey.Tab, Ctrl: true, Shift: true);
    private static readonly KeyChord CtrlN = new(ShortcutKey.N, Ctrl: true);
    private static readonly KeyChord F9 = new(ShortcutKey.F9);

    private static readonly ShortcutKey[] Digits =
    [
        ShortcutKey.D1, ShortcutKey.D2, ShortcutKey.D3, ShortcutKey.D4, ShortcutKey.D5,
        ShortcutKey.D6, ShortcutKey.D7, ShortcutKey.D8, ShortcutKey.D9, ShortcutKey.D0,
    ];

    private static KeyChord Ctrl(ShortcutKey digit) => new(digit, Ctrl: true);

    private static (IncidentWorkspaceViewModel Vm, FixedClock Clock, FakeTicker Ticker) Workspace(
        IReadOnlyList<NavEntry>? navigation = null)
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            new[] { ("A?", false) },
            new[] { ("B?", false) });
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            MasterDataSet.Empty with { Roles = new[] { new Role("EL") }, Navigation = navigation ?? Array.Empty<NavEntry>() },
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
        return (vm, clock, ticker);
    }

    private static string? Selected(IncidentWorkspaceViewModel vm) => vm.SelectedNavItem?.Header;

    private static void StartTrupp(IncidentWorkspaceViewModel vm)
    {
        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = "Muster";
        vm.Scba.NewTruppmann = "Beispiel";
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);
    }

    private static void AddTaskOnFiveMinuteTimer(IncidentWorkspaceViewModel vm)
    {
        vm.Tasks.NewText = "Wasserversorgung sicherstellen";
        vm.Tasks.NewTimerMinutes = 5;
        vm.Tasks.AddTaskCommand.Execute(null);
    }

    private static void Advance(FixedClock clock, FakeTicker ticker, int minutes)
    {
        clock.Now = clock.Now.AddMinutes(minutes);
        ticker.Fire();
    }

    [Fact]
    public void Ctrl_digit_opens_the_rail_entry_at_that_position()
    {
        // Every module is named, so no missing one is slotted in behind its default predecessor
        // and shifts the positions this test counts.
        var leading = new[] { NavModules.Leadership, NavModules.Etb, NavModules.Scba };
        var navigation = leading
            .Concat(NavModules.All.Except(leading))
            .Select(key => new NavEntry(key, null, true))
            .ToList();
        var (vm, _, _) = Workspace(navigation);
        var focusRequests = 0;
        vm.ModuleFocusRequested += (_, _) => focusRequests++;

        Assert.True(vm.TryRunShortcut(Ctrl(ShortcutKey.D1)));
        Assert.Equal("FÜHRUNG", Selected(vm));
        Assert.Equal(1, focusRequests);

        Assert.True(vm.TryRunShortcut(Ctrl(ShortcutKey.D3)));
        Assert.Equal("ATEMSCHUTZ", Selected(vm));
    }

    [Fact]
    public void Ctrl_4_opens_Fuehrung_in_the_place_funktionen_had()
    {
        var (vm, _, _) = Workspace();

        string[] expected =
        [
            "AUFBAU", "ETB", "AUFGABEN", "FÜHRUNG", "BETEILIGTE", "ATEMSCHUTZ", "CO-MESSUNG",
            "DATEIEN", "LINKS",
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(vm.TryRunShortcut(Ctrl(Digits[i])));
            Assert.Equal(expected[i], Selected(vm));
        }
    }

    [Fact]
    public void Rail_hints_number_the_shown_entries_in_order_without_a_gap_for_a_hidden_one()
    {
        // Every module is named, so none is slotted in behind its default predecessor and shifts
        // the positions this test counts.
        var leading = new[] { NavModules.Leadership, NavModules.Tasks, NavModules.Etb };
        var navigation = leading
            .Concat(NavModules.All.Except(leading))
            .Select(key => new NavEntry(key, null, key != NavModules.Tasks))
            .ToList();
        var (vm, _, _) = Workspace(navigation);

        // The Einsatz's own Checklisten follow the modules the layout names.
        Assert.Equal(
            Enumerable.Range(0, vm.NavItems.Count).Select(i => i < 10 ? $"Strg+{(i + 1) % 10}" : null),
            vm.NavItems.Select(item => item.ShortcutHint));
        Assert.Equal("Strg+2", vm.NavItems.Single(item => item.ModuleKey == NavModules.Etb).ShortcutHint);
        Assert.DoesNotContain(vm.NavItems, item => item.ModuleKey == NavModules.Tasks);
        Assert.True(vm.NavItems[^1].IsChecklist);
        Assert.True(vm.NavItems[^2].IsChecklist);
    }

    [Fact]
    public void A_digit_past_the_last_rail_entry_does_nothing()
    {
        var onlyEtb = NavModules.All.Select(key => new NavEntry(key, null, key == NavModules.Etb)).ToList();
        var (vm, _, _) = Workspace(onlyEtb);
        var before = Selected(vm);
        var beyond = Digits[vm.NavItems.Count];

        Assert.False(vm.TryRunShortcut(Ctrl(beyond)));
        Assert.Equal(before, Selected(vm));
    }

    [Fact]
    public void Ctrl_tab_walks_the_rail_as_shown_and_wraps_at_both_ends()
    {
        var (vm, _, _) = Workspace();
        vm.SelectedNavItem = vm.NavItems[^1];

        Assert.True(vm.TryRunShortcut(CtrlTab));
        Assert.Same(vm.NavItems[0], vm.SelectedNavItem);

        Assert.True(vm.TryRunShortcut(CtrlShiftTab));
        Assert.Same(vm.NavItems[^1], vm.SelectedNavItem);

        Assert.True(vm.TryRunShortcut(CtrlShiftTab));
        Assert.Same(vm.NavItems[^2], vm.SelectedNavItem);
    }

    [Fact]
    public void Ctrl_n_opens_the_etb_and_asks_for_its_first_field_even_from_inside_the_etb()
    {
        var (vm, _, _) = Workspace();
        vm.TryRunShortcut(Ctrl(ShortcutKey.D4));
        var requests = new List<ModuleFocusRequestedEventArgs>();
        vm.ModuleFocusRequested += (_, e) => requests.Add(e);

        Assert.True(vm.TryRunShortcut(CtrlN));

        Assert.Equal("ETB", Selected(vm));
        Assert.True(Assert.Single(requests).ToStartField);
    }

    [Fact]
    public void Ctrl_n_on_a_phone_opens_the_etb_composer()
    {
        var (vm, _, _) = Workspace();
        vm.IsNarrow = true;

        Assert.True(vm.TryRunShortcut(CtrlN));

        Assert.Equal("ETB", Selected(vm));
        Assert.True(vm.Etb.IsComposerOpen);
    }

    [Fact]
    public void No_shortcut_runs_while_the_workspace_shows_an_overlay()
    {
        var (vm, _, _) = Workspace();
        var before = Selected(vm);
        vm.CloseIncidentCommand.Execute(null);
        Assert.NotNull(vm.PendingConfirm);

        Assert.False(vm.TryRunShortcut(Ctrl(ShortcutKey.D4)));
        Assert.False(vm.TryRunShortcut(CtrlTab));
        Assert.False(vm.TryRunShortcut(CtrlN));
        Assert.Equal(before, Selected(vm));
    }

    [Fact]
    public void No_shortcut_runs_while_the_abschnitt_dialog_is_open()
    {
        var (vm, _, _) = Workspace();
        var before = Selected(vm);
        vm.Leadership.AddAbschnittCommand.Execute(null);
        Assert.NotNull(vm.PendingCommandNodeDialog);

        Assert.False(vm.TryRunShortcut(CtrlTab));
        Assert.Equal(before, Selected(vm));

        vm.PendingCommandNodeDialog!.CancelCommand.Execute(null);
        vm.PendingCommandNodeDialog?.ConfirmDiscardCommand.Execute(null);
        Assert.Null(vm.PendingCommandNodeDialog);
    }

    [Fact]
    public void No_shortcut_runs_while_the_person_dialog_is_open()
    {
        var (vm, _, _) = Workspace();
        var before = Selected(vm);
        vm.Leadership.AddFachberaterCommand.Execute(null);
        Assert.NotNull(vm.PendingLeadershipPersonDialog);

        Assert.False(vm.TryRunShortcut(CtrlTab));
        Assert.Equal(before, Selected(vm));

        vm.PendingLeadershipPersonDialog!.CancelCommand.Execute(null);
        Assert.Null(vm.PendingLeadershipPersonDialog);
    }

    [Fact]
    public void Without_an_open_warning_f9_does_nothing()
    {
        var (vm, _, _) = Workspace();

        Assert.Equal(WarningKind.None, vm.MostUrgentWarning);
        Assert.False(vm.TryRunShortcut(F9));
    }

    [Fact]
    public void A_rueckzugsalarm_comes_before_every_other_warning()
    {
        var (vm, clock, ticker) = Workspace();
        StartTrupp(vm);
        AddTaskOnFiveMinuteTimer(vm);

        // Past the 30-minute Einsatzzeit: Rückzug, a due Druckabfrage, the task and the ILS
        // Rückmeldung are all open at once.
        Advance(clock, ticker, 31);
        Assert.True(vm.Reminder!.IsDue);

        Assert.Equal(WarningKind.Retreat, vm.MostUrgentWarning);
        Assert.True(vm.TryRunShortcut(F9));
        Assert.Equal("ATEMSCHUTZ", Selected(vm));
        Assert.Same(vm.Scba.Trupps[0], vm.Scba.SelectedTrupp);
    }

    [Fact]
    public void A_due_druckabfrage_comes_before_a_due_task()
    {
        var (vm, clock, ticker) = Workspace();
        StartTrupp(vm);
        AddTaskOnFiveMinuteTimer(vm);

        // The Druckabfrage is due a third into the 30-minute Einsatzzeit.
        Advance(clock, ticker, 11);
        Assert.True(vm.Tasks.HasDueTask);

        Assert.Equal(WarningKind.PressureCheck, vm.MostUrgentWarning);
        Assert.True(vm.TryRunShortcut(F9));
        Assert.Equal("ATEMSCHUTZ", Selected(vm));
    }

    [Fact]
    public void A_due_task_comes_before_the_ils_rueckmeldung()
    {
        var (vm, clock, ticker) = Workspace();
        AddTaskOnFiveMinuteTimer(vm);
        Advance(clock, ticker, 16);
        Assert.True(vm.Reminder!.IsDue);

        Assert.Equal(WarningKind.TaskDue, vm.MostUrgentWarning);
        Assert.True(vm.TryRunShortcut(F9));
        Assert.Equal("AUFGABEN", Selected(vm));
    }

    [Fact]
    public void For_the_ils_rueckmeldung_f9_asks_the_view_to_focus_its_button_and_acknowledges_nothing()
    {
        var (vm, clock, ticker) = Workspace();
        Advance(clock, ticker, 16);
        var requests = 0;
        vm.ReminderFocusRequested += (_, _) => requests++;

        Assert.Equal(WarningKind.IlsReminder, vm.MostUrgentWarning);
        Assert.True(vm.TryRunShortcut(F9));

        Assert.Equal(1, requests);
        Assert.True(vm.Reminder!.IsDue);
    }

    [Fact]
    public void The_most_urgent_warning_is_announced_when_it_changes()
    {
        var (vm, clock, ticker) = Workspace();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Advance(clock, ticker, 16);

        Assert.Contains(nameof(IncidentWorkspaceViewModel.MostUrgentWarning), changes);
        Assert.True(vm.ShowsWarningHintOnReminder);
    }
}
