using System.Globalization;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Tasks;
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
            "/x.lagebuch",
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

        // The modules the layout predates slot in behind their default predecessors, so the
        // layout's own three entries keep their relative order rather than their positions.
        var headers = Headers(vm).ToList();
        Assert.True(headers.IndexOf("ETB") < headers.IndexOf("NACHBEREITUNG"));
        Assert.True(headers.IndexOf("NACHBEREITUNG") < headers.IndexOf("DATEIEN"));
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

    // #670: the dot is colour-only, so the name a screen reader announces carries the state.
    [Fact]
    public void A_modules_accessible_name_is_its_header()
    {
        var vm = Workspace(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });

        var module = vm.NavItems.First(i => !i.IsChecklist);

        Assert.Equal(module.Header, module.AccessibleName);
    }

    [Fact]
    public void A_checklists_accessible_name_carries_its_state_and_follows_it()
    {
        var vm = Workspace(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });
        var item = vm.NavItems.First(i => i.IsChecklist);
        var checklist = (ChecklistViewModel)item.Content;
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal("NACHBEREITUNG – unvollständig", item.AccessibleName);

        checklist.Items[0].IsDone = true;

        Assert.Equal("NACHBEREITUNG – vollständig", item.AccessibleName);
        Assert.Contains(nameof(WorkspaceNavItemViewModel.AccessibleName), raised);
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

    // ===== Per-Einsatz rail edits: hide, show again, add a Checkliste from the Stammdaten. =====
    private static readonly Guid Vorlage = new("44444444-4444-4444-4444-444444444444");

    private static ChecklistTemplate Template(Guid id, string title) =>
        new(id, title, new[] { new ChecklistTemplateItem("Punkt", true) });

    private static (IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) Editable(
        IReadOnlyList<ChecklistSeed>? seeds = null,
        IReadOnlyList<ChecklistTemplate>? templates = null)
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            seeds ?? Array.Empty<ChecklistSeed>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty with { ChecklistTemplates = templates ?? Array.Empty<ChecklistTemplate>() },
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
        return (vm, session);
    }

    private static WorkspaceNavItemViewModel Entry(IncidentWorkspaceViewModel vm, string railKey) =>
        vm.NavItems.Single(i => string.Equals(i.RailKey, railKey, StringComparison.Ordinal));

    private static string KeyOf(Guid listId) => listId.ToString("D", CultureInfo.InvariantCulture);

    [Fact]
    public void Hiding_an_entry_removes_it_from_the_rail_and_lists_it_as_hidden()
    {
        var (vm, _) = Editable();

        vm.HideNavItemCommand.Execute(Entry(vm, NavModules.Files));

        Assert.DoesNotContain(vm.NavItems, i => i.RailKey == NavModules.Files);
        var hidden = Assert.Single(vm.HiddenNavEntries);
        Assert.Equal(NavModules.Files, hidden.RailKey);
        Assert.Null(hidden.Template);
        Assert.Equal(ModuleLabels.Header(NavModules.Files), hidden.Header);
        Assert.True(vm.HasAddableNavEntries);
    }

    [Fact]
    public void Hiding_the_open_entry_opens_its_neighbour()
    {
        var (vm, _) = Editable();
        var keys = vm.NavItems.Select(i => i.RailKey).ToList();
        var middle = keys.IndexOf(NavModules.Scba);
        vm.SelectedNavItem = vm.NavItems[middle];

        vm.HideNavItemCommand.Execute(vm.SelectedNavItem);

        Assert.Equal(keys[middle + 1], vm.SelectedNavItem?.RailKey);
        Assert.True(vm.SelectedNavItem?.IsSelected);

        // The last entry has no next one, so the previous one opens.
        var last = vm.NavItems[^1];
        var beforeLast = vm.NavItems[^2].RailKey;
        vm.SelectedNavItem = last;

        vm.HideNavItemCommand.Execute(last);

        Assert.Equal(beforeLast, vm.SelectedNavItem?.RailKey);
    }

    [Fact]
    public void The_etb_cannot_be_hidden()
    {
        var (vm, _) = Editable();
        var etb = Entry(vm, NavModules.Etb);

        Assert.False(etb.CanHide);
        Assert.False(vm.HideNavItemCommand.CanExecute(etb));
        Assert.True(vm.HideNavItemCommand.CanExecute(Entry(vm, NavModules.Files)));
    }

    [Fact]
    public void Atemschutz_with_an_active_trupp_cannot_be_hidden_until_the_trupp_is_back()
    {
        var (vm, session) = Editable();
        Assert.True(Entry(vm, NavModules.Scba).CanHide);
        session.AddScbaTrupp("Angriffstrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300);
        var trupp = session.Incident.ScbaTrupps.Single().Id;

        session.StartScbaTrupp(trupp);

        Assert.False(Entry(vm, NavModules.Scba).CanHide);
        Assert.False(vm.HideNavItemCommand.CanExecute(Entry(vm, NavModules.Scba)));

        session.WithdrawScbaTrupp(trupp);

        Assert.False(Entry(vm, NavModules.Scba).CanHide);

        session.MarkScbaRemoved(trupp);

        Assert.True(Entry(vm, NavModules.Scba).CanHide);
        Assert.True(vm.HideNavItemCommand.CanExecute(Entry(vm, NavModules.Scba)));
    }

    // The safety net: work that arrives while this Einsatz has the module hidden (a peer's hide, an
    // older file) brings it back until the work is done. A Stammdaten switch-off is left alone;
    // IncidentWorkspaceViewModelTests pins that.
    [Fact]
    public void A_trupp_going_in_brings_back_an_atemschutz_hidden_in_this_einsatz_until_it_is_out()
    {
        var (vm, session) = Editable();
        vm.HideNavItemCommand.Execute(Entry(vm, NavModules.Scba));
        session.AddScbaTrupp("Angriffstrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300);
        var trupp = session.Incident.ScbaTrupps.Single().Id;

        session.StartScbaTrupp(trupp);

        Assert.False(Entry(vm, NavModules.Scba).CanHide);

        session.WithdrawScbaTrupp(trupp);
        session.MarkScbaRemoved(trupp);

        Assert.DoesNotContain(vm.NavItems, i => i.RailKey == NavModules.Scba);
    }

    [Fact]
    public void Aufgaben_with_an_open_task_cannot_be_hidden()
    {
        var (vm, session) = Editable();

        session.AddTask("Drehleiter anfordern", null, TaskImportance.High, TaskUrgency.High, 5);

        Assert.False(Entry(vm, NavModules.Tasks).CanHide);
        Assert.False(vm.HideNavItemCommand.CanExecute(Entry(vm, NavModules.Tasks)));
    }

    [Fact]
    public void A_read_only_einsatz_offers_no_hiding_and_no_adding()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var editable = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.lagebuch", Array.Empty<ChecklistSeed>());
        editable.SetNavVisibility(NavModules.Files, false);
        var readOnly = LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");
        var vm = new IncidentWorkspaceViewModel(
            readOnly,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty with { ChecklistTemplates = new[] { Template(Vorlage, "Hochwasser") } },
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());

        Assert.All(vm.NavItems, i => Assert.False(i.CanHide));
        Assert.All(vm.NavItems, i => Assert.False(vm.HideNavItemCommand.CanExecute(i)));
        Assert.Empty(vm.HiddenNavEntries);
        Assert.Empty(vm.AddableChecklists);
        Assert.False(vm.HasAddableNavEntries);
        Assert.False(vm.AddNavEntryCommand.CanExecute(new AddableNavEntryViewModel("DATEIEN", NavModules.Files, null)));
    }

    [Fact]
    public void Adding_a_hidden_entry_shows_it_again()
    {
        var (vm, session) = Editable();
        vm.HideNavItemCommand.Execute(Entry(vm, NavModules.Files));

        vm.AddNavEntryCommand.Execute(Assert.Single(vm.HiddenNavEntries));

        Assert.Contains(vm.NavItems, i => i.RailKey == NavModules.Files);
        Assert.Empty(vm.HiddenNavEntries);
        Assert.False(vm.HasAddableNavEntries);
        Assert.True(session.Incident.NavOverrides[NavModules.Files]);
    }

    [Fact]
    public void Templates_already_in_the_einsatz_are_not_offered()
    {
        var (vm, _) = Editable(
            new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) },
            new[] { Template(Nachbereitung, "Nachbereitung"), Template(Vorlage, "Hochwasser") });

        var offered = Assert.Single(vm.AddableChecklists);
        Assert.Equal(Vorlage, offered.Template?.Id);
        Assert.Null(offered.RailKey);
        Assert.Equal("HOCHWASSER", offered.Header);
    }

    [Fact]
    public void Adding_a_template_puts_a_new_checkliste_on_the_rail_and_opens_it()
    {
        var (vm, session) = Editable(templates: new[] { Template(Vorlage, "Hochwasser") });

        vm.AddNavEntryCommand.Execute(Assert.Single(vm.AddableChecklists));

        var added = Entry(vm, KeyOf(Vorlage));
        Assert.Equal("HOCHWASSER", added.Header);
        Assert.Same(added, vm.SelectedNavItem);
        Assert.Equal("Punkt", Assert.Single(Assert.IsType<ChecklistViewModel>(added.Content).Items).Text);
        Assert.Empty(vm.AddableChecklists);
        Assert.Contains(session.Incident.Checklists, l => l.Id == Vorlage);
    }

    [Fact]
    public void Rebuilding_the_rail_keeps_the_module_view_models()
    {
        var (vm, _) = Editable(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });
        var scba = vm.Scba;
        var tasks = vm.Tasks;
        var checklist = Entry(vm, KeyOf(Nachbereitung)).Content;

        vm.HideNavItemCommand.Execute(Entry(vm, NavModules.Files));

        Assert.Same(scba, vm.Scba);
        Assert.Same(tasks, vm.Tasks);
        Assert.Same(scba, Entry(vm, NavModules.Scba).Content);
        Assert.Same(checklist, Entry(vm, KeyOf(Nachbereitung)).Content);
    }

    // A Checkliste that leaves the rail takes its session subscription with it (#167/#279).
    [Fact]
    public void A_hidden_checkliste_no_longer_reacts_to_changes()
    {
        var (vm, session) = Editable(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) });
        var checklist = (ChecklistViewModel)Entry(vm, KeyOf(Nachbereitung)).Content;

        vm.HideNavItemCommand.Execute(Entry(vm, KeyOf(Nachbereitung)));
        session.ToggleChecklistItem(session.Incident.Checklists.Single(l => l.Id == Nachbereitung).Items[0].Id);

        Assert.False(checklist.AllMandatoryDone);
    }

    [Fact]
    public void A_remote_hide_of_the_open_entry_opens_its_neighbour()
    {
        var (vm, session) = Editable();
        var keys = vm.NavItems.Select(i => i.RailKey).ToList();
        var files = keys.IndexOf(NavModules.Files);
        vm.SelectedNavItem = vm.NavItems[files];

        // A joined device's command, applied on the host the way CommandApplier does it.
        session.Incident.SetNavVisibility(NavModules.Files, false);
        session.SaveExternalChange();

        Assert.DoesNotContain(vm.NavItems, i => i.RailKey == NavModules.Files);
        Assert.Equal(keys[files + 1], vm.SelectedNavItem?.RailKey);
    }

    [Fact]
    public void A_refused_add_surfaces_as_a_status_not_a_crash()
    {
        var (vm, _) = Editable(
            new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht", true)) },
            new[] { Template(Nachbereitung, "Nachbereitung") });

        // Offered before another device added the same Vorlage.
        vm.AddNavEntryCommand.Execute(new AddableNavEntryViewModel("NACHBEREITUNG", null, Template(Nachbereitung, "Nachbereitung")));

        Assert.Equal("Diese Checkliste ist bereits im Einsatz.", vm.NavEditError);

        vm.HideNavItemCommand.Execute(Entry(vm, NavModules.Files));

        Assert.Null(vm.NavEditError);
    }

    [Fact]
    public void Shortcut_hints_follow_the_rail_after_a_hide()
    {
        var (vm, _) = Editable();

        vm.HideNavItemCommand.Execute(vm.NavItems[1]);

        for (var i = 0; i < vm.NavItems.Count; i++)
        {
            Assert.Equal(ShortcutRegistry.HintFor(i), vm.NavItems[i].ShortcutHint);
        }
    }
}
