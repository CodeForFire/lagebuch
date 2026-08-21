using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// Issue #43: the ILS reminder's countdown looked weak — a small lowercase "in 11:32" with no
// emphasis. It is now the hero of the bar: the remaining time in large tactical monospace (stable
// digit width as it ticks every second), in the reminder's accent colour.
public class ReminderFontTests
{
    [AvaloniaFact]
    public void The_ils_countdown_is_the_monospace_hero()
    {
        // The helper leaves the auto-started ILS reminder in the running-not-due state (acknowledged
        // after the time-advance), so the countdown text is present.
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();

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
