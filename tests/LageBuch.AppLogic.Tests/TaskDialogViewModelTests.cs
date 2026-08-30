using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class TaskDialogViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, FixedClock Clock) NewSession()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (session, clock);
    }

    private static MasterDataSet MasterData() => MasterDataSet.Empty with
    {
        RadioCallSigns = new[] { "FFB 1/44/1" },
    };

    [Fact]
    public void Prefills_text_from_the_etb_entry_and_defaults_from_medium_urgency()
    {
        var (session, _) = NewSession();
        var closed = false;
        var dialog = new TaskDialogViewModel(
            session,
            MasterData(),
            "Lage erkundet — Mensch lebend aufgefunden",
            () => { });
        dialog.Closed += (_, _) => closed = true;

        Assert.Equal("Lage erkundet — Mensch lebend aufgefunden", dialog.Text);
        Assert.Equal(TaskUrgency.Medium, dialog.Urgency);
        Assert.Equal(15, dialog.TimerMinutes);

        dialog.SaveCommand.Execute(null);

        var task = Assert.Single(session.Incident.Tasks);
        Assert.Equal(dialog.Text, task.Text);
        Assert.True(closed); // SPEICHERN closes the dialog
    }

    [Fact]
    public void Save_and_create_another_keeps_dialog_open_with_cleared_text_and_sticky_fields()
    {
        var (session, _) = NewSession();
        var closedCount = 0;
        var dialog = new TaskDialogViewModel(session, MasterData(), "Erster Auftrag", () => { });
        dialog.Closed += (_, _) => closedCount++;

        dialog.Assignee = "FFB 1/44/1";
        dialog.Urgency = TaskUrgency.High;
        dialog.SaveAndCreateAnotherCommand.Execute(null);

        Assert.Single(session.Incident.Tasks);
        Assert.Equal(0, closedCount);              // dialog stays open
        Assert.Equal(string.Empty, dialog.Text);   // ready for the next task
        Assert.Equal("FFB 1/44/1", dialog.Assignee); // sticky
        Assert.Equal(TaskUrgency.High, dialog.Urgency);
        Assert.Equal(5, dialog.TimerMinutes);      // sticky override/default

        dialog.Text = "Zweiter Auftrag";
        dialog.SaveAndCreateAnotherCommand.Execute(null);

        Assert.Equal(2, session.Incident.Tasks.Count);
        Assert.Equal(0, closedCount);
    }

    [Fact]
    public void Cancel_closes_without_saving()
    {
        var (session, _) = NewSession();
        var closed = false;
        var dialog = new TaskDialogViewModel(session, MasterData(), "Entwurf", () => { });
        dialog.Closed += (_, _) => closed = true;

        dialog.CancelCommand.Execute(null);

        Assert.Empty(session.Incident.Tasks);
        Assert.True(closed);
    }

    [Fact]
    public void Save_canExecute_needs_text()
    {
        var (session, _) = NewSession();
        var dialog = new TaskDialogViewModel(session, MasterData(), string.Empty, () => { });

        Assert.False(dialog.SaveCommand.CanExecute(null));
        Assert.False(dialog.SaveAndCreateAnotherCommand.CanExecute(null));
        dialog.Text = "Jetzt ja";
        Assert.True(dialog.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_canExecute_is_gated_on_a_readonly_session()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        var dialog = new TaskDialogViewModel(ro, MasterData(), "Nachtrag", () => { });

        Assert.False(dialog.SaveCommand.CanExecute(null));
        Assert.False(dialog.SaveAndCreateAnotherCommand.CanExecute(null));
        dialog.Text = "Jetzt erst recht"; // text alone must not re-enable on a closed Einsatz
        Assert.False(dialog.SaveCommand.CanExecute(null));
    }
}
