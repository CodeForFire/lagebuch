using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #294: every change anywhere in the Einsatz rebuilt the Kräfte grid with Clear()+re-add, so the
// selected row lost its highlight (and the STATUS combo its focus) after any save.
public class ForcesSelectionSurvivesChangeTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        UnitStatus = new[] { "Alarmiert", "Im Einsatz" },
    };

    [AvaloniaFact]
    public void The_selected_row_stays_selected_when_the_incident_changes_elsewhere()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddForceUnit("FFB Wache 1", 9, "FFB 11/1", "Alarmiert", null);
        session.AddForceUnit("Emmering", 6, "FFB 12/1", "Alarmiert", null);
        var vm = new ForcesViewModel(session, new FixedClock(), Md(), () => { });
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = Assert.IsType<DataGrid>(view.FindControl<DataGrid>("ForcesGrid"));
        var second = vm.Forces[1];
        grid.SelectedItem = second;
        Dispatcher.UIThread.RunJobs();

        session.AddJournalEntry(EtbDirection.Outgoing, "Lagemeldung");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(second, grid.SelectedItem);

        // A status change on the selected row itself is the case the operator actually hit.
        second.Status = "Im Einsatz";
        Dispatcher.UIThread.RunJobs();

        Assert.Same(second, grid.SelectedItem);
        Assert.Equal("Im Einsatz", session.Incident.Forces[1].Status);
    }
}
