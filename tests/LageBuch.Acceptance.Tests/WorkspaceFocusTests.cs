using Avalonia;
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
                "/x.fwincident",
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
        var (window, _, _) = ShowWorkspace();

        Click(window, RailTab(window, "KRÄFTE"));

        window.AssertFocused(Named<Control>(window, "VehicleBox"));
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
        Assert.Same(vm.Forces, vm.SelectedNavItem?.Content);
        Assert.Equal("KRÄFTE", ((WorkspaceNavItemViewModel)SelectedRailTab(window).DataContext!).Header);
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
