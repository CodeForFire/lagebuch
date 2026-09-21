using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// #422: "Druckabfrage Button in der Titelleiste zeigt keine Reaktion, sollte auf den Eintrag
// verlinken von dem Trupp." It was a TextBlock in a red Border — it read as a button and was
// tapped as one. Both Atemschutz header bars are now real buttons that open the ATEMSCHUTZ tab on
// the Trupp they name. Clicked through the headless input stack rather than by executing the
// command, because "nothing happens when I tap it" is precisely a hit-testing question.
public class ScbaBannerJumpTests
{
    [AvaloniaFact]
    public void Tapping_the_druckabfrage_bar_opens_the_atemschutz_tab_on_that_trupp()
    {
        var (window, view, vm) = ShowWorkspace();
        Capture(window, "scba-banner-before.png");

        var bar = view.GetControl<Button>("ScbaControlJumpButton");
        Assert.True(bar.IsVisible);
        Click(window, bar);

        Assert.Same(vm.Scba, vm.SelectedNavItem!.Content);
        Assert.Equal("ATEMSCHUTZ", vm.SelectedNavItem.Header);

        // The overdue Trupp, not merely the first row: two are under air here.
        var overdue = vm.Scba.Trupps[0];
        Assert.True(overdue.IsControlDue);
        Assert.False(vm.Scba.Trupps[1].IsControlDue);
        Assert.Same(overdue, vm.Scba.SelectedTrupp);
        Assert.Same(overdue, Grid(window).SelectedItem);

        Capture(window, "scba-banner-after.png");
    }

    [AvaloniaFact]
    public void Tapping_the_rueckzugsalarm_banner_opens_the_atemschutz_tab_on_the_alarming_trupp()
    {
        var (window, view, vm) = ShowWorkspace();

        var banner = view.GetControl<Button>("ScbaAlarmJumpButton");
        Assert.True(banner.IsVisible);
        Click(window, banner);

        Assert.Same(vm.Scba, vm.SelectedNavItem!.Content);

        var alarming = vm.Scba.Trupps[0];
        Assert.True(alarming.IsAlarm);
        Assert.False(vm.Scba.Trupps[1].IsAlarm);
        Assert.Same(alarming, Grid(window).SelectedItem);
    }

    [AvaloniaFact]
    public void Quittieren_stays_its_own_button_next_to_the_jump()
    {
        var (window, view, vm) = ShowWorkspace();

        Click(window, view.GetControl<Button>("ScbaAlarmAckButton"));

        // The acknowledge silences the alarm; it must not have been swallowed by the jump around
        // it, and it must not have navigated anywhere.
        Assert.True(vm.Scba.IsAlarmAcknowledged);
        Assert.NotSame(vm.Scba, vm.SelectedNavItem?.Content);
    }

    private static (Window Window, IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();

        // A second Trupp under air, fresh: overdue and alarming are both Trupp 1, so "the bar
        // leads to the right row" is a claim with something to be wrong about — and the
        // screenshot shows which of two the operator was sent to.
        vm.Scba.NewDesignation = "Sicherheitstrupp";
        vm.Scba.NewTruppfuehrer = "Huber";
        vm.Scba.NewTruppmann = "Berger";
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);

        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static DataGrid Grid(Window window) =>
        window.GetVisualDescendants().OfType<DataGrid>().Single();

    private static void Click(Window window, Control control)
    {
        var centre = Avalonia.VisualExtensions.TranslatePoint(
            control,
            new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // Opt-in PNG capture for the PR's before/after pair; a plain test run writes nothing.
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        // Park the pointer clear of the header, or the jump button's tooltip stands open across
        // the Rückzugsalarm banner in the captured frame.
        window.MouseMove(new Avalonia.Point(window.Width / 2, window.Height - 20));
        Dispatcher.UIThread.RunJobs();

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }
}
