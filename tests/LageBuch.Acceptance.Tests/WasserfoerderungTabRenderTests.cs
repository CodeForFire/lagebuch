using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// Issue #150 (Plan A): the new WASSERFÖRDERUNG tab. Doubles as the PR screenshot capture
// (RENDER_OUT), same idiom as TasksTabRenderTests/ForcesTabRenderTests.
public class WasserfoerderungTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new ManualTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, name));
    }

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void Wasserfoerderung_tab_renders_empty_then_planned_streets()
    {
        var (window, vm, session) = ShowWorkspace();

        Tabs(window).SelectedIndex = 9; // WASSERFÖRDERUNG
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Wasserfoerderung.Rows);
        Capture(window, "wasserfoerderung-before.png");

        session.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 100);
        session.AddWasserfoerderungLeitung(null, null, 400, 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Wasserfoerderung.Rows.Count);
        Assert.Equal("Ltg 1", vm.Wasserfoerderung.Rows[0].NumberDisplay);
        Assert.Equal(4, session.Incident.Wasserfoerderung[0].PumpCount);
        Capture(window, "wasserfoerderung-after.png");
    }
}