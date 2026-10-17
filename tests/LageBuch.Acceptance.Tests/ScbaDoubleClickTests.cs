using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// ABGENOMMEN takes the slot RÜCKZUG just left, so a double-click on RÜCKZUG used to withdraw the
/// Trupp and end its monitoring in one gesture (#601). Replayed through the real buttons, because
/// the bug is the layout: the second click lands wherever the first one's button used to be.
/// </summary>
public class ScbaDoubleClickTests
{
    [AvaloniaFact]
    public void A_double_click_on_Rueckzug_leaves_the_trupp_withdrawing()
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

        var view = new ScbaView { DataContext = scba };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rueckzug = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && "RÜCKZUG".Equals(b.Content) && ReferenceEquals(b.DataContext, row));
        var centre = rueckzug.TranslatePoint(new Point(rueckzug.Bounds.Width / 2, rueckzug.Bounds.Height / 2), window)!.Value;

        for (var click = 0; click < 2; click++)
        {
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        var after = scba.Trupps.Single(r => r.Id == row.Id);
        Assert.True(after.IsWithdrawing);
        Assert.False(after.IsReturned);
    }
}
