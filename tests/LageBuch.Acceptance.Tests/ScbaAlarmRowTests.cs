using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// #669: an alarming Trupp was told apart from the others by two red cells. The whole row — and on
// a phone the whole card — now carries the alarm, whatever the selection, and drops it the moment
// the Trupp leaves the alarm.
public class ScbaAlarmRowTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(double width, double height)
    {
        // Trupp 1 is 31 minutes under air (Rückzugsalarm), Trupp 2 has just started.
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        vm.Scba.NewDesignation = "Sicherheitstrupp";
        vm.Scba.NewTruppfuehrer = "Huber";
        vm.Scba.NewTruppmann = "Berger";
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);

        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ATEMSCHUTZ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Scba.Trupps[0].IsAlarm);
        Assert.False(vm.Scba.Trupps[1].IsAlarm);
        return (window, vm);
    }

    private static List<DataGridRow> Rows(Window window) =>
        window.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.DataContext is ScbaTruppRow).ToList();

    private static Color Fill(DataGridRow row)
    {
        var rect = row.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "BackgroundRectangle");
        return Assert.IsAssignableFrom<ISolidColorBrush>(rect.Fill).Color;
    }

    [AvaloniaFact]
    public void Only_the_alarming_trupps_grid_row_carries_the_alarm()
    {
        var (window, vm) = ShowWorkspace(1920, 1032);
        Capture(window, "scba-alarm-grid.png");

        var rows = Rows(window);
        Assert.Equal(2, rows.Count);
        Assert.Contains("alarm", rows.Single(r => ReferenceEquals(r.DataContext, vm.Scba.Trupps[0])).Classes);
        Assert.DoesNotContain("alarm", rows.Single(r => ReferenceEquals(r.DataContext, vm.Scba.Trupps[1])).Classes);
    }

    [AvaloniaFact]
    public void Selecting_the_alarming_row_does_not_mask_its_alarm()
    {
        var (window, vm) = ShowWorkspace(1920, 1032);
        var alarming = Rows(window).Single(r => ReferenceEquals(r.DataContext, vm.Scba.Trupps[0]));
        var unselected = Fill(alarming);

        vm.Scba.SelectedTrupp = vm.Scba.Trupps[0];
        Dispatcher.UIThread.RunJobs();

        alarming = Rows(window).Single(r => ReferenceEquals(r.DataContext, vm.Scba.Trupps[0]));
        Assert.True(alarming.IsSelected);
        Assert.Contains("alarm", alarming.Classes);
        var selected = Fill(alarming);

        // Still the signal hue (red dominant), and still told apart from the unselected alarm row.
        Assert.True(selected.R > selected.G * 2 && selected.R > selected.B * 2, $"selected alarm fill {selected} is not red");
        Assert.NotEqual(unselected, selected);
    }

    [AvaloniaFact]
    public void The_alarm_leaves_the_row_when_the_trupp_leaves_the_alarm()
    {
        var (window, vm) = ShowWorkspace(1920, 1032);
        var trupp = vm.Scba.Trupps[0];

        trupp.WithdrawCommand.Execute(null);
        vm.Scba.Trupps.First(r => r.DisplayName == trupp.DisplayName).MarkRemovedCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(vm.Scba.Trupps, r => r.IsAlarm);
        Assert.All(Rows(window), r => Assert.DoesNotContain("alarm", r.Classes));
    }

    [AvaloniaFact]
    public void Only_the_alarming_trupps_phone_card_carries_the_alarm()
    {
        var (window, vm) = ShowWorkspace(412, 915);
        Capture(window, "scba-alarm-cards.png");

        var cards = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("row-card") && b.DataContext is ScbaTruppRow && b.IsEffectivelyVisible)
            .ToList();
        Assert.Equal(2, cards.Count);
        Assert.Contains("alarm", cards.Single(c => ReferenceEquals(c.DataContext, vm.Scba.Trupps[0])).Classes);
        Assert.DoesNotContain("alarm", cards.Single(c => ReferenceEquals(c.DataContext, vm.Scba.Trupps[1])).Classes);
    }

    // Opt-in PNG capture for the PR's before/after pair; a plain test run writes nothing.
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
        frame?.SavePng(System.IO.Path.Join(dir, name));
    }
}
