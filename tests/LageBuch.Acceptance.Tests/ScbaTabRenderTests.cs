using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// The Atemschutz tab is the safety-critical heart of the app: this pins that the composite
// state built by WorkspaceRenderHelper (started trupp, pressure control due, Rückzugsalarm
// after 31 min) still renders as the alarm row it must be. Doubles as the README screenshot
// capture (RENDER_OUT), same idiom as FilesTabRenderTests.
public class ScbaTabRenderTests
{
    [AvaloniaFact]
    public void Atemschutz_tab_shows_the_started_trupp_in_rueckzugsalarm()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");
        tabs.SelectedIndex = 4; // ATEMSCHUTZ
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("ATEMSCHUTZ", ((TabItem)tabs.SelectedItem!).Header);
        var trupp = Assert.Single(vm.Scba.Trupps);
        Assert.True(trupp.IsActive);
        Assert.True(trupp.IsAlarm);

        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Combine(dir, "atemschutz.png"));
        }
    }
}
