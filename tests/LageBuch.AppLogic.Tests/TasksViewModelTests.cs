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
        var session = LocalIncidentSession.StartNew(store, clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        return (session, clock, store);
    }

    private static MasterDataSet MasterData() => MasterDataSet.Empty with
    {
        RadioCallSigns = new[] { "FFB 1/44/1" },
        Roles = new[] { "EL" },
        Personnel = new[] { new Person("Mustermann", "Max", "ZF", null, null) },
    };

    [Fact]
    public void AddTask_clears_text_keeps_priorities_sticky_and_fires_onchanged()
    {
        var (session, _, _) = NewSession();
        var changedCount = 0;
        var vm = new TasksViewModel(session, new FixedClock(T0), new FakeTicker(),
            new FakeAlarmService(), MasterData(), () => changedCount++);

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
    public void AddTask_canExecute_requires_text_and_nonnegative_minutes()
    {
        var (session, clock, _) = NewSession();
        var vm = NewVm(session, clock);

        Assert.False(vm.AddTaskCommand.CanExecute(null));
        vm.NewText = "X";
        Assert.True(vm.AddTaskCommand.CanExecute(null));
        vm.NewTimerMinutes = 0;
        Assert.True(vm.AddTaskCommand.CanExecute(null)); // timer=0 is allowed (no due date)
        vm.NewTimerMinutes = -1;
        Assert.False(vm.AddTaskCommand.CanExecute(null)); // negative is rejected
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
        Assert.Equal(new[] { "high-important", "high-urgent", "old-low" },
            vm.Rows.Select(r => r.Text).ToArray());

        vm.Filter = TaskFilterKind.Done;
        Assert.Equal(new[] { "done-high", "done-old" }, vm.Rows.Select(r => r.Text).ToArray());

        vm.Filter = TaskFilterKind.All;
        Assert.Equal(new[] { "high-important", "high-urgent", "old-low", "done-high", "done-old" },
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
        Assert.StartsWith("noch", row.RemainingDisplay);

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
        LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        Assert.StartsWith("ERLEDIGT ·", done.CompletedDisplay);
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

    private static TasksViewModel NewVm(LocalIncidentSession session, FixedClock clock,
        FakeTicker? ticker = null, FakeAlarmService? alarm = null) =>
        new(session, clock, ticker ?? new FakeTicker(), alarm ?? new FakeAlarmService(),
            MasterData(), () => { });
}
