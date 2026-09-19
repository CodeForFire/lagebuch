using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// The workspace rail is built from the Stammdaten layout and the Einsatz's own Checklisten, so
// these pin the two ends the resolver cannot: that a checklist entry gets a working
// ChecklistViewModel, and that rebuilding the rail does not leak the previous generation's
// session subscriptions (#167/#279).
public class WorkspaceNavItemsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly Guid Nachbereitung = new("33333333-3333-3333-3333-333333333333");

    private static ChecklistSeed Seed(Guid id, string title, params (string Text, bool IsMandatory)[] items) =>
        new(id, title, items);

    private static IncidentWorkspaceViewModel Workspace(
        IReadOnlyList<ChecklistSeed> seeds,
        IReadOnlyList<NavEntry>? navigation = null)
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            seeds);
        return new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty with { Navigation = navigation ?? Array.Empty<NavEntry>() },
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
    }

    private static List<string> Headers(IncidentWorkspaceViewModel vm) =>
        vm.NavItems.Select(i => i.Header).ToList();

    [Fact]
    public void An_einsatz_with_no_checklisten_has_a_rail_without_any()
    {
        var vm = Workspace(Array.Empty<ChecklistSeed>());

        Assert.DoesNotContain(vm.NavItems, i => i.IsChecklist);
        Assert.Contains("ETB", Headers(vm));
    }

    [Fact]
    public void Three_checklisten_each_get_their_own_tab_titled_after_the_list()
    {
        var vm = Workspace(new[]
        {
            Seed(ChecklistDefaults.AufbauListId, "Aufbau", ("A", true)),
            Seed(Nachbereitung, "Nachbereitung", ("B", false)),
            Seed(ChecklistDefaults.AbbauListId, "Abbau", ("C", false)),
        });

        Assert.Equal(3, vm.NavItems.Count(i => i.IsChecklist));
        Assert.Contains("NACHBEREITUNG", Headers(vm));
        Assert.Contains("AUFBAU", Headers(vm));
        Assert.Contains("ABBAU", Headers(vm));
    }

    [Fact]
    public void A_module_switched_off_in_stammdaten_is_absent_from_the_rail()
    {
        var vm = Workspace(
            Array.Empty<ChecklistSeed>(),
            new[]
            {
                new NavEntry(NavModules.Etb, null, true),
                new NavEntry(NavModules.Scba, null, false),
            });

        Assert.DoesNotContain("ATEMSCHUTZ", Headers(vm));
        Assert.Contains("ETB", Headers(vm));
    }

    [Fact]
    public void The_layout_order_is_the_rail_order()
    {
        var vm = Workspace(
            new[] { Seed(Nachbereitung, "Nachbereitung", ("B", false)) },
            new[]
            {
                new NavEntry(NavModules.Etb, null, true),
                new NavEntry(NavModules.Checklist, Nachbereitung, true),
                new NavEntry(NavModules.Files, null, true),
            });

        Assert.Equal(new[] { "ETB", "NACHBEREITUNG", "DATEIEN" }, Headers(vm).Take(3));
    }

    [Fact]
    public void A_checklist_nav_item_carries_a_working_checklist_view_model()
    {
        var vm = Workspace(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });

        var item = Assert.Single(vm.NavItems, i => i.IsChecklist);
        var checklist = Assert.IsType<ChecklistViewModel>(item.Content);
        Assert.Equal("Nachbereitung", checklist.Title);
        Assert.Equal("Bericht", Assert.Single(checklist.Items).Text);
    }

    // The header dot: a checklist tab reports completion, a module tab never does. Binding a
    // module's dot through a null checklist would leave IsVisible at its default true and put a
    // status dot on every tab.
    [Fact]
    public void Only_checklist_items_report_completion()
    {
        var vm = Workspace(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });

        var module = vm.NavItems.First(i => !i.IsChecklist);
        Assert.False(module.IsComplete);
        Assert.False(module.IsIncomplete);

        var checklist = vm.NavItems.First(i => i.IsChecklist);
        Assert.False(checklist.IsComplete);
        Assert.True(checklist.IsIncomplete);
    }

    [Fact]
    public void Ticking_the_last_mandatory_item_flips_the_tabs_dot()
    {
        var vm = Workspace(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });
        var item = vm.NavItems.First(i => i.IsChecklist);
        var checklist = (ChecklistViewModel)item.Content;

        checklist.Items[0].IsDone = true;

        Assert.True(item.IsComplete);
        Assert.False(item.IsIncomplete);
    }

    // Every child subscribes to session.Changed in its constructor, so an undisposed generation
    // keeps reacting forever -- the #167/#279 failure. Disposing the workspace must take the nav
    // items and their checklists with it.
    [Fact]
    public void Disposing_the_workspace_stops_its_checklists_reacting()
    {
        var vm = Workspace(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });
        var item = vm.NavItems.First(i => i.IsChecklist);
        var checklist = (ChecklistViewModel)item.Content;

        vm.Dispose();
        checklist.Items[0].IsDone = true;

        Assert.False(item.IsComplete);
    }
}
