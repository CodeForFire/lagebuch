using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// The Ort of a Druckabfrage is typed after the Druck. In the grid it sits on the line below, for
/// lack of room beside it, so Tab has to be told to go Druck → Ort → DRUCK rather than reach the
/// buttons first; on the phone card it is beside the Druck and gets there on its own.
/// </summary>
public class ScbaLocationInputTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_goes_from_the_druck_to_the_ort_and_then_to_the_druck_button(bool narrow)
    {
        var (row, view, window) = ShowActiveTrupp(narrow);
        var pressure = Visible<NumericUpDown>(view, "PressureInput", row);
        var location = Visible<TextBox>(view, "LocationInput", row);

        pressure.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(pressure.IsKeyboardFocusWithin);

        window.Tab();
        Assert.True(location.IsFocused);

        window.Tab();
        var focused = Assert.IsType<Button>(window.FocusedElement());
        Assert.Equal("DRUCK", focused.Content);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enter_in_the_ort_records_the_druck_with_it(bool narrow)
    {
        var (row, view, window) = ShowActiveTrupp(narrow);
        var location = Visible<TextBox>(view, "LocationInput", row);
        row.PressureInput = 300; // the Einstiegsdruck again: plausible at once, unlike a drop with no time passed
        row.LocationInput = "2. OG";

        location.Focus();
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.Enter);

        Assert.Null(row.PressureInput);
        Assert.Null(row.LocationInput);
        Assert.Equal("2. OG", row.LocationDisplay);
    }

    private static T Visible<T>(ScbaView view, string name, ScbaTruppRow row)
        where T : Control =>
        view.GetVisualDescendants().OfType<T>()
            .First(c => c.Name == name && c.IsEffectivelyVisible && ReferenceEquals(c.DataContext, row));

    private static (ScbaTruppRow Row, ScbaView View, Window Window) ShowActiveTrupp(bool narrow)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        scba.NewDesignation = "Angriffstrupp";
        scba.NewTruppfuehrer = "Müller";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;
        scba.AddTruppCommand.Execute(null);
        var row = scba.Trupps[^1];
        row.StartCommand.Execute(null);

        // The same setup ScbaDoubleClickTests uses: a phone's width folds the grid into cards.
        vm.IsNarrow = narrow;
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (row, view, window);
    }
}
