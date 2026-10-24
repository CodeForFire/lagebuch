using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #536: in the Kräfte grid, a row's STATUS combo gave up keyboard focus as soon as it was clicked,
// so after choosing a status, Down moved the grid's cell cursor to the next row instead of
// stepping the status. Not the rebuild #294 blamed: it happened with no save at all. Every left
// press inside a DataGrid cell hands focus to the grid (DataGridCell's PointerPressed class
// handler, which runs even for a press the combo already handled), so the combo that the press
// had just focused lost it again.
public class ForcesStatusComboFocusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Am Einsatzort" },
    };

    private static (Window Window, ForcesView View, ForcesViewModel Vm) Show()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        // A middle row: on the last one a grid-cursor Down has nowhere to go and looks the same as
        // a combo that ignored the key.
        session.AddForceUnit("FFB Wache 1", 6, "Florian Musterstadt 40/1", "Alarmiert", null);
        session.AddForceUnit("FFB Wache 2", 6, "Florian Musterstadt 41/1", "Alarmiert", null);
        session.AddForceUnit("FFB Wache 3", 6, "Florian Musterstadt 42/1", "Alarmiert", null);
        var vm = new ForcesViewModel(session, new FixedClock(), Md(), () => { });
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static ComboBox StatusCombo(ForcesView view, ForceRow row) =>
        view.GetControl<DataGrid>("ForcesGrid").GetVisualDescendants().OfType<ComboBox>()
            .Single(c => ReferenceEquals(c.DataContext, row));

    private static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_clicked_status_combo_in_a_grid_row_keeps_the_keyboard()
    {
        var (window, view, vm) = Show();
        var row = vm.Forces[1];
        var combo = StatusCombo(view, row);

        Click(window, combo);
        window.Press(PhysicalKey.Escape);

        window.AssertFocused(combo);

        window.Press(PhysicalKey.ArrowDown);

        Assert.Equal("Auf Anfahrt", row.Status);
        Assert.Equal("Alarmiert", vm.Forces[2].Status);
    }

    // The Bemerkung beside it never lost focus to a click: a TextBox takes focus back after the
    // cell has taken it. Pinned so that handing focus back to the combo leaves the field alone.
    [AvaloniaFact]
    public void A_clicked_bemerkung_field_in_a_grid_row_keeps_the_keyboard()
    {
        var (window, view, vm) = Show();
        var notes = view.GetControl<DataGrid>("ForcesGrid").GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.TemplatedParent is null && ReferenceEquals(t.DataContext, vm.Forces[1]));

        Click(window, notes);

        window.AssertFocused(notes);
    }

    [AvaloniaFact]
    public void A_click_elsewhere_in_a_grid_row_still_focuses_the_grid()
    {
        var (window, view, vm) = Show();
        var grid = view.GetControl<DataGrid>("ForcesGrid");
        var callSignCell = grid.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == vm.Forces[1].CallSign);

        Click(window, callSignCell);

        window.AssertFocused(grid);
        Assert.Same(vm.Forces[1], grid.SelectedItem);
    }
}
