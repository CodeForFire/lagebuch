using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
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

    // #968: the fixture's "Florian Musterstadt 40/1 · Angriffstrupp" is under air, so that Fahrzeug's
    // Sicherheitstrupp is not offered Angriffstrupp -- the change that listed the same Trupp twice.
    [AvaloniaFact]
    public void The_flyout_does_not_offer_the_trupp_art_another_trupp_of_the_fahrzeug_already_has()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        scba.NewDesignation = "Sicherheitstrupp";
        scba.NewCallSign = "Florian Musterstadt 40/1";
        scba.NewTruppfuehrer = "Huber";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;
        scba.AddTruppCommand.Execute(null);
        var row = scba.Trupps[^1];
        var (window, view) = Show(scba);

        var pencil = Pencil(view, row);
        Click(pencil);
        var flyout = Assert.IsType<Flyout>(pencil.Flyout);
        var content = Assert.IsAssignableFrom<Control>(flyout.Content);
        var offered = content.GetVisualDescendants().OfType<Button>()
            .Select(b => b.DataContext).OfType<TruppTypeChoice>().Select(c => c.Name).ToList();

        Assert.Equal(new[] { "Sicherheitstrupp", "CSA-Trupp" }, offered);
        CaptureWithPopup(window, content, "scba-trupp-art-flyout-without-duplicate.png");
    }

    [AvaloniaFact]
    public void Registering_the_same_trupp_again_says_so_under_the_form()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        var (window, view) = Show(scba);
        scba.NewDesignation = "Angriffstrupp";
        scba.NewCallSign = "Florian Musterstadt 40/1";
        scba.NewTruppfuehrer = "Huber";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;

        scba.AddTruppCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var summary = view.GetControl<TextBlock>("DockErrorSummary");
        Assert.True(summary.IsEffectivelyVisible);
        Assert.Equal("„Florian Musterstadt 40/1 · Angriffstrupp“ ist bereits eingetragen.", summary.Text);
        Assert.Single(scba.Trupps);
        CaptureWithPopup(window, null, "scba-duplicate-trupp-refused.png");
    }

    private static (Window Window, ScbaView View) Show(ScbaViewModel scba)
    {
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        var window = new Window { Content = workspace, Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static Button Pencil(ScbaView view, ScbaTruppRow row) =>
        view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.DataContext, row)
                && AutomationProperties.GetName(b) == "Trupp-Art ändern");

    // The flyout renders in its own PopupRoot, so the window's frame lacks it; draw it on top, as
    // ForcesTabRenderTests does. Only with RENDER_OUT set, for the pull request's screenshots.
    private static void CaptureWithPopup(Window window, Control? popupContent, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Dispatcher.UIThread.RunJobs();
        using var windowFrame = window.CaptureRenderedFrame();
        Assert.NotNull(windowFrame);
        using var composite = new RenderTargetBitmap(windowFrame.PixelSize);
        using (var context = composite.CreateDrawingContext())
        {
            context.DrawImage(windowFrame, new Rect(windowFrame.Size));
            if (popupContent is not null && TopLevel.GetTopLevel(popupContent) is { } popupRoot)
            {
                using var popupFrame = popupRoot.CaptureRenderedFrame();
                Assert.NotNull(popupFrame);
                var offset = popupRoot.PointToScreen(default) - window.PointToScreen(default);
                context.DrawImage(popupFrame, new Rect(new Point(offset.X, offset.Y), popupFrame.Size));
            }
        }

        Directory.CreateDirectory(dir);
        composite.Save(Path.Join(dir, name), PngBitmapEncoderOptions.Default);
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
