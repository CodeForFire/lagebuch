using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// A start that threw while building the main window used to end with no window at all: the
// crash report went to the pending folder, and the next start -- which moved it out of there to
// announce it -- crashed the same way before it had a screen to announce it on. Every start
// failed silently. The failure now gets a window of its own, and its report is written at once.
public class StartupFailureWindowTests
{
    private const string ReportFolder = @"C:\Users\x\AppData\Roaming\Lagebuch\crashes";

    [AvaloniaFact]
    public void A_start_that_throws_shows_the_failure_and_where_its_report_went()
    {
        var failure = new InvalidOperationException("SQLite Error 1: 'no such column: postal_code'.");
        var reported = new List<Exception>();

        var window = LageBuch.App.Shared.App.CreateDesktopMainWindow(
            () => throw failure,
            ex =>
            {
                reported.Add(ex);
                return ReportFolder;
            });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<StartupFailureWindow>(window);
        Assert.Same(failure, Assert.Single(reported));
        var details = window.GetControl<SelectableTextBlock>("FailureDetails");
        Assert.Contains("System.InvalidOperationException", details.Text, StringComparison.Ordinal);
        Assert.Contains("no such column: postal_code", details.Text, StringComparison.Ordinal);
        var report = window.GetControl<SelectableTextBlock>("ReportLocation");
        Assert.Contains(ReportFolder, report.Text, StringComparison.Ordinal);
        foreach (var line in new[] { details, report })
        {
            Assert.True(line.IsEffectivelyVisible);
            Assert.Equal(TextWrapping.Wrap, line.TextWrapping);
        }
    }

    [AvaloniaFact]
    public void A_report_that_could_not_be_written_is_said_so_rather_than_pointing_nowhere()
    {
        var window = LageBuch.App.Shared.App.CreateDesktopMainWindow(
            () => throw new IOException("disk full"),
            _ => null);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            StartupFailureWindow.NoReportWritten,
            window.GetControl<SelectableTextBlock>("ReportLocation").Text);
    }

    [AvaloniaFact]
    public void Closing_the_failure_window_closes_it()
    {
        var window = LageBuch.App.Shared.App.CreateDesktopMainWindow(() => throw new InvalidOperationException(), _ => null);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetControl<Button>("CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
    }
}
