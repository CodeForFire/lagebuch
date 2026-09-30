using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #443: a Geschoss could be added but not removed. Drives the real view to confirm OG/UG
// ENTFERNEN are there, that a floor carrying data asks before anything is written, and that the
// buttons stop at the building's bounds.
public class CoFloorRemovalPanelTests
{
    private static (Window Window, CoMessprotokollViewModel Vm, LocalIncidentSession Session) Show(
        int floorCount = 2, int undergroundFloorCount = 1)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", floorCount, 4, undergroundFloorCount);
        session.RecordCoValue(session.Incident.Buildings[0].Id, floorCount, 2, 120);

        var vm = new CoMessprotokollViewModel(session, new FixedClock(), () => { })
        {
            IsStructureMode = true,
        };
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, vm, session);
    }

    private static Border Panel(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "FloorRemovalPanel");

    private static Button ButtonNamed(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.NotNull(button.Command);
        button.Command.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Removing_an_Obergeschoss_with_a_measurement_shows_the_panel_and_writes_nothing()
    {
        var (window, _, session) = Show();
        var before = session.Incident.Dwellings.Count;
        Assert.False(Panel(window).IsEffectivelyVisible);

        Click(ButtonNamed(window, "RemoveObergeschossButton"));

        Assert.True(Panel(window).IsEffectivelyVisible);
        Assert.Contains(
            Panel(window).GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "2. OG entfernen? 4 Wohnungen, davon 1 mit erfassten Daten.");
        Assert.Equal(2, session.Incident.Buildings[0].FloorCount);
        Assert.Equal(before, session.Incident.Dwellings.Count);
    }

    [AvaloniaFact]
    public void Confirming_removes_the_floor_and_hides_the_panel()
    {
        var (window, _, session) = Show();
        Click(ButtonNamed(window, "RemoveObergeschossButton"));

        Click(ButtonNamed(window, "ConfirmFloorRemovalButton"));

        Assert.False(Panel(window).IsEffectivelyVisible);
        Assert.Equal(1, session.Incident.Buildings[0].FloorCount);
        Assert.DoesNotContain(session.Incident.Dwellings, d => d.FloorOrdinal == 2);
    }

    [AvaloniaFact]
    public void Cancelling_leaves_the_floor_where_it_was()
    {
        var (window, vm, session) = Show();
        Click(ButtonNamed(window, "RemoveObergeschossButton"));

        Click(ButtonNamed(window, "CancelFloorRemovalButton"));

        Assert.False(Panel(window).IsEffectivelyVisible);
        Assert.Equal(2, session.Incident.Buildings[0].FloorCount);
        Assert.Contains(vm.MatrixRows, r => r.Ordinal == 2);
    }

    [AvaloniaFact]
    public void An_empty_Untergeschoss_goes_without_the_panel()
    {
        var (window, vm, session) = Show();

        Click(ButtonNamed(window, "RemoveUntergeschossButton"));

        Assert.False(Panel(window).IsEffectivelyVisible);
        Assert.Equal(0, session.Incident.Buildings[0].UndergroundFloorCount);
        Assert.DoesNotContain(vm.MatrixRows, r => r.Ordinal < 0);
    }

    [AvaloniaFact]
    public void The_buttons_are_disabled_at_the_buildings_bounds()
    {
        var (window, _, _) = Show(floorCount: 1, undergroundFloorCount: 0);

        Assert.False(ButtonNamed(window, "RemoveObergeschossButton").IsEffectivelyEnabled);
        Assert.False(ButtonNamed(window, "RemoveUntergeschossButton").IsEffectivelyEnabled);
    }
}
