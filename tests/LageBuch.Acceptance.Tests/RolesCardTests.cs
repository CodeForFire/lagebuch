using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #668: on a phone the Funktionen are cards, and a running assignment's card showed "BIS —",
// which reads as ended. The card names BIS only once the assignment has one.
public class RolesCardTests
{
    [AvaloniaFact]
    public void A_running_card_shows_no_bis_line_and_an_ended_card_does()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", AnonymizedExampleData.OperatorSurname, from: clock.Now.AddMinutes(-40), to: clock.Now.AddMinutes(-10));
        session.AssignRole("EL", AnonymizedExampleData.OperatorSurnameAlt, from: clock.Now.AddMinutes(-10));
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            MasterDataSet.Empty,
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        vm.Roles.ShowAllRoles = true; // the ended one is listed only on request
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 412, Height = 915 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        Dispatcher.UIThread.RunJobs();

        var bisLines = window.GetVisualDescendants().OfType<StackPanel>()
            .Where(p => p.Name == "RoleCardTo")
            .ToList();
        Capture(window, "roles-cards.png");

        var running = Assert.Single(bisLines, p => p.DataContext is RoleAssignmentRow { IsRunning: true });
        var ended = Assert.Single(bisLines, p => p.DataContext is RoleAssignmentRow { IsRunning: false });
        Assert.False(running.IsEffectivelyVisible);
        Assert.True(ended.IsEffectivelyVisible);
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
        frame?.SavePng(Path.Join(dir, name));
    }
}
