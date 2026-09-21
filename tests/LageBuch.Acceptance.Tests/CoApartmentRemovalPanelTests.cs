using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #419: shrinking a floor in Struktur-Modus used to delete Wohnungen off the right-hand end the
// instant the spinner changed. Drives the real view to confirm the panel actually appears, that
// nothing is written while it is open, and that ABBRECHEN puts the spinner back.
public class CoApartmentRemovalPanelTests
{
    private static (Window Window, CoMessprotokollView View, CoMessprotokollViewModel Vm, LocalIncidentSession Session) Show()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 1, 4);
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);

        var vm = new CoMessprotokollViewModel(session, new FixedClock(), () => { })
        {
            IsStructureMode = true,
        };
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, view, vm, session);
    }

    private static Border Panel(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ApartmentRemovalPanel");

    private static Button ConfirmButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmApartmentRemovalButton");

    [AvaloniaFact]
    public void Shrinking_a_floor_that_holds_a_measurement_shows_the_panel_and_writes_nothing()
    {
        var (window, _, vm, session) = Show();
        var before = session.Incident.Dwellings.Count;
        Assert.False(Panel(window).IsEffectivelyVisible);

        vm.MatrixRows.Single(r => r.Ordinal == 0).ApartmentCount = 3;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Panel(window).IsEffectivelyVisible);
        Assert.Equal(before, session.Incident.Dwellings.Count);
        Assert.Equal(4, session.Incident.Buildings[0].ApartmentsFor(0));
    }

    [AvaloniaFact]
    public void Confirming_removes_the_ticked_Wohnung_and_hides_the_panel()
    {
        var (window, _, vm, session) = Show();
        vm.MatrixRows.Single(r => r.Ordinal == 0).ApartmentCount = 3;
        Dispatcher.UIThread.RunJobs();

        var confirm = ConfirmButton(window);
        Assert.True(confirm.IsEffectivelyEnabled);
        confirm.Command!.Execute(confirm.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Panel(window).IsEffectivelyVisible);
        Assert.Equal(3, session.Incident.Buildings[0].ApartmentsFor(0));
        Assert.Equal(3, session.Incident.Dwellings.Count(d => d.FloorOrdinal == 0));
    }

    [AvaloniaFact]
    public void Cancelling_puts_the_spinner_back_where_it_was()
    {
        var (window, _, vm, session) = Show();
        vm.MatrixRows.Single(r => r.Ordinal == 0).ApartmentCount = 1;
        Dispatcher.UIThread.RunJobs();

        vm.CancelApartmentRemovalCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Panel(window).IsEffectivelyVisible);
        Assert.Equal(4, vm.MatrixRows.Single(r => r.Ordinal == 0).ApartmentCount);
        Assert.Equal(4, session.Incident.Buildings[0].ApartmentsFor(0));
    }
}
