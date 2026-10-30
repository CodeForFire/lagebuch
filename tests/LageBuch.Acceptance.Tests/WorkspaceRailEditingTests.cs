using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The Lagebuchführer changes this Einsatz's rail on the rail itself: a right-click (a long-press on
// a phone) hides an entry, and a „+“ after the last entry brings a hidden one back or adds a
// Checkliste created in the Stammdaten after the Einsatz began. On a phone the „+“ lives in MEHR.
public class WorkspaceRailEditingTests
{
    private static readonly Guid Hochwasser = new("44444444-4444-4444-4444-444444444444");

    private static IncidentWorkspaceViewModel Workspace() => Editable().Vm;

    private static (IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) Editable()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname),
            "/x.lagebuch",
            Array.Empty<ChecklistSeed>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new ManualTicker(),
            WorkspaceRenderHelper.MasterData() with
            {
                ChecklistTemplates = new[]
                {
                    new ChecklistTemplate(Hochwasser, "Hochwasser", new[] { new ChecklistTemplateItem("Sandsäcke angefordert?", true) }),
                },
            },
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        return (vm, session);
    }

    private static Window Show(IncidentWorkspaceViewModel vm, bool phone = false)
    {
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = phone ? 412 : 1280,
            Height = phone ? 915 : 900,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Named<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First(v => v.Name == name);

    private static TabItem RailTab(Window window, string header)
    {
        var tabs = WorkspaceRenderHelper.Tabs(window);
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            if (tabs.ContainerFromIndex(i) is TabItem { DataContext: WorkspaceNavItemViewModel item } tab
                && item.Header == header)
            {
                return tab;
            }
        }

        throw new InvalidOperationException($"no rail tab headed \"{header}\".");
    }

    private static Point CentreIn(Control control, TopLevel topLevel) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;

    // A real press and release in the control's own top level: the window, or a popup's.
    private static void Press(Control control, MouseButton button = MouseButton.Left)
    {
        var topLevel = TopLevel.GetTopLevel(control)!;
        var centre = CentreIn(control, topLevel);
        topLevel.MouseDown(centre, button);
        topLevel.MouseUp(centre, button);
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem HideItemOf(Control owner)
    {
        var menu = owner.ContextMenu;
        Assert.NotNull(menu);
        Assert.True(menu.IsOpen, "the context gesture must open the entry's menu");
        return Named<MenuItem>(menu, "RailHideItem");
    }

    private static List<string> Headers(IEnumerable<Button> buttons) =>
        buttons.Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text ?? "?").ToList();

    [AvaloniaFact]
    public void Ausblenden_in_the_rail_context_menu_hides_the_entry()
    {
        var vm = Workspace();
        var window = Show(vm);
        var files = ModuleLabels.Header(NavModules.Files);

        var tab = RailTab(window, files);
        Press(tab, MouseButton.Right);

        var hide = HideItemOf(tab);
        Assert.Equal("Ausblenden", hide.Header);
        Assert.True(hide.IsEffectivelyEnabled);
        Press(hide);

        Assert.DoesNotContain(files, WorkspaceRenderHelper.RailHeaders(window));
        Assert.Contains(vm.HiddenNavEntries, e => e.RailKey == NavModules.Files);
    }

    [AvaloniaFact]
    public void A_refused_ausblenden_is_shown_in_the_header_not_in_a_closed_flyout()
    {
        var (vm, session) = Editable();
        var window = Show(vm);

        // The domain caps an Einsatz's overrides; at the cap, a new key is refused.
        for (var i = 0; i < Incident.MaxNavOverrides; i++)
        {
            session.SetNavVisibility($"x{i}", true);
        }

        Dispatcher.UIThread.RunJobs();
        var tab = RailTab(window, ModuleLabels.Header(NavModules.Files));
        Press(tab, MouseButton.Right);
        Press(HideItemOf(tab));

        Assert.False(Named<Button>(window, "RailAddButton").Flyout!.IsOpen);
        var banner = Named<Border>(window, "NavEditErrorBanner");
        Assert.True(banner.IsEffectivelyVisible, "the refusal must be readable without opening anything");
        Assert.Equal(
            $"Mehr als {Incident.MaxNavOverrides} Navigationsüberschreibungen sind nicht möglich.",
            Named<TextBlock>(banner, "NavEditErrorText").Text);
        Assert.Contains(ModuleLabels.Header(NavModules.Files), WorkspaceRenderHelper.RailHeaders(window));

        Press(Named<Button>(banner, "DismissNavEditErrorButton"));

        Assert.False(banner.IsEffectivelyVisible);
        Assert.Null(vm.NavEditError);
    }

    // Another device hides an entry while the Lagebuchführer is typing here: the rail updates
    // around the open module, and the caret stays where it was.
    [AvaloniaFact]
    public void A_remote_rail_change_keeps_focus_in_the_etb()
    {
        var (vm, session) = Editable();
        var window = Show(vm);
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var box = Named<TextBox>(window, "EtbTextBox");
        box.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.AssertFocused(box);

        // A joined device's command, applied on the host the way CommandApplier does it.
        session.Incident.SetNavVisibility(NavModules.Files, false);
        session.SaveExternalChange();
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(ModuleLabels.Header(NavModules.Files), WorkspaceRenderHelper.RailHeaders(window));
        window.AssertFocused(Named<TextBox>(window, "EtbTextBox"));
    }

    [AvaloniaFact]
    public void The_etb_context_menu_item_is_disabled()
    {
        var window = Show(Workspace());

        var tab = RailTab(window, ModuleLabels.Header(NavModules.Etb));
        Press(tab, MouseButton.Right);

        Assert.False(HideItemOf(tab).IsEffectivelyEnabled, "the ETB is the legal record and never leaves the rail");
    }

    [AvaloniaFact]
    public void The_plus_button_has_an_automation_name()
    {
        var window = Show(Workspace());

        var plus = Named<Button>(window, "RailAddButton");

        Assert.True(plus.IsEffectivelyVisible, "a Checkliste is addable, so the „+“ shows");
        Assert.Equal("Modul hinzufügen", AutomationProperties.GetName(plus));
    }

    [AvaloniaFact]
    public void The_plus_flyout_lists_hidden_entries_and_new_checklisten()
    {
        var vm = Workspace();
        var window = Show(vm);
        var files = ModuleLabels.Header(NavModules.Files);
        vm.HideNavItemCommand.Execute(vm.NavItems.Single(i => i.RailKey == NavModules.Files));
        Dispatcher.UIThread.RunJobs();

        var plus = Named<Button>(window, "RailAddButton");
        Press(plus);
        Assert.True(plus.Flyout!.IsOpen);

        var content = (Control)((Flyout)plus.Flyout).Content!;
        Assert.Equal(new[] { files }, Headers(Named<ItemsControl>(content, "HiddenNavList").GetVisualDescendants().OfType<Button>()));
        var checklists = Named<ItemsControl>(content, "AddableChecklistList").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(new[] { "HOCHWASSER" }, Headers(checklists));

        Press(checklists[0]);

        Assert.False(plus.Flyout.IsOpen, "a choice is made in one tap");
        Assert.Equal("HOCHWASSER", vm.SelectedNavItem?.Header);
        Assert.Contains("HOCHWASSER", WorkspaceRenderHelper.RailHeaders(window));
    }

    [AvaloniaFact]
    public void The_plus_is_hidden_when_nothing_is_addable()
    {
        var vm = Workspace();
        var window = Show(vm);

        vm.AddNavEntryCommand.Execute(Assert.Single(vm.AddableChecklists));
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named<Button>(window, "RailAddButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_more_flyout_offers_modul_hinzufuegen_on_a_phone()
    {
        var vm = Workspace();
        var window = Show(vm, phone: true);
        Assert.True(vm.IsNarrow);
        Assert.False(Named<Button>(window, "RailAddButton").IsEffectivelyVisible, "the phone has no rail to put a „+“ on");

        var more = Named<Button>(window, "MoreModulesButton");
        Press(more);
        var add = Named<Button>((Control)((Flyout)more.Flyout!).Content!, "MoreAddEntry");
        Assert.Equal("MODUL HINZUFÜGEN", add.Content);
        Assert.True(add.IsEffectivelyVisible);

        Press(add);
        Assert.True(add.Flyout!.IsOpen);
        var content = (Control)((Flyout)add.Flyout).Content!;
        var checklist = Named<ItemsControl>(content, "AddableChecklistList").GetVisualDescendants().OfType<Button>().Single();
        Press(checklist);

        Assert.False(more.Flyout.IsOpen, "the choice is made, so MEHR gives the screen back");
        Assert.Equal("HOCHWASSER", vm.SelectedNavItem?.Header);
    }

    [AvaloniaFact]
    public void A_phone_nav_cell_carries_the_ausblenden_menu()
    {
        var vm = Workspace();
        var window = Show(vm, phone: true);

        var cell = Named<ItemsControl>(window, "PhoneNavCells").GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is WorkspaceNavItemViewModel { RailKey: not NavModules.Etb });
        var item = (WorkspaceNavItemViewModel)cell.DataContext!;

        // A long-press raises ContextRequested; headless input has no touch hold to send.
        cell.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();
        Press(HideItemOf(cell));

        Assert.DoesNotContain(vm.NavItems, i => i.RailKey == item.RailKey);
    }
}
