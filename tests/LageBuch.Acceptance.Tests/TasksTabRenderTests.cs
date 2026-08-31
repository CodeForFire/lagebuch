using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Tasks;

namespace LageBuch.Acceptance.Tests;

// Issue #88: the new "AUFGABEN" tab. Doubles as the PR screenshot capture (RENDER_OUT),
// same idiom as FilesTabRenderTests/ForcesTabRenderTests.
public class TasksTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session, ManualTicker Ticker, FixedClock Clock) ShowWorkspace()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ticker = new ManualTicker();
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session, ticker, clock);
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
    public void Workspace_now_nine_tabs_with_aufgaben_third()
    {
        var (window, _, _, _, _) = ShowWorkspace();
        Assert.Equal(10, Tabs(window).Items.Count());
        var aufgabenTab = (TabItem)Tabs(window).Items.ElementAt(2)!;
        Assert.Equal("AUFGABEN", (string)aufgabenTab.Header!);
    }

    [AvaloniaFact]
    public void Aufgaben_tab_renders_open_overdue_and_done_rows()
    {
        var (window, vm, session, ticker, clock) = ShowWorkspace();

        session.AddTask("Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        session.AddTask("Kräftemeldung nachholen", null, TaskImportance.Medium, TaskUrgency.Medium, 15);
        session.AddTask("Gerät nachlegen", null, TaskImportance.Low, TaskUrgency.Low, 30);
        clock.Now = clock.Now.AddMinutes(6);   // first task overdue
        ticker.Pulse();
        session.SetTaskCompleted(session.Incident.Tasks[1].Id, true);

        Tabs(window).SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        // OFFEN is the default filter: the completed Kräftemeldung is not shown.
        Assert.DoesNotContain(vm.Tasks.Rows, r => r.Text == "Kräftemeldung nachholen");
        Assert.Contains(vm.Tasks.Rows, r => r.IsOverdue);

        Capture(window, "aufgaben-tab.png");

        // Dialog capture for the PR: add ETB entry and open task dialog in one step.
        vm.Etb.NewText = "Feuer im 2. OG";
        vm.Etb.NewFrom = "ILS";
        vm.Etb.AddEntryAndCreateTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.PendingTaskDialog);
        Capture(window, "aufgaben-dialog.png");
    }
}
