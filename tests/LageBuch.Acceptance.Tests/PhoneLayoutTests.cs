using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The phone contract, in one place. A Bavarian ILS phone is ~411dp wide (Medium_Phone_API_35:
// 1080px @ 420dpi); these render at 412x915, the size `make phone-screenshots` uses.
//
// Every test here asks the same question in a different module: is the thing the Lagebuchführer
// has to reach actually inside the viewport? That is the failure the whole phone layout exists to
// fix — before it, the ETB showed nothing but its ZEIT column and the Atemschutz tab showed no
// Trupp at all, because the grids are budgeted for a 1280px ELW monitor.
//
// The desktop counterpart of each assertion lives with the feature it belongs to
// (LayoutAlignmentTests, HeaderLayoutTests, ModuleTabsScrollingTests); nothing here runs wide.
public class PhoneLayoutTests
{
    private const double PhoneWidth = 412.0;
    private const double PhoneHeight = 915.0;

    private static Window PhoneWindow(Control content)
    {
        var window = new Window { Content = content, Width = PhoneWidth, Height = PhoneHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Named<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First(v => v.Name == name);

    private static double RightEdge(Visual v, Visual relativeTo) =>
        v.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X + v.Bounds.Width;

    /// <summary>
    /// Every laid-out control sits inside the viewport. This is the invariant the whole phone
    /// layout exists for, and it catches an overflow anywhere in the tree rather than only where
    /// someone thought to look.
    /// </summary>
    private static void AssertNothingOverflows(Window window, Visual root)
    {
        var offenders = root.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)

            // Only what the views themselves declare. A control's own template parts are its
            // business and are clipped by it — ComboBox's DropDownGlyph, for one, reports a
            // translated x in the thousands while the ComboBox around it sits well inside the
            // viewport, and chasing that says nothing about whether the layout fits.
            .Where(c => c.TemplatedParent is null)
            .Select(c => (Control: c, Right: RightEdge(c, window)))

            // A hairline of rounding is not an overflow; a clipped control is.
            .Where(x => x.Right > PhoneWidth + 1.0)
            .Select(x => $"{Describe(x.Control)} ends at x={x.Right:0}")
            .Take(8)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"these controls run past the {PhoneWidth:0}dp viewport: {string.Join("; ", offenders)}");
    }

    /// <summary>
    /// A control plus the nearest named thing above it — "Ellipse" alone names nothing you can
    /// go and fix.
    /// </summary>
    private static string Describe(Control control)
    {
        var self = control.GetType().Name
            + (string.IsNullOrEmpty(control.Name) ? string.Empty : $"#{control.Name}");
        var owners = control.GetVisualAncestors()
            .OfType<Control>()
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .Take(4)
            .Select(a => $"{a.GetType().Name}#{a.Name}");
        return $"{self} (under {string.Join(" < ", owners)})";
    }

    [AvaloniaFact]
    public void The_bottom_bar_replaces_the_rail_and_every_cell_is_reachable()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = PhoneWindow(view);

        Assert.True(vm.IsNarrow, "the workspace must know it is laid out for a phone");

        var bar = Named<Border>(view, "PhoneNavBar");
        Assert.True(bar.IsEffectivelyVisible, "the bottom nav bar must take over from the rail");
        Assert.True(RightEdge(bar, window) <= PhoneWidth + 1.0);

        // The rail itself is gone: its TabItems are hidden, so the 172px column costs nothing.
        var tabs = WorkspaceRenderHelper.Tabs(window);
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            var container = tabs.ContainerFromIndex(i);
            Assert.False(
                container!.IsEffectivelyVisible,
                "a rail tab is still taking width from the content on a phone");
        }

        // Four cells plus MEHR, and the rest of the rail is behind it rather than lost.
        Assert.Equal(4, vm.PrimaryNavItems.Count);
        Assert.True(vm.HasOverflowNavItems);
        Assert.Equal(
            vm.NavItems.Count,
            vm.PrimaryNavItems.Count + vm.OverflowNavItems.Count);
    }

    [AvaloniaFact]
    public void The_bottom_bar_opens_the_module_it_names()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        _ = PhoneWindow(new IncidentWorkspaceView { DataContext = vm });

        var target = vm.PrimaryNavItems[2];
        vm.SelectNavItemCommand.Execute(target);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(target, vm.SelectedNavItem);
        Assert.True(target.IsSelected, "the open cell has to light up");
        Assert.All(
            vm.NavItems.Where(i => !ReferenceEquals(i, target)),
            i => Assert.False(i.IsSelected));
    }

    [AvaloniaFact]
    public void A_checkliste_behind_MEHR_still_reports_its_status()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        _ = PhoneWindow(new IncidentWorkspaceView { DataContext = vm });

        // The shipped rail puts both Checklisten past the fourth slot, and an unfinished one is
        // exactly what the operator must not lose sight of behind an overflow button.
        Assert.Contains(vm.OverflowNavItems, i => i.IsComplete || i.IsIncomplete);
        Assert.True(
            vm.OverflowHasStatusDot,
            "MEHR must carry a dot while anything hidden behind it has one");
    }

    [AvaloniaTheory]
    [InlineData("ETB")]
    [InlineData("KRÄFTE")]
    [InlineData("ATEMSCHUTZ")]
    [InlineData("AUFGABEN")]
    [InlineData("FUNKTIONEN")]
    [InlineData("DATEIEN")]
    [InlineData("CO-MESSUNG")]
    [InlineData("LINKS")]
    [InlineData("BETEILIGTE")]
    [InlineData("KONTAKTE")]
    [InlineData("AUFBAU")]
    public void No_module_runs_past_the_phone_viewport(string header)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = PhoneWindow(view);

        WorkspaceRenderHelper.SelectTab(window, header);
        Dispatcher.UIThread.RunJobs();

        AssertNothingOverflows(window, view);
    }

    [AvaloniaTheory]
    [InlineData("ETB")]
    [InlineData("KRÄFTE")]
    [InlineData("ATEMSCHUTZ")]
    [InlineData("AUFGABEN")]
    [InlineData("FUNKTIONEN")]
    [InlineData("BETEILIGTE")]
    public void A_dock_becomes_a_sheet_that_opens_and_closes(string header)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = PhoneWindow(view);
        WorkspaceRenderHelper.SelectTab(window, header);
        Dispatcher.UIThread.RunJobs();

        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var open = Named<Button>(content, "OpenComposerButton");

        // Closed to begin with: the list is what the module is for, and a dock that is always
        // there costs it half the screen.
        Assert.True(open.IsEffectivelyVisible, $"{header} has no way to open its dock on a phone");

        open.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(open.IsEffectivelyVisible);

        // Looked up only now: a collapsed dock is not realized, so before this point there is no
        // close button anywhere in the visual tree to find.
        var close = Named<Button>(content, "CloseComposerButton");
        Assert.True(close.IsEffectivelyVisible);

        // Still inside the viewport with every field stacked and the keyboard yet to appear.
        AssertNothingOverflows(window, view);

        close.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(open.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Adding_an_etb_entry_closes_the_sheet_again()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = PhoneWindow(new IncidentWorkspaceView { DataContext = vm });
        WorkspaceRenderHelper.SelectTab(window, "ETB");

        var etb = vm.Etb;
        Assert.True(etb.IsNarrow, "the workspace must hand the flag down to its modules");

        etb.OpenComposerCommand.Execute(null);
        Assert.True(etb.IsComposerOpen);

        etb.NewText = "Lagemeldung";
        etb.AddEntryCommand.Execute(null);

        Assert.False(etb.IsComposerOpen, "a saved entry must give the list back");
        Assert.True(etb.ShowComposerButton);
    }

    [AvaloniaFact]
    public void A_rejected_etb_entry_keeps_the_sheet_open()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = PhoneWindow(new IncidentWorkspaceView { DataContext = vm });
        WorkspaceRenderHelper.SelectTab(window, "ETB");

        var etb = vm.Etb;
        etb.OpenComposerCommand.Execute(null);
        etb.NewText = string.Empty;
        etb.AddEntryCommand.Execute(null);

        Assert.True(etb.IsComposerOpen, "the sheet must stay open so the complaint can be read");
        Assert.NotNull(etb.ErrorSummary);
    }

    [AvaloniaFact]
    public void Every_module_that_lays_out_for_a_phone_is_told_that_it_is_on_one()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        _ = PhoneWindow(new IncidentWorkspaceView { DataContext = vm });

        // The propagation list is written by hand, so a module can implement INarrowAware and be
        // forgotten — CO-Messung was, and its Wohnung editor would silently never take the width
        // it needs. Ask the modules themselves instead of trusting the list.
        object?[] modules =
        [
            vm.Etb, vm.Tasks, vm.Roles, vm.Forces, vm.InvolvedParties, vm.Scba, vm.CoMessprotokoll, vm.Files, vm.Links,
            vm.Contacts,
        ];

        var aware = modules.OfType<INarrowAware>().ToArray();
        Assert.NotEmpty(aware);
        Assert.All(
            aware,
            m => Assert.True(
                m.IsNarrow,
                $"{m.GetType().Name} implements INarrowAware but never hears that it is on a phone"));
    }

    [AvaloniaFact]
    public void The_etb_reads_as_cards_and_the_grid_stands_down()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = PhoneWindow(new IncidentWorkspaceView { DataContext = vm });
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        Dispatcher.UIThread.RunJobs();

        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        Assert.False(
            Named<Border>(content, "EtbGridPanel").IsEffectivelyVisible,
            "the seven-column grid cannot fit 412dp and must give way");
        Assert.True(Named<Border>(content, "EtbCardPanel").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_stammdaten_editor_drills_in_and_comes_back()
    {
        var vm = new MasterDataEditorViewModel(new DemoProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = PhoneWindow(view);

        Assert.True(vm.IsNarrow, "the editor must know it is laid out for a phone");
        Assert.True(vm.ShowCategories, "the rail is the screen until a category is picked");
        Assert.False(vm.ShowDetail);
        AssertNothingOverflows(window, view);

        vm.SelectedSection = vm.Sections[1];
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.ShowCategories);
        Assert.True(vm.ShowDetail, "picking a category has to open it");

        var back = Named<Button>(view, "BackToCategoriesButton");
        Assert.True(back.IsEffectivelyVisible, "there must be a way back to the categories");
        back.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.ShowCategories);
    }

    /// <summary>Enough Stammdaten for the rail to have something to drill into.</summary>
    private sealed class DemoProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Roles = new[] { new Role("EL"), new Role("ZF") },
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/10/1", 9) },
        };

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }

    [AvaloniaFact]
    public void A_wide_window_keeps_the_rail_the_grids_and_the_docks()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsNarrow);
        Assert.False(
            Named<Border>(view, "PhoneNavBar").IsEffectivelyVisible,
            "the bottom bar must not appear on an ELW screen");
        Assert.True(WorkspaceRenderHelper.Tabs(window).ContainerFromIndex(0)!.IsEffectivelyVisible);

        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        Assert.True(Named<Border>(content, "EtbGridPanel").IsEffectivelyVisible);
        Assert.False(Named<Border>(content, "EtbCardPanel").IsEffectivelyVisible);

        // The dock is simply always there when there is room for it.
        Assert.True(vm.Etb.ShowComposer);
        Assert.False(vm.Etb.ShowComposerButton);
    }
}
