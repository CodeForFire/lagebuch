using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        var session = TestSession.StartNew(
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

        // Brush transitions (the triage segments' fill) run on the wall clock, not the dispatcher;
        // only a screenshot needs them finished.
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void Workspace_rail_carries_an_aufgaben_tab()
    {
        var (window, _, _, _, _) = ShowWorkspace();
        var headers = WorkspaceRenderHelper.RailHeaders(window);

        Assert.Equal(12, headers.Count);
        Assert.Contains("AUFGABEN", headers);
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

        WorkspaceRenderHelper.SelectTab(window, "AUFGABEN");

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

    // #246: editing opens a panel in the dock's place; the grid above keeps reading as triage, and
    // the chosen segments fill in the colour the grid uses for that level.
    [AvaloniaFact]
    public void Editing_an_aufgabe_shows_the_panel_with_filled_segments_in_the_docks_place()
    {
        var (window, vm, session, ticker, clock) = ShowWorkspace();
        session.AddTask("Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        session.AddTask("Presse-Info vorbereiten", null, TaskImportance.Low, TaskUrgency.Medium, 15);
        session.AddTask("Gerät nachlegen", null, TaskImportance.Low, TaskUrgency.Low, 30);
        clock.Now = clock.Now.AddMinutes(6); // Tür sichern overdue: the fällig bar shows +5 MIN
        ticker.Pulse();
        WorkspaceRenderHelper.SelectTab(window, "AUFGABEN");
        var row = vm.Tasks.Rows.Single(r => r.Text == "Presse-Info vorbereiten");

        row.BeginEditCommand.Execute(null);
        vm.Tasks.EditImportance = TaskImportance.High;
        Dispatcher.UIThread.RunJobs();

        var panel = Named<Border>(window, "TaskEditPanel");
        Assert.True(panel.IsEffectivelyVisible);
        Assert.False(Named<StackPanel>(window, "DockFields").IsEffectivelyVisible);
        Assert.True(Named<Button>(window, "TaskDueExtendButton").IsEffectivelyVisible);
        var importance = Named<ListBox>(window, "EditImportanceBox");
        Assert.Equal(2, importance.SelectedIndex); // Hoch
        Assert.Equal(1, Named<ListBox>(window, "EditUrgencyBox").SelectedIndex); // Mittel
        Capture(window, "aufgaben-bearbeiten.png");

        window.Width = 412;
        window.Height = 915;
        Dispatcher.UIThread.RunJobs();
        Capture(window, "aufgaben-bearbeiten-phone.png");
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
