using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Feuerwehr.App.Shared.Views;

namespace Feuerwehr.Acceptance.Tests;

// Issue #43: the ILS reminder's countdown looked weak — a small lowercase "in 11:32" with no
// emphasis. It is now the hero of the bar: the remaining time in large tactical monospace (stable
// digit width as it ticks every second), in the reminder's accent colour.
public class ReminderFontTests
{
    [AvaloniaFact]
    public void The_ils_countdown_is_the_monospace_hero()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        // Put the ILS reminder into the running-not-due state so the countdown text is present.
        vm.Reminder!.StopCommand.Execute(null);
        vm.Reminder!.IntervalMinutes = 15;
        vm.Reminder!.StartCommand.Execute(null);

        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var countdown = view.GetControl<TextBlock>("ReminderCountdownText");
        var mono = (FontFamily)Application.Current!.FindResource("MonoFont")!;

        Assert.Equal(mono, countdown.FontFamily);
        Assert.True(countdown.FontSize >= 18, $"countdown is {countdown.FontSize}px — not the hero of the bar");
    }
}
