using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// #246: an Aufgabe is corrected in a panel below the grid, never in the grid's cells, so the grid
// keeps showing Wichtigkeit and Dringlichkeit in their colours while a task is being edited.
public class TaskEditingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, FixedClock Clock) NewSession()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (session, clock);
    }

    private static TasksViewModel NewVm(
        LocalIncidentSession session,
        FixedClock clock,
        Action? onChanged = null,
        FakeTicker? ticker = null,
        FakeAlarmService? alarm = null) =>
        new(
            session,
            clock,
            ticker ?? new FakeTicker(),
            alarm ?? new FakeAlarmService(),
            MasterDataSet.Empty,
            onChanged ?? (() => { }));

    private static (LocalIncidentSession Session, FixedClock Clock, TasksViewModel Vm) WithOneTask(Action? onChanged = null)
    {
        var (session, clock) = NewSession();
        session.AddTask("Presse-Info vorbereiten", "FFB 1/44/1", TaskImportance.Low, TaskUrgency.Medium, 15);
        return (session, clock, NewVm(session, clock, onChanged));
    }

    [Fact]
    public void Beginning_an_edit_fills_the_panel_from_the_row()
    {
        var (_, _, vm) = WithOneTask();
        var row = vm.Rows.Single();

        row.BeginEditCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.Same(row, vm.EditingTask);
        Assert.Same(row, vm.SelectedTask);
        Assert.Equal("Presse-Info vorbereiten", vm.EditText);
        Assert.Equal("FFB 1/44/1", vm.EditAssignee);
        Assert.Equal(TaskImportance.Low, vm.EditImportance);
        Assert.Equal(TaskUrgency.Medium, vm.EditUrgency);
        Assert.Null(vm.EditErrorSummary);
    }

    [Fact]
    public void Saving_an_edit_writes_all_four_fields_and_closes_the_panel()
    {
        var changes = 0;
        var (session, _, vm) = WithOneTask(() => changes++);
        var row = vm.Rows.Single();
        row.BeginEditCommand.Execute(null);

        vm.EditText = "Presse-Info an Pressesprecher";
        vm.EditAssignee = "Mustermann, Max";
        vm.EditImportance = TaskImportance.High;
        vm.EditUrgency = TaskUrgency.High;
        vm.SaveEditCommand.Execute(null);

        var task = Assert.Single(session.Incident.Tasks);
        Assert.Equal("Presse-Info an Pressesprecher", task.Text);
        Assert.Equal("Mustermann, Max", task.Assignee);
        Assert.Equal(TaskImportance.High, task.Importance);
        Assert.Equal(TaskUrgency.High, task.Urgency);
        Assert.False(vm.IsEditing);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_saved_edit_shows_in_the_kept_row_with_its_new_colour()
    {
        var (_, _, vm) = WithOneTask();
        var row = vm.Rows.Single();
        row.BeginEditCommand.Execute(null);

        vm.EditImportance = TaskImportance.High;
        vm.EditText = "Presse-Info sofort";
        vm.SaveEditCommand.Execute(null);

        Assert.Same(row, vm.Rows.Single());
        Assert.Equal("Presse-Info sofort", row.Text);
        Assert.Equal("Hoch", row.ImportanceLabel);
        Assert.True(row.IsImportanceHigh);
        Assert.False(row.IsImportanceLow);
    }

    [Fact]
    public void Saving_with_blank_text_names_the_field_and_keeps_the_panel_open()
    {
        var changes = 0;
        var (session, _, vm) = WithOneTask(() => changes++);
        vm.Rows.Single().BeginEditCommand.Execute(null);

        vm.EditText = "   ";
        vm.SaveEditCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.NotNull(vm.EditTextError);
        Assert.NotNull(vm.EditErrorSummary);
        Assert.Equal("Presse-Info vorbereiten", session.Incident.Tasks.Single().Text);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Cancelling_an_edit_changes_nothing()
    {
        var (session, _, vm) = WithOneTask();
        vm.Rows.Single().BeginEditCommand.Execute(null);

        vm.EditText = "verworfen";
        vm.EditImportance = TaskImportance.High;
        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Null(vm.EditingTask);
        Assert.Equal("Presse-Info vorbereiten", session.Incident.Tasks.Single().Text);
        Assert.Equal(TaskImportance.Low, session.Incident.Tasks.Single().Importance);
    }

    [Fact]
    public void Saving_without_a_change_sends_nothing()
    {
        var changes = 0;
        var (session, _, vm) = WithOneTask(() => changes++);
        var sessionChanges = 0;
        session.Changed += () => sessionChanges++;
        vm.Rows.Single().BeginEditCommand.Execute(null);

        vm.SaveEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal(0, sessionChanges);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_done_task_cannot_be_edited()
    {
        var (session, _, vm) = WithOneTask();
        vm.Filter = TaskFilterKind.All;
        var row = vm.Rows.Single();

        session.SetTaskCompleted(row.Id, true);

        Assert.False(row.BeginEditCommand.CanExecute(null));
    }

    [Fact]
    public void A_read_only_workspace_offers_no_edit()
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
        live.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        using var vm = NewVm(ro, clock);

        Assert.False(vm.Rows.Single().BeginEditCommand.CanExecute(null));
    }

    [Fact]
    public void The_input_dock_steps_aside_while_editing()
    {
        var (_, _, vm) = WithOneTask();
        Assert.True(vm.ShowComposer);

        vm.Rows.Single().BeginEditCommand.Execute(null);
        Assert.False(vm.ShowComposer);

        vm.CancelEditCommand.Execute(null);
        Assert.True(vm.ShowComposer);
    }

    [Fact]
    public void On_a_phone_the_add_button_hides_while_editing()
    {
        var (_, _, vm) = WithOneTask();
        vm.IsNarrow = true;
        Assert.True(vm.ShowComposerButton);

        vm.Rows.Single().BeginEditCommand.Execute(null);

        Assert.False(vm.ShowComposerButton);
        Assert.False(vm.ShowComposer);
    }

    [Fact]
    public void A_task_completed_elsewhere_closes_its_edit_panel()
    {
        var (session, _, vm) = WithOneTask();
        var row = vm.Rows.Single();
        row.BeginEditCommand.Execute(null);

        session.SetTaskCompleted(row.Id, true); // another device ticks it off

        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void An_unrelated_change_keeps_the_panel_and_what_was_typed()
    {
        var (session, _, vm) = WithOneTask();
        vm.Rows.Single().BeginEditCommand.Execute(null);
        vm.EditText = "halb getippt";

        session.AddTask("Andere", null, TaskImportance.Low, TaskUrgency.Low, 5);

        Assert.True(vm.IsEditing);
        Assert.Equal("halb getippt", vm.EditText);
    }

    [Fact]
    public void Plus_five_in_the_panel_extends_the_timer_at_once()
    {
        var (session, _, vm) = WithOneTask();
        var row = vm.Rows.Single();
        var dueBefore = session.Incident.Tasks.Single().DueAt;
        row.BeginEditCommand.Execute(null);

        vm.ExtendEditingTimerCommand.Execute(null);

        Assert.Equal(dueBefore.AddMinutes(5), session.Incident.Tasks.Single().DueAt);
        Assert.Equal("noch 20:00", row.RemainingDisplay);
        Assert.True(vm.IsEditing); // an action, not a field: the panel stays open
    }

    [Fact]
    public void Plus_five_is_off_for_a_task_without_a_timer()
    {
        var (session, clock) = NewSession();
        session.AddTask("Ohne Timer", null, TaskImportance.Low, TaskUrgency.Low, 0);
        using var vm = NewVm(session, clock);
        vm.Rows.Single().BeginEditCommand.Execute(null);

        Assert.False(vm.ExtendEditingTimerCommand.CanExecute(null));
    }

    [Fact]
    public void Plus_five_in_the_due_bar_puts_the_task_off_from_now()
    {
        var changes = 0;
        var (session, clock, vm) = WithOneTask(() => changes++);
        clock.Now = T0.AddMinutes(20); // five minutes overdue
        vm.Sync();
        Assert.True(vm.HasDueTask);

        vm.ExtendMostOverdueTaskCommand.Execute(null);

        Assert.Equal(T0.AddMinutes(25), session.Incident.Tasks.Single().DueAt);
        Assert.False(vm.HasDueTask);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_put_off_task_alarms_again_when_it_falls_due_again()
    {
        var (session, clock) = NewSession();
        session.AddTask("Rückruf", null, TaskImportance.High, TaskUrgency.High, 5);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        using var vm = NewVm(session, clock, ticker: ticker, alarm: alarm);

        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        vm.ExtendMostOverdueTaskCommand.Execute(null);
        ticker.Fire();
        clock.Now = T0.AddMinutes(12);
        ticker.Fire();

        Assert.Equal(2, alarm.Played.Count);
    }

    [Fact]
    public void Priority_options_run_from_low_to_high_and_carry_their_colour()
    {
        var (_, _, vm) = WithOneTask();

        Assert.Equal(new[] { "Niedrig", "Mittel", "Hoch" }, vm.ImportanceOptions.Select(o => o.Label));
        Assert.Equal(new[] { "Niedrig", "Mittel", "Hoch" }, vm.UrgencyOptions.Select(o => o.Label));
        Assert.True(vm.ImportanceOptions[0].IsLow);
        Assert.True(vm.UrgencyOptions[1].IsMedium);
        Assert.True(vm.ImportanceOptions[2].IsHigh);
    }
}
