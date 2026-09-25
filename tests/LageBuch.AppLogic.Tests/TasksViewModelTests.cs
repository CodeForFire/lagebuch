using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class TasksViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, FixedClock Clock, FakeStore Store) NewSession()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (session, clock, store);
    }

    private static MasterDataSet MasterData() => MasterDataSet.Empty with
    {
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/44/1", 6) },
        Roles = new[] { "EL" },
        Personnel = new[] { new Person("Mustermann", "Max", "ZF", null, null) },
    };

    [Fact]
    public void AddTask_clears_text_keeps_priorities_sticky_and_fires_onchanged()
    {
        var (session, _, _) = NewSession();
        var changedCount = 0;
        var vm = new TasksViewModel(
            session,
            new FixedClock(T0),
            new FakeTicker(),
            new FakeAlarmService(),
            MasterData(),
            () => changedCount++);

        vm.NewText = "Tür sichern";
        vm.NewAssignee = "FFB 1/44/1";
        vm.NewUrgency = TaskUrgency.High;               // resets the minutes field to the default 5
        Assert.Equal(5, vm.NewTimerMinutes);

        vm.AddTaskCommand.Execute(null);

        var task = Assert.Single(session.Incident.Tasks);
        Assert.Equal("Tür sichern", task.Text);
        Assert.Equal(T0.AddMinutes(5), task.DueAt);     // High -> 5 minutes
        Assert.Equal(string.Empty, vm.NewText);         // text cleared for rapid follow-ups ...
        Assert.Equal("FFB 1/44/1", vm.NewAssignee);     // ... priorities stay sticky
        Assert.True(changedCount >= 1);                 // workspace hook fired
    }

    [Fact]
    public void Changing_urgency_resets_minutes_but_override_survives_until_next_change()
    {
        var (session, clock, _) = NewSession();
        var vm = NewVm(session, clock);

        vm.NewUrgency = TaskUrgency.Low;
        Assert.Equal(30, vm.NewTimerMinutes);
        vm.NewTimerMinutes = 7;                         // operator override
        vm.NewUrgency = TaskUrgency.Medium;             // changing urgency re-applies its default
        Assert.Equal(15, vm.NewTimerMinutes);
    }

    [Fact]
    public void AddTask_names_the_empty_text_rather_than_going_grey()
    {
        var (session, clock, _) = NewSession();
        var vm = NewVm(session, clock);

        Assert.True(vm.AddTaskCommand.CanExecute(null)); // the press is the question (#412)
        vm.AddTaskCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.NewTextError);
        Assert.Empty(session.Incident.Tasks);

        vm.NewText = "X";
        Assert.Null(vm.NewTextError); // fixed as it is typed, without a second press
        vm.AddTaskCommand.Execute(null);
        Assert.Single(session.Incident.Tasks);
    }

    [Fact]
    public void AddTask_accepts_a_zero_timer_and_names_a_negative_one()
    {
        var (session, clock, _) = NewSession();
        var vm = NewVm(session, clock);
        vm.NewText = "X";

        vm.NewTimerMinutes = 0;
        vm.AddTaskCommand.Execute(null);
        Assert.Single(session.Incident.Tasks); // timer=0 is allowed (no due date)
        Assert.Null(vm.NewTimerMinutesError);

        vm.NewText = "Y";
        vm.NewTimerMinutes = -1;
        vm.AddTaskCommand.Execute(null);

        Assert.Equal(ValidationMessages.TimerMinutes, vm.NewTimerMinutesError);
        Assert.Single(session.Incident.Tasks); // the negative one was not added
    }

    [Fact]
    public void A_added_task_leaves_the_dock_quiet_for_the_next_one()
    {
        var (session, clock, _) = NewSession();
        var vm = NewVm(session, clock);
        vm.AddTaskCommand.Execute(null); // provokes the message
        vm.NewText = "Erster Auftrag";

        vm.AddTaskCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewText); // cleared for rapid follow-up entries
        Assert.Null(vm.NewTextError); // ...and that clearing must not read as a fresh complaint
    }

    [Fact]
    public void Rows_sort_open_before_done_then_urgency_importance_age()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("old-low", null, TaskImportance.Low, TaskUrgency.Low, 30);          // A
        session.AddTask("high-urgent", null, TaskImportance.Low, TaskUrgency.High, 5);      // B
        session.AddTask("high-important", null, TaskImportance.High, TaskUrgency.High, 5);  // C
        session.AddTask("done-high", null, TaskImportance.High, TaskUrgency.High, 5);
        session.SetTaskCompleted(session.Incident.Tasks[3].Id, true);
        session.AddTask("done-old", null, TaskImportance.High, TaskUrgency.High, 5);
        session.SetTaskCompleted(session.Incident.Tasks[4].Id, true);

        var vm = NewVm(session, clock);

        // Open: urgency desc -> importance desc -> age asc; done hidden by the OFFEN default filter.
        Assert.Equal(
            new[] { "high-important", "high-urgent", "old-low" },
            vm.Rows.Select(r => r.Text).ToArray());

        vm.Filter = TaskFilterKind.Done;
        Assert.Equal(new[] { "done-high", "done-old" }, vm.Rows.Select(r => r.Text).ToArray());

        vm.Filter = TaskFilterKind.All;
        Assert.Equal(
            new[] { "high-important", "high-urgent", "old-low", "done-high", "done-old" },
            vm.Rows.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void Overdue_state_does_not_reorder_rows()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("low-long", null, TaskImportance.Low, TaskUrgency.Low, 30);     // sorts last
        session.AddTask("high-short", null, TaskImportance.High, TaskUrgency.High, 5);  // sorts first
        var vm = NewVm(session, clock, ticker);

        clock.Now = T0.AddMinutes(6);   // high-short is now overdue — but keeps its position
        ticker.Fire();

        Assert.Equal(new[] { "high-short", "low-long" }, vm.Rows.Select(r => r.Text).ToArray());
        Assert.True(vm.Rows[0].IsOverdue);
        Assert.False(vm.Rows[1].IsOverdue);
    }

    [Fact]
    public void Checking_off_writes_back_without_extra_echo_saves()
    {
        var (session, clock, store) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 30);
        var vm = NewVm(session, clock);
        vm.Filter = TaskFilterKind.All; // the toggle survives Sync only while done rows stay visible
        var savesAfterBuild = store.SaveCount;

        vm.Rows.Single().IsDone = true;

        Assert.True(session.Incident.Tasks[0].IsCompleted);
        Assert.True(store.SaveCount > savesAfterBuild); // a real user toggle persists

        // Echo-guard: rebuild pulls state (as a remote broadcast would) without writing back.
        var beforePull = store.SaveCount;
        vm.Sync();
        Assert.Equal(beforePull, store.SaveCount);
        Assert.True(vm.Rows.Single().IsDone);
    }

    [Fact]
    public void Unchecking_keeps_the_original_due_at()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 30);
        var dueBefore = session.Incident.Tasks[0].DueAt;
        session.SetTaskCompleted(session.Incident.Tasks[0].Id, true);
        var vm = NewVm(session, clock);
        vm.Filter = TaskFilterKind.All; // a completed task is reached for un-checking via ALLE/ERLEDIGT

        vm.Rows.Single().IsDone = false;

        Assert.False(session.Incident.Tasks[0].IsCompleted);
        Assert.Equal(dueBefore, session.Incident.Tasks[0].DueAt);
    }

    [Fact]
    public void Ticker_refreshes_countdown_and_plays_due_alarm_exactly_once()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        session.AddTask("schnell", null, TaskImportance.High, TaskUrgency.High, 5);
        var vm = NewVm(session, clock, ticker, alarm);
        var row = vm.Rows.Single();

        clock.Now = T0.AddMinutes(4);
        ticker.Fire();
        Assert.False(row.IsOverdue);
        Assert.StartsWith("noch", row.RemainingDisplay, StringComparison.Ordinal);

        clock.Now = T0.AddMinutes(5).AddSeconds(1);
        ticker.Fire();

        Assert.True(row.IsOverdue);
        Assert.Equal("FÄLLIG", row.RemainingDisplay);
        Assert.Single(alarm.Played);                     // exactly once ...
        Assert.Equal(AlarmSound.TaskDue, alarm.Played[0]);

        ticker.Fire();                                   // ... not again on subsequent ticks
        Assert.Single(alarm.Played);
    }

    [Fact]
    public void Completed_rows_stop_being_overdue_and_show_a_dash()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock);
        vm.Filter = TaskFilterKind.All; // the dash display is only visible once the row stays listed
        clock.Now = T0.AddMinutes(6);
        vm.Rows.Single().RefreshClock(clock.Now);
        Assert.True(vm.Rows.Single().IsOverdue);

        session.SetTaskCompleted(session.Incident.Tasks[0].Id, true);

        Assert.False(vm.Rows.Single().IsOverdue);
        Assert.Equal("–", vm.Rows.Single().RemainingDisplay);
    }

    [Fact]
    public void Task_without_timer_shows_dash_and_is_never_overdue()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 0);
        var vm = NewVm(session, clock);
        vm.Filter = TaskFilterKind.All;

        Assert.Equal("–", vm.Rows.Single().RemainingDisplay);
        Assert.False(vm.Rows.Single().IsOverdue);

        clock.Now = T0.AddMinutes(999);
        vm.Rows.Single().RefreshClock(clock.Now);

        Assert.Equal("–", vm.Rows.Single().RemainingDisplay);
        Assert.False(vm.Rows.Single().IsOverdue);
    }

    [Fact]
    public void Readonly_session_disables_the_dock()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        var vm = new TasksViewModel(ro, clock, new FakeTicker(), new FakeAlarmService(), MasterData(), () => { });

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddTaskCommand.CanExecute(null));
    }

    [Fact]
    public void Completed_rows_carry_an_erledigt_stamp_open_rows_none()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 30);
        var vm = NewVm(session, clock);
        vm.Filter = TaskFilterKind.All; // the toggle survives Sync only while done rows stay visible

        Assert.Equal(string.Empty, vm.Rows.Single().CompletedDisplay); // open: no stamp

        vm.Rows.Single().IsDone = true;
        var done = vm.Rows.Single(); // recreated by the write-back's Sync

        Assert.True(done.IsDone);
        Assert.StartsWith("ERLEDIGT ·", done.CompletedDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Radio_bools_write_through_to_the_filter_and_false_is_a_noop()
    {
        var (session, clock, _) = NewSession();
        var vm = NewVm(session, clock);

        Assert.True(vm.IsOpenFilter);   // default OFFEN

        vm.IsDoneFilter = true;         // TwoWay radio binding write
        Assert.True(vm.IsDoneFilter);
        Assert.False(vm.IsOpenFilter);
        Assert.Equal(TaskFilterKind.Done, vm.Filter);

        vm.IsOpenFilter = false;        // binding engines may write back unchanged values
        Assert.Equal(TaskFilterKind.Done, vm.Filter); // ... that must not flip the filter
    }

    // ----- #460: the Aufgabe-fällig bar names the task and leads to it -----
    [Fact]
    public void Task_rows_carry_their_task_id()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);

        var vm = NewVm(session, clock);

        Assert.Equal(session.Incident.Tasks[0].Id, vm.Rows.Single().Id);
    }

    [Fact]
    public void Due_bar_is_hidden_while_no_task_is_overdue()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("mit Timer", null, TaskImportance.Low, TaskUrgency.Low, 5);
        session.AddTask("ohne Timer", null, TaskImportance.Low, TaskUrgency.Low, 0);
        var vm = NewVm(session, clock, ticker);

        clock.Now = T0.AddMinutes(4);
        ticker.Fire();

        Assert.False(vm.HasDueTask);
        Assert.Equal("—", vm.DueTaskDisplay);
    }

    [Fact]
    public void Due_bar_names_the_most_overdue_task_and_its_assignee()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("Später fällig", "EL", TaskImportance.High, TaskUrgency.High, 10);
        session.AddTask("Wasserversorgung prüfen", "FFB 1/44/1", TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        clock.Now = T0.AddMinutes(6);
        ticker.Fire();

        Assert.True(vm.HasDueTask);
        Assert.Equal("Aufgabe fällig: Wasserversorgung prüfen (zugeteilt an FFB 1/44/1)", vm.DueTaskDisplay);
        Assert.Contains(nameof(TasksViewModel.HasDueTask), changed);
        Assert.Contains(nameof(TasksViewModel.DueTaskDisplay), changed);
    }

    [Fact]
    public void Due_bar_leaves_out_zugeteilt_when_nobody_is_assigned()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("Lage melden", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);

        clock.Now = T0.AddMinutes(6);
        ticker.Fire();

        Assert.Equal("Aufgabe fällig: Lage melden", vm.DueTaskDisplay);
    }

    [Fact]
    public void Due_bar_counts_the_other_overdue_tasks()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("Erste", null, TaskImportance.Low, TaskUrgency.Low, 5);
        session.AddTask("Zweite", null, TaskImportance.Low, TaskUrgency.Low, 6);
        session.AddTask("Dritte", null, TaskImportance.Low, TaskUrgency.Low, 7);
        var vm = NewVm(session, clock, ticker);

        clock.Now = T0.AddMinutes(8);
        ticker.Fire();

        Assert.Equal("Aufgabe fällig: Erste · +2 weitere", vm.DueTaskDisplay);
    }

    [Fact]
    public void Due_bar_clears_when_the_task_is_marked_done()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);
        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        Assert.True(vm.HasDueTask);

        vm.Rows.Single().IsDone = true;

        Assert.False(vm.HasDueTask);
    }

    [Fact]
    public void Completing_from_the_bar_marks_the_most_overdue_task_done()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("Erste", null, TaskImportance.Low, TaskUrgency.Low, 5);
        session.AddTask("Zweite", null, TaskImportance.Low, TaskUrgency.Low, 6);
        var vm = NewVm(session, clock, ticker);
        clock.Now = T0.AddMinutes(8);
        ticker.Fire();

        vm.CompleteMostOverdueTaskCommand.Execute(null);

        Assert.True(session.Incident.Tasks.Single(t => t.Text == "Erste").IsCompleted);
        Assert.False(session.Incident.Tasks.Single(t => t.Text == "Zweite").IsCompleted);
        Assert.Equal("Aufgabe fällig: Zweite", vm.DueTaskDisplay);
    }

    [Fact]
    public void ShowMostOverdueTask_selects_the_task_and_asks_to_reveal_it()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("Später fällig", null, TaskImportance.High, TaskUrgency.High, 10);
        session.AddTask("Überfällig", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);
        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        var reveals = 0;
        vm.RevealRequested += (_, _) => reveals++;

        vm.ShowMostOverdueTaskCommand.Execute(null);

        Assert.Equal("Überfällig", vm.SelectedTask?.Text);
        Assert.Contains(vm.SelectedTask, vm.Rows);
        Assert.Equal(1, reveals);
    }

    [Fact]
    public void Showing_the_same_task_twice_asks_to_reveal_it_again()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);
        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        var reveals = 0;
        vm.RevealRequested += (_, _) => reveals++;

        vm.ShowMostOverdueTaskCommand.Execute(null);
        vm.ShowMostOverdueTaskCommand.Execute(null);

        Assert.Equal(2, reveals);
    }

    [Fact]
    public void Showing_a_task_when_none_is_due_selects_nothing_and_reveals_nothing()
    {
        var (session, clock, _) = NewSession();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock);
        var reveals = 0;
        vm.RevealRequested += (_, _) => reveals++;

        vm.ShowMostOverdueTaskCommand.Execute(null);

        Assert.Null(vm.SelectedTask);
        Assert.Equal(0, reveals);
    }

    [Fact]
    public void Showing_a_task_hidden_by_the_done_filter_switches_back_to_open()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);
        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        vm.Filter = TaskFilterKind.Done;

        vm.ShowMostOverdueTaskCommand.Execute(null);

        Assert.Equal(TaskFilterKind.Open, vm.Filter);
        Assert.Equal("X", vm.SelectedTask?.Text);
        Assert.Contains(vm.SelectedTask, vm.Rows);
    }

    [Fact]
    public void The_selected_task_survives_an_incident_wide_change()
    {
        var (session, clock, _) = NewSession();
        var ticker = new FakeTicker();
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var vm = NewVm(session, clock, ticker);
        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        vm.ShowMostOverdueTaskCommand.Execute(null);

        session.AddTask("Neu", null, TaskImportance.Low, TaskUrgency.Low, 30); // Sync() rebuilds rows

        Assert.Equal("X", vm.SelectedTask?.Text);
        Assert.Contains(vm.SelectedTask, vm.Rows);
    }

    [Fact]
    public void Due_bar_is_hidden_in_a_read_only_workspace()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var live = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        live.AddTask("Liegengeblieben", null, TaskImportance.Low, TaskUrgency.Low, 5);
        clock.Now = T0.AddHours(3);
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        var vm = new TasksViewModel(ro, clock, new FakeTicker(), new FakeAlarmService(), MasterData(), () => { });

        Assert.False(vm.HasDueTask);
    }

    private static TasksViewModel NewVm(
        LocalIncidentSession session,
        FixedClock clock,
        FakeTicker? ticker = null,
        FakeAlarmService? alarm = null) =>
        new(
            session,
            clock,
            ticker ?? new FakeTicker(),
            alarm ?? new FakeAlarmService(),
            MasterData(),
            () => { });
}
