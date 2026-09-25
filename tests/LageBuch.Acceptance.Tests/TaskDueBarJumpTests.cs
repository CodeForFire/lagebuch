using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// #460: "aufgabe: header meldung fehlt mit klick auf aufgabe". A task falling due played a tone
// and turned a chip red in a tab the operator was not looking at. The header now carries a bar
// naming the task, and tapping it opens AUFGABEN on that task. Clicked through the headless
// input stack, as ScbaBannerJumpTests does, because a jump that never fires is a hit-test bug.
public class TaskDueBarJumpTests
{
    [AvaloniaFact]
    public void Tapping_the_aufgabe_faellig_bar_opens_the_aufgaben_tab_on_that_task()
    {
        var (window, view, vm) = ShowWorkspace();
        Capture(window, "task-due-bar-before.png");

        var bar = view.GetControl<Button>("TaskDueJumpButton");
        Assert.True(bar.IsVisible);
        Assert.Equal(
            "Aufgabe fällig: Wasserversorgung aus Hydrant sicherstellen (zugeteilt an EL)",
            view.GetControl<TextBlock>("TaskDueText").Text);
        Click(window, bar);

        Assert.Same(vm.Tasks, vm.SelectedNavItem!.Content);
        Assert.Equal("AUFGABEN", vm.SelectedNavItem.Header);

        // The overdue task, not merely the first row: the urgent one is listed above it.
        var overdue = vm.Tasks.Rows.Single(r => r.IsOverdue);
        Assert.NotSame(overdue, vm.Tasks.Rows[0]);
        Assert.Same(overdue, vm.Tasks.SelectedTask);
        Assert.Same(overdue, Grid(window).SelectedItem);

        Capture(window, "task-due-bar-after.png");
    }

    [AvaloniaFact]
    public void Erledigt_stays_its_own_button_next_to_the_jump()
    {
        var (window, view, vm) = ShowWorkspace();
        var before = vm.SelectedNavItem;

        Click(window, view.GetControl<Button>("TaskDueDoneButton"));

        // ERLEDIGT ticks the task off and the bar goes; it must not have been swallowed by the
        // jump beside it, and it must not have navigated anywhere.
        Assert.False(vm.Tasks.HasDueTask);
        Assert.False(view.GetControl<Border>("TaskDueBar").IsVisible);
        Assert.Same(before, vm.SelectedNavItem);
    }

    private static (Window Window, IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
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

        window.MouseMove(new Avalonia.Point(window.Width / 2, window.Height - 20));
        Dispatcher.UIThread.RunJobs();

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }
}
