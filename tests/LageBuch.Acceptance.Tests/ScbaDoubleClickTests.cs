using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// Each of a Trupp's states puts its next button in the slot the previous one just left, so the
/// second click of a double-click lands on whatever the first one made appear: ABGENOMMEN after
/// RÜCKZUG (#601), DRUCK or RÜCKZUG after IM EINSATZ (#630). Replayed through the real buttons,
/// because the bug is the layout: the second click lands wherever the first one's button used to be.
/// </summary>
public class ScbaDoubleClickTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_double_click_on_Rueckzug_leaves_the_trupp_withdrawing(bool narrow)
    {
        var (scba, row, view, window) = ShowTrupp(narrow);
        row.StartCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        DoubleClick(window, view, row, "RÜCKZUG", atRightEdge: false);

        var after = scba.Trupps.Single(r => r.Id == row.Id);
        Assert.True(after.IsWithdrawing);
        Assert.False(after.IsReturned);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A_double_click_on_Im_Einsatz_starts_the_trupp_and_nothing_else(bool narrow, bool atRightEdge)
    {
        var (scba, row, view, window) = ShowTrupp(narrow);

        DoubleClick(window, view, row, "IM EINSATZ", atRightEdge);

        var after = scba.Trupps.Single(r => r.Id == row.Id);
        Assert.True(after.IsActive);
        Assert.False(after.IsWithdrawing);
        Assert.False(after.HasPressureError);
    }

    private static (ScbaViewModel Scba, ScbaTruppRow Row, ScbaView View, Window Window) ShowTrupp(bool narrow)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        scba.NewDesignation = "Angriffstrupp";
        scba.NewTruppfuehrer = "Müller";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;
        scba.AddTruppCommand.Execute(null);
        var row = scba.Trupps[^1];

        // What IncidentWorkspaceView does at a phone's width: its code-behind folds the composer
        // away, and its root's "narrow" class swaps the grid for the cards.
        vm.IsNarrow = narrow;
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (scba, row, view, window);
    }

    private static void DoubleClick(Window window, ScbaView view, ScbaTruppRow row, string label, bool atRightEdge)
    {
        var button = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && label.Equals(b.Content) && ReferenceEquals(b.DataContext, row));
        var x = atRightEdge ? button.Bounds.Width - 2 : button.Bounds.Width / 2;
        var point = button.TranslatePoint(new Point(x, button.Bounds.Height / 2), window)!.Value;

        for (var click = 0; click < 2; click++)
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
