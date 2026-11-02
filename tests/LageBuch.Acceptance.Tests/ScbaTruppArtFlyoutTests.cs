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

/// <summary>
/// Changing a Trupp's Trupp-Art through the real control (#835), pressed for real in both layouts
/// so the flyout has to close on its own, which the Sicherheitstrupp picker once did not (#832).
/// </summary>
public class ScbaTruppArtFlyoutTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Picking_a_trupp_art_changes_it_and_closes_the_flyout(bool narrow)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        scba.NewDesignation = "Sicherheitstrupp";
        scba.NewCallSign = "Florian Musterdorf 42/1";
        scba.NewTruppfuehrer = "Müller";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;
        scba.AddTruppCommand.Execute(null);
        var row = scba.Trupps[^1];

        vm.IsNarrow = narrow;
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var pencil = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.DataContext, row)
                && AutomationProperties.GetName(b) == "Trupp-Art ändern");
        Click(pencil);
        var flyout = Assert.IsType<Flyout>(pencil.Flyout);
        Assert.True(flyout.IsOpen);
        var entry = Assert.IsAssignableFrom<Control>(flyout.Content).GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is TruppTypeChoice { Name: "Angriffstrupp" });
        Click(entry);

        Assert.Equal("Florian Musterdorf 42/1 · Angriffstrupp", scba.Trupps.Single(r => r.Id == row.Id).DisplayName);
        Assert.False(flyout.IsOpen);
        Assert.Contains(
            view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == "Florian Musterdorf 42/1 · Angriffstrupp");
    }

    private static void Click(Control control)
    {
        // The phone's cards stack below the form, so the last Trupp's controls start off screen.
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var topLevel = TopLevel.GetTopLevel(control)!;
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;
        topLevel.MouseDown(centre, MouseButton.Left);
        topLevel.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
