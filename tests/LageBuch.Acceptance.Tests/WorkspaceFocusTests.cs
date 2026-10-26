using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #542: focus stays where the user left it. A module takes focus only when the user asked for it
// (a click on its rail tab, a warning bar); arrowing the rail, a rebuild and a lost connection
// leave the user where they were.
public class WorkspaceFocusTests
{
    private static (Window Window, IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm) ShowWorkspace(
        IncidentWorkspaceViewModel? vm = null)
    {
        vm ??= new IncidentWorkspaceViewModel(
            TestSession.StartNew(
                new FakeStore(),
                new FixedClock(),
                new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
                "/x.lagebuch",
                Array.Empty<(string, bool)>(),
                Array.Empty<(string, bool)>()),
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static TabItem RailTab(Window window, string header)
    {
        var tabs = WorkspaceRenderHelper.Tabs(window);
        return tabs.GetVisualDescendants().OfType<TabItem>()
            .Single(t => t.DataContext is WorkspaceNavItemViewModel item && item.Header == header);
    }

    // ORGANISATION, FUNKTIONEN and KRÄFTE are tabs inside FÜHRUNG: opened through the rail, then clicked.
    private static void ClickTab(Window window, string header)
    {
        if (header is "ORGANISATION" or "FUNKTIONEN" or "KRÄFTE")
        {
            WorkspaceRenderHelper.SelectTab(window, "FÜHRUNG");
            Click(window, FuehrungTab(window, header));
            return;
        }

        Click(window, RailTab(window, header));
    }

    private static TabItem FuehrungTab(Window window, string header) =>
        WorkspaceRenderHelper.LeadershipTabStrip(window).Items.OfType<TabItem>().Single(t => Equals(t.Header, header));

    private static TabItem SelectedRailTab(Window window)
    {
        var tabs = WorkspaceRenderHelper.Tabs(window);
        return (TabItem)tabs.ContainerFromIndex(tabs.SelectedIndex)!;
    }

    private static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void FocusByTab(Control control)
    {
        control.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Arrowing_the_rail_keeps_focus_on_the_rail()
    {
        var (window, _, vm) = ShowWorkspace();
        var tabs = WorkspaceRenderHelper.Tabs(window);
        FocusByTab(SelectedRailTab(window));
        Assert.Equal(0, tabs.SelectedIndex);

        window.Press(PhysicalKey.ArrowDown);
        window.Press(PhysicalKey.ArrowDown);
        window.Press(PhysicalKey.ArrowDown);

        Assert.Equal(3, tabs.SelectedIndex);
        Assert.Same(vm.NavItems[3], vm.SelectedNavItem);
        window.AssertFocused(SelectedRailTab(window));
    }

    // Every module, KONTAKTE among them: no view may take focus because the rail passed over it.
    [AvaloniaFact]
    public void Arrowing_down_the_whole_rail_never_leaves_it()
    {
        var (window, _, vm) = ShowWorkspace();
        var tabs = WorkspaceRenderHelper.Tabs(window);
        FocusByTab(SelectedRailTab(window));

        for (var i = 1; i < vm.NavItems.Count; i++)
        {
            window.Press(PhysicalKey.ArrowDown);

            Assert.Equal(i, tabs.SelectedIndex);
            window.AssertFocused(SelectedRailTab(window));
        }
    }

    [AvaloniaFact]
    public void Clicking_a_rail_tab_focuses_the_module_first_field()
    {
        var (window, _, vm) = ShowWorkspace();
        vm.LeadershipTabs.SelectedTabIndex = LeadershipTabsViewModel.ForcesTab;

        Click(window, RailTab(window, "FÜHRUNG"));

        window.AssertFocused(Named<Control>(window, "VehicleBox"));
    }

    // A module's own tabs (PR A's module-tabs) behave like rail tabs: a click asks for that tab.
    [AvaloniaFact]
    public void Clicking_the_etb_tabs_focuses_each_tab_s_first_field()
    {
        var (window, _, vm) = ShowWorkspace();
        Click(window, RailTab(window, "ETB"));

        Click(window, Named<TabItem>(window, "SituationReportTab"));
        Assert.Equal(EtbTabsViewModel.SituationReportTab, vm.EtbTabs.SelectedTabIndex);
        window.AssertFocused(Named<Control>(window, "NewReportButton"));

        Click(window, Named<TabItem>(window, "JournalTab"));
        Assert.Equal(EtbTabsViewModel.JournalTab, vm.EtbTabs.SelectedTabIndex);
        window.AssertFocused(Named<Control>(window, "FromBox"));
    }

    [AvaloniaFact]
    public void Clicking_the_lagemeldung_tab_on_an_open_draft_focuses_meldender()
    {
        var (window, _, vm) = ShowWorkspace();
        vm.SituationReports.NewReportCommand.Execute(null);
        Click(window, RailTab(window, "ETB"));

        Click(window, Named<TabItem>(window, "SituationReportTab"));

        window.AssertFocused(Named<Control>(window, "ReporterBox"));
    }

    // Leaving an edited field saves the draft while focus is on its way to the next control. The
    // save used to hide the editor for a moment, so focus landed nowhere, and the control kept a
    // focus adorner it never lost: the next Tab onto it crashed the app with "Border already has a
    // visual parent AdornerLayer".
    [AvaloniaFact]
    public void Tab_from_an_edited_lagemeldung_field_lands_on_the_next_control_every_time()
    {
        var (window, _, vm) = ShowWorkspace();
        vm.SituationReports.NewReportCommand.Execute(null);
        Click(window, RailTab(window, "ETB"));
        Click(window, Named<TabItem>(window, "SituationReportTab"));
        var location = Named<AutoCompleteBox>(window, "LocationBox");
        var spell = window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Einsatzstelle buchstabieren");

        for (var lap = 0; lap < 2; lap++)
        {
            FocusByTab(location);
            window.Type("x");
            window.Tab();

            window.AssertFocused(spell);
        }
    }

    // A click on one of Führung's own tabs asks for that tab the way a rail click asks for a module.
    [AvaloniaFact]
    public void Clicking_the_kraefte_tab_inside_fuehrung_focuses_its_first_field()
    {
        var (window, _, vm) = ShowWorkspace();

        ClickTab(window, "KRÄFTE");

        Assert.Equal(LeadershipTabsViewModel.ForcesTab, vm.LeadershipTabs.SelectedTabIndex);
        window.AssertFocused(Named<Control>(window, "VehicleBox"));
    }

    // The board has no entry dock, so a click on ORGANISATION leaves focus on the tab clicked.
    [AvaloniaFact]
    public void Clicking_the_organisation_tab_keeps_focus_on_that_tab()
    {
        var (window, _, vm) = ShowWorkspace();
        ClickTab(window, "KRÄFTE");

        ClickTab(window, "ORGANISATION");

        Assert.Equal(LeadershipTabsViewModel.OrganisationTab, vm.LeadershipTabs.SelectedTabIndex);
        window.AssertFocused(FuehrungTab(window, "ORGANISATION"));
    }

    // Field test: the list a suggestion box used to open on focus covered the rows the
    // Lagebuchführer had switched to the module to read. It now stays closed, as KRÄFTE's ComboBox does.
    [AvaloniaTheory]
    [InlineData("ETB", "FromBox")]
    [InlineData("FUNKTIONEN", "RoleBox")]
    [InlineData("ATEMSCHUTZ", "CallSignBox")]
    public void Clicking_a_rail_tab_leaves_the_first_field_suggestion_list_closed(string header, string firstField)
    {
        var (window, _, _) = ShowWorkspace();

        ClickTab(window, header);

        var box = Named<AutoCompleteBox>(window, firstField);
        window.AssertFocused(box);
        Assert.False(box.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void Clicking_the_kontakte_tab_puts_the_caret_in_the_search_box()
    {
        var (window, _, _) = ShowWorkspace();

        Click(window, RailTab(window, "KONTAKTE"));

        window.AssertFocused(Named<TextBox>(window, "ContactSearchBox"));
    }

    [AvaloniaFact]
    public void Clicking_the_links_tab_puts_the_caret_in_the_search_box()
    {
        var (window, _, _) = ShowWorkspace();

        Click(window, RailTab(window, "LINKS"));

        window.AssertFocused(Named<TextBox>(window, "LinkSearchBox"));
    }

    [AvaloniaFact]
    public void Clicking_the_beteiligte_tab_focuses_name()
    {
        var (window, _, _) = ShowWorkspace();

        Click(window, RailTab(window, "BETEILIGTE"));

        window.AssertFocused(Named<Control>(window, "NewNameBox"));
    }

    [AvaloniaFact]
    public void The_task_due_bar_focuses_wichtigkeit()
    {
        var (window, view, _) = ShowWorkspace(WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true));

        Click(window, view.GetControl<Button>("TaskDueJumpButton"));

        window.AssertFocusWithin(Named<Control>(window, "ImportanceBox"));
    }

    [AvaloniaFact]
    public void A_read_only_flip_keeps_the_selected_module_and_rail_focus()
    {
        // Headless tests cannot build a RemoteIncidentSession; closing runs the same rebuild a
        // synced read-only flip does (the sync side is WorkspaceCollaborationTests).
        var (window, _, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        FocusByTab(SelectedRailTab(window));

        vm.CloseIncidentCommand.Execute(null);
        vm.PendingConfirm!.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsReadOnly);
        Assert.Same(vm.LeadershipTabs, vm.SelectedNavItem?.Content);
        Assert.Equal(LeadershipTabsViewModel.ForcesTab, vm.LeadershipTabs.SelectedTabIndex);
        Assert.Equal("FÜHRUNG", ((WorkspaceNavItemViewModel)SelectedRailTab(window).DataContext!).Header);
        window.AssertFocused(SelectedRailTab(window));
    }

    [AvaloniaFact]
    public void Reconnecting_restores_focus_to_the_field_it_left()
    {
        var (window, _, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var from = Named<AutoCompleteBox>(window, "FromBox");
        FocusByTab(from);

        vm.IsConnected = false;
        Dispatcher.UIThread.RunJobs();
        vm.IsConnected = true;
        Dispatcher.UIThread.RunJobs();

        window.AssertFocused(from);
    }

    [AvaloniaFact]
    public void Toggling_the_etb_filter_keeps_the_selected_row()
    {
        var (window, _, vm) = ShowWorkspace();
        vm.Etb.NewText = "Erste Lagemeldung";
        vm.Etb.AddEntryCommand.Execute(null);
        vm.Etb.NewText = "Wasserversorgung steht";
        vm.Etb.AddEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var grid = Named<DataGrid>(window, "EtbGrid");
        var selected = vm.Etb.Entries.Single(e => e.Text == "Erste Lagemeldung");
        grid.SelectedItem = selected;
        Dispatcher.UIThread.RunJobs();

        vm.Etb.HideSystemEntries = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(selected, grid.SelectedItem);
    }
}
