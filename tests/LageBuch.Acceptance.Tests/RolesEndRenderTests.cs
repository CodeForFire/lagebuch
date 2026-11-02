using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// FUNKTIONEN ends a Funktion nobody takes over (#823): BEENDEN on the row, then a question below the
// grid in the transfer panel's place. Doubles as the PR screenshot capture.
public class RolesEndRenderTests
{
    [AvaloniaFact]
    public void Beenden_on_a_row_asks_below_the_grid_before_anything_ends()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Mustermann, Max", "Florian Emmering 1/1", from: clock.Now, slot: RoleSlot.Leiter);
        session.AssignRole("EAL", "Musterfrau, Erika", "Florian Aich 40/1", from: clock.Now, section: "Nord", slot: RoleSlot.Leiter);

        var md = MasterDataSet.Empty with
        {
            Roles = new[] { new Role("EL", RoleUniqueness.UniquePerIncident, RoleSlot.Leiter), new Role("EAL", RoleUniqueness.Multiple, RoleSlot.Leiter) },
        };
        var vm = new IncidentWorkspaceViewModel(
            session, clock, new NoopTicker(), md, new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        Dispatcher.UIThread.RunJobs();

        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "EndRowButton").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal("EAL (Musterfrau, Erika) beenden", AutomationProperties.GetName(buttons[1]));
        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "EndPanel");
        Assert.False(panel.IsVisible);
        Capture(window, "funktionen-beenden-row.png");

        buttons[1].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(panel.IsVisible);
        Assert.Equal(
            "Funktion EAL (Musterfrau, Erika) beenden?",
            window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EndQuestionText").Text);
        Assert.All(session.Incident.Roles, r => Assert.Null(r.To));
        Capture(window, "funktionen-beenden-confirm.png");
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }
}
