using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// #724, found testing #695: "a selected row seems to have a different position in the tab order".
// Tab entered every row of a grid, and after a scroll the grid's recycled rows sat in the visual tree
// bottom-up, so Tab walked them from the last row built upwards and reached the selected one
// somewhere in the middle. Tab now enters the selected row only, then leaves the grid.
public class GridRowTabTests
{
    private sealed record Etb(Window Window, DataGrid Grid, List<EtbEntryRow> Rows);

    private static Etb ShowEtb(int entries = 30)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
        for (var n = 0; n < entries; n++)
        {
            vm.Etb.NewFrom = "Florian Musterstadt 1/10";
            vm.Etb.NewText = "Eintrag " + n.ToString(CultureInfo.InvariantCulture);
            vm.Etb.AddEntryCommand.Execute(null);
        }

        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1600, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var grid = WorkspaceRenderHelper.SelectedTabContent(window).GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "EtbGrid");
        return new Etb(window, grid, vm.Etb.Entries.ToList());
    }

    private static void ScrollTo(DataGrid grid, EtbEntryRow row)
    {
        grid.ScrollIntoView(row, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickRow(Etb etb, int index)
    {
        Dispatcher.UIThread.RunJobs();
        var cell = etb.Grid.GetVisualDescendants().OfType<DataGridCell>()
            .First(c => ReferenceEquals(c.DataContext, etb.Rows[index]) && c.IsEffectivelyVisible);
        var point = cell.TranslatePoint(new Point(10, 10), etb.Window) ?? throw new InvalidOperationException("Cell is not in the window.");
        etb.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        etb.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button FocusedButton(Etb etb, out int row)
    {
        var button = Assert.IsType<Button>(etb.Window.FocusedElement());
        row = button.DataContext is EtbEntryRow entry ? etb.Rows.IndexOf(entry) : -1;
        return button;
    }

    [AvaloniaFact]
    public void Tab_from_the_grid_enters_the_selected_row_after_a_scroll_and_then_leaves_the_grid()
    {
        var etb = ShowEtb();
        ScrollTo(etb.Grid, etb.Rows[25]);
        ScrollTo(etb.Grid, etb.Rows[5]);
        ClickRow(etb, 7);
        etb.Window.AssertFocused(etb.Grid);

        etb.Window.Tab();
        Assert.Equal("Bearbeiten", AutomationProperties.GetName(FocusedButton(etb, out var first)));
        Assert.Equal(7, first);

        etb.Window.Tab();
        Assert.Equal("Aufgabe erstellen", AutomationProperties.GetName(FocusedButton(etb, out var second)));
        Assert.Equal(7, second);

        etb.Window.Tab();
        Assert.False(etb.Window.IsFocusWithin(etb.Grid), $"Expected Tab to leave the grid, but focus is on {etb.Window.DescribeFocus()}.");
    }

    [AvaloniaFact]
    public void Shift_tab_from_the_selected_row_goes_back_to_the_grid_not_to_another_row()
    {
        var etb = ShowEtb();
        ClickRow(etb, 3);
        etb.Window.Tab();
        FocusedButton(etb, out var row);
        Assert.Equal(3, row);

        etb.Window.ShiftTab();

        etb.Window.AssertFocused(etb.Grid);
    }

    // The selected row has been scrolled out of view with the wheel, so the grid has not built it.
    [AvaloniaFact]
    public void Tab_from_the_grid_reaches_the_selected_row_even_when_it_is_scrolled_away()
    {
        var etb = ShowEtb();
        ClickRow(etb, 2);
        ScrollTo(etb.Grid, etb.Rows[^1]);
        etb.Window.AssertFocused(etb.Grid);

        etb.Window.Tab();

        FocusedButton(etb, out var row);
        Assert.Equal(2, row);
    }

    // A click puts focus on a row's button without selecting the row; Tab has to go on from there,
    // not from a row the selection still names.
    [AvaloniaFact]
    public void Focusing_a_row_s_button_selects_that_row()
    {
        var etb = ShowEtb(entries: 5);
        ClickRow(etb, 1);
        var edit = etb.Grid.GetVisualDescendants().OfType<Button>()
            .First(b => ReferenceEquals(b.DataContext, etb.Rows[4]) && AutomationProperties.GetName(b) == "Bearbeiten");

        edit.Focus(NavigationMethod.Pointer);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(etb.Rows[4], etb.Grid.SelectedItem);
        etb.Window.Tab();
        FocusedButton(etb, out var row);
        Assert.Equal(4, row);
    }

    // Every grid in the workspace, a new one included: the bug was in the grid, not in the ETB.
    [AvaloniaFact]
    public void Every_grid_in_the_workspace_tabs_into_its_selected_row_only()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1600, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grids = new List<DataGrid>();
        foreach (var module in new[] { "ETB", "AUFGABEN", "FUNKTIONEN", "KRÄFTE", "ATEMSCHUTZ" })
        {
            WorkspaceRenderHelper.SelectTab(window, module);
            grids.AddRange(WorkspaceRenderHelper.SelectedTabContent(window).GetVisualDescendants().OfType<DataGrid>());
        }

        Assert.Equal(5, grids.Count);
        Assert.All(grids, grid => Assert.True(App.Shared.Behaviors.SelectedRowTab.GetIsEnabled(grid), grid.Name));
    }
}
