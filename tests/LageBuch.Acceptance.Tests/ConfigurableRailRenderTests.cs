using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The whole point of the change, rendered: a brigade whose Stammdaten define three Checklisten,
// put one of them second, and switch two modules off gets exactly that rail -- not the ten tabs
// the app used to ship.
public class ConfigurableRailRenderTests
{
    private static readonly Guid Nachbereitung = new("b7f3c611-2a58-4c9e-8d40-5f1a2b3c4d5e");
    private static readonly Guid Fahrzeug = new("c8e4d722-3b69-4d1f-9e51-6a2b3c4d5e6f");

    private static ChecklistSeed Seed(Guid id, string title, params (string Text, bool IsMandatory)[] items) =>
        new(id, title, items);

    private static MasterDataSet CustomLayout() => MasterDataSet.Empty with
    {
        Navigation = new[]
        {
            new NavEntry(NavModules.Checklist, ChecklistDefaults.AufbauListId, true),
            new NavEntry(NavModules.Etb, null, true),
            new NavEntry(NavModules.Checklist, Nachbereitung, true),
            new NavEntry(NavModules.Tasks, null, true),
            new NavEntry(NavModules.Forces, null, true),
            new NavEntry(NavModules.Roles, null, false),
            new NavEntry(NavModules.Scba, null, false),
            new NavEntry(NavModules.Co, null, false),
            new NavEntry(NavModules.Files, null, true),
            new NavEntry(NavModules.Links, null, true),
            new NavEntry(NavModules.Checklist, Fahrzeug, true),
        },
    };

    private static Window ShowWorkspace()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[]
            {
                Seed(ChecklistDefaults.AufbauListId, "Aufbau", ("Aufstellort ELW frei?", true)),
                Seed(Nachbereitung, "Nachbereitung", ("Einsatzbericht geschrieben?", false)),
                Seed(Fahrzeug, "Fahrzeug", ("Tank gefüllt?", true), ("Schläuche getauscht?", false)),
            },
            keyword: "B3P");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            CustomLayout(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_rail_is_exactly_what_stammdaten_configured()
    {
        var window = ShowWorkspace();

        Assert.Equal(
            new[]
            {
                "AUFBAU", "ETB", "NACHBEREITUNG", "AUFGABEN", "KRÄFTE",
                "DATEIEN", "LINKS", "FAHRZEUG",
            },
            WorkspaceRenderHelper.RailHeaders(window));
    }

    [AvaloniaFact]
    public void A_checklist_named_by_stammdaten_opens_under_its_own_name()
    {
        var window = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "FAHRZEUG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        var texts = content.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Tank gefüllt?", texts);
        Assert.Contains("Schläuche getauscht?", texts);

        // Its heading is the list's own name, where it used to read a fixed "CHECKLISTE".
        Assert.Contains("FAHRZEUG", texts);
        Assert.DoesNotContain("CHECKLISTE", texts);
    }

    [AvaloniaFact]
    public void Each_checklist_carries_its_own_completion_dot()
    {
        var window = ShowWorkspace();

        // Nachbereitung has no mandatory item, so it is complete from the start; Aufbau is not.
        Assert.True(WorkspaceRenderHelper.RailStatusDots(window, "NACHBEREITUNG").Complete.IsVisible);
        Assert.True(WorkspaceRenderHelper.RailStatusDots(window, "AUFBAU").Incomplete.IsVisible);

        // A module tab shows neither dot -- both booleans are false for it, so the template's
        // ellipses stay hidden rather than defaulting to visible.
        var (moduleComplete, moduleIncomplete) = WorkspaceRenderHelper.RailStatusDots(window, "ETB");
        Assert.False(moduleComplete.IsVisible);
        Assert.False(moduleIncomplete.IsVisible);
    }

    [AvaloniaFact]
    public void A_brigade_that_uses_no_checklisten_gets_a_rail_without_any()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<ChecklistSeed>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            MasterDataSet.Empty,
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            new[] { "ETB", "AUFGABEN", "FUNKTIONEN", "KRÄFTE", "ATEMSCHUTZ", "CO-MESSUNG", "DATEIEN", "LINKS" },
            WorkspaceRenderHelper.RailHeaders(window));
    }
}
