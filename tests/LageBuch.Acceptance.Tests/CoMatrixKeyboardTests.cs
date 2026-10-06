using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #543: the CO-Messung matrix is walked with the arrow keys -- ← → between the Wohnungen of one
// floor, ↑ ↓ between floors -- and Enter opens the focused Wohnung, as a click on its tile does.
// Before this the only way across it was Tab, flat by flat, floor by floor.
public class CoMatrixKeyboardTests
{
    // Haus A: EG and 1.OG with three Wohnungen each, 2.OG with two.
    private static (Window Window, CoMessprotokollViewModel Vm) ShowMatrix()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Huber", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 2, 3); // EG plus two Obergeschosse
        session.SetApartmentCount(session.Incident.Buildings[0].Id, 2, 2);
        var vm = new CoMessprotokollViewModel(session, clock, () => { });
        var window = new Window { Content = new CoMessprotokollView { DataContext = vm }, Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static Button Tile(Window window, int floor, int apartment) =>
        window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is DwellingCellViewModel cell && cell.FloorOrdinal == floor && cell.ApartmentNumber == apartment);

    private static void FocusTile(Window window, int floor, int apartment)
    {
        Tile(window, floor, apartment).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Arrow_keys_move_between_flats_and_floors()
    {
        var (window, _) = ShowMatrix();
        FocusTile(window, 1, 1);

        window.Press(PhysicalKey.ArrowRight);
        window.AssertFocused(Tile(window, 1, 2));

        window.Press(PhysicalKey.ArrowUp);
        window.AssertFocused(Tile(window, 2, 2));

        window.Press(PhysicalKey.ArrowDown);
        window.Press(PhysicalKey.ArrowDown);
        window.AssertFocused(Tile(window, 0, 2));

        window.Press(PhysicalKey.ArrowLeft);
        window.AssertFocused(Tile(window, 0, 1));
    }

    [AvaloniaFact]
    public void Arrow_up_onto_a_shorter_floor_lands_on_its_last_flat()
    {
        var (window, _) = ShowMatrix();
        FocusTile(window, 1, 3);

        window.Press(PhysicalKey.ArrowUp);

        window.AssertFocused(Tile(window, 2, 2));
    }

    [AvaloniaFact]
    public void Arrow_keys_at_the_edge_of_the_matrix_keep_focus()
    {
        var (window, _) = ShowMatrix();

        FocusTile(window, 2, 1);
        window.Press(PhysicalKey.ArrowUp);
        window.AssertFocused(Tile(window, 2, 1));
        window.Press(PhysicalKey.ArrowLeft);
        window.AssertFocused(Tile(window, 2, 1));

        FocusTile(window, 0, 3);
        window.Press(PhysicalKey.ArrowDown);
        window.AssertFocused(Tile(window, 0, 3));
        window.Press(PhysicalKey.ArrowRight);
        window.AssertFocused(Tile(window, 0, 3));
    }

    [AvaloniaFact]
    public void Enter_on_a_flat_opens_its_editor()
    {
        var (window, vm) = ShowMatrix();
        FocusTile(window, 0, 1);
        window.Press(PhysicalKey.ArrowUp);
        window.Press(PhysicalKey.ArrowRight);

        window.Press(PhysicalKey.Enter);

        var editor = Assert.IsType<DwellingEditorViewModel>(vm.Editor);
        Assert.Equal(1, editor.FloorOrdinal);
        Assert.Equal(2, editor.ApartmentNumber);
    }
}
