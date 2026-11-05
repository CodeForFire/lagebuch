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

// FUNKTIONEN shows where the Führungstafel placed a Funktion ("Abschnitt 1 / Nord") beside one that
// was only typed in. Doubles as the PR screenshot capture.
public class RolesNodeLabelRenderTests
{
    [AvaloniaFact]
    public void Funktionen_tab_shows_the_place_of_a_placed_funktion_and_the_text_of_a_free_text_one()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var abschnitt = session.Incident.CommandNodes[^1].Id;
        session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Nord", abschnitt);
        var nord = session.Incident.CommandNodes[^1].Id;
        session.AssignRole("UEAL", "Mustermann, Max", "Florian Emmering 11/1", from: clock.Now, nodeId: nord, slot: RoleSlot.Leiter);
        session.AssignRole("ZF", "Musterfrau, Erika", "Florian Aich 40/1", from: clock.Now, section: "Wache Süd");

        var md = MasterDataSet.Empty with
        {
            Roles = new[] { new Role("UEAL", RoleUniqueness.UniquePerSection, RoleSlot.Leiter), new Role("ZF") },
        };
        var vm = new IncidentWorkspaceViewModel(
            session, clock, new NoopTicker(), md, new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Roles.Roles.Count);
        var cells = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "RoleSectionText").ToList();
        Assert.Equal(["Abschnitt 1 / Nord", "Wache Süd"], cells.Select(c => c.Text));
        Assert.Equal(
            ["Unter FÜHRUNG zugeordnet", null],
            cells.Select(c => (string?)ToolTip.GetTip(c)));

        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "funktionen-node-label.png"));
    }

    // #824: the ABSCHNITT box suggests the Abschnitte FÜHRUNG has formed, and typing one places the
    // Funktion there rather than in a Freitext-Abschnitt of the same name.
    [AvaloniaFact]
    public void The_abschnitt_box_suggests_the_formed_abschnitte_and_places_a_match()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var abschnitt = session.Incident.CommandNodes[^1].Id;
        session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Nord", abschnitt);
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        var sued = session.Incident.CommandNodes[^1].Id;

        var md = MasterDataSet.Empty with
        {
            Roles = new[] { new Role("EAL", RoleUniqueness.Multiple, RoleSlot.Leiter), new Role("ZF") },
        };
        var vm = new IncidentWorkspaceViewModel(
            session, clock, new NoopTicker(), md, new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => b.Name == "SectionBox");
        Assert.Equal(["Abschnitt 1", "Abschnitt 1 / Nord", "Abschnitt 2 Süd"], Assert.IsAssignableFrom<IEnumerable<string>>(box.ItemsSource));

        vm.Roles.NewRole = "EAL";
        vm.Roles.NewPersonName = "Lechner, Georg";
        box.Text = "Abschnitt 2 Süd";
        Dispatcher.UIThread.RunJobs();
        vm.Roles.AddRoleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var eal = Assert.Single(session.Incident.Roles);
        Assert.Equal(sued, eal.NodeId);
        Assert.Equal(
            "Abschnitt 2 Süd",
            window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "RoleSectionText").Text);

        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "funktionen-abschnitt-placed.png"));
    }
}
