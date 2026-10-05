using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// Rückgängig after a toggle (#543): one stray Space while a grid has focus must not close an
/// Aufgabe without anyone noticing, so ticking one off offers a few seconds to take it back.
/// </summary>
public class UndoNoticeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static TasksViewModel Tasks(LocalIncidentSession session, FixedClock clock, Action<string, Action> offerUndo) =>
        new(session, clock, new FakeTicker(), new FakeAlarmService(), MasterDataSet.Empty, () => { }, offerUndo);

    private static IncidentWorkspaceViewModel Workspace(LocalIncidentSession session, FixedClock clock, FakeTicker ticker) =>
        new(
            session,
            clock,
            ticker,
            MasterDataSet.Empty,
            new FakeDialogs(),
            new FakeAlarmService(),
            new FakeHostController());

    [Fact]
    public void Ticking_off_an_aufgabe_offers_to_take_it_back()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        string? offered = null;
        Action? undo = null;
        using var vm = Tasks(session, clock, (message, onUndo) =>
        {
            offered = message;
            undo = onUndo;
        });
        vm.Filter = TaskFilterKind.All;

        vm.Rows.Single().IsDone = true;

        Assert.Equal("„Tür sichern“ erledigt.", offered);
        Assert.True(session.Incident.Tasks[0].IsCompleted);
        undo!();
        Assert.False(session.Incident.Tasks[0].IsCompleted);
        Assert.False(vm.Rows.Single().IsDone);
    }

    [Fact]
    public void Reopening_an_aufgabe_offers_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        session.SetTaskCompleted(session.Incident.Tasks[0].Id, true);
        var offers = 0;
        using var vm = Tasks(session, clock, (_, _) => offers++);
        vm.Filter = TaskFilterKind.All;

        vm.Rows.Single().IsDone = false;

        Assert.Equal(0, offers);
        Assert.False(session.Incident.Tasks[0].IsCompleted);
    }

    [Fact]
    public void A_completion_pulled_from_elsewhere_offers_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        var offers = 0;
        using var vm = Tasks(session, clock, (_, _) => offers++);
        vm.Filter = TaskFilterKind.All;

        session.SetTaskCompleted(session.Incident.Tasks[0].Id, true); // another tab or device

        Assert.True(vm.Rows.Single().IsDone);
        Assert.Equal(0, offers);
    }

    [Fact]
    public void Undo_on_the_workspace_reopens_the_aufgabe_and_closes_the_notice()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        using var vm = Workspace(session, clock, new FakeTicker());
        vm.Tasks.Filter = TaskFilterKind.All;

        vm.Tasks.Rows.Single().IsDone = true;

        var notice = Assert.IsType<UndoNoticeViewModel>(vm.UndoNotice);
        Assert.Equal("„Tür sichern“ erledigt.", notice.Message);
        vm.UndoCommand.Execute(null);
        Assert.False(session.Incident.Tasks[0].IsCompleted);
        Assert.Null(vm.UndoNotice);
    }

    [Fact]
    public void Undo_with_no_notice_does_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        session.SetTaskCompleted(session.Incident.Tasks[0].Id, true);
        using var vm = Workspace(session, clock, new FakeTicker());

        Assert.False(vm.UndoCommand.CanExecute(null)); // Ctrl+Z is left to whatever else wants it
        vm.UndoCommand.Execute(null);

        Assert.True(session.Incident.Tasks[0].IsCompleted);
        Assert.Null(vm.UndoNotice);
    }

    [Fact]
    public void Dismissing_the_notice_keeps_the_aufgabe_done()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        using var vm = Workspace(session, clock, new FakeTicker());
        vm.Tasks.Filter = TaskFilterKind.All;
        vm.Tasks.Rows.Single().IsDone = true;

        vm.UndoNotice!.DismissCommand.Execute(null);

        Assert.Null(vm.UndoNotice);
        vm.UndoCommand.Execute(null);
        Assert.True(session.Incident.Tasks[0].IsCompleted);
    }

    [Fact]
    public void The_notice_goes_away_on_its_own_after_a_few_seconds()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        using var vm = Workspace(session, clock, ticker);
        vm.Tasks.Filter = TaskFilterKind.All;
        vm.Tasks.Rows.Single().IsDone = true;

        clock.Now = T0.AddSeconds(7);
        ticker.Fire();
        Assert.NotNull(vm.UndoNotice);

        clock.Now = T0.AddSeconds(8);
        ticker.Fire();
        Assert.Null(vm.UndoNotice);
        vm.UndoCommand.Execute(null);
        Assert.True(session.Incident.Tasks[0].IsCompleted);
    }

    [Fact]
    public void A_second_offer_replaces_the_first()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        session.AddTask("Strom abschalten", null, TaskImportance.Low, TaskUrgency.Low, 30);
        using var vm = Workspace(session, clock, new FakeTicker());
        vm.Tasks.Filter = TaskFilterKind.All;
        var first = vm.Tasks.Rows.Single(r => r.Text == "Tür sichern");
        var second = vm.Tasks.Rows.Single(r => r.Text == "Strom abschalten");

        first.IsDone = true;
        second.IsDone = true;
        vm.UndoCommand.Execute(null);

        Assert.True(session.Incident.Tasks.Single(t => t.Text == "Tür sichern").IsCompleted);
        Assert.False(session.Incident.Tasks.Single(t => t.Text == "Strom abschalten").IsCompleted);
    }

    [Fact]
    public void No_ticker_subscription_is_held_while_no_notice_shows()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        session.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        using var vm = Workspace(session, clock, ticker);
        vm.Tasks.Filter = TaskFilterKind.All;
        var idle = ticker.SubscriberCount;

        vm.Tasks.Rows.Single().IsDone = true;
        Assert.Equal(idle + 1, ticker.SubscriberCount);

        vm.UndoCommand.Execute(null);
        Assert.Equal(idle, ticker.SubscriberCount);
    }
}
