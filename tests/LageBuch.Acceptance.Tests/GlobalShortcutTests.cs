using Avalonia.Automation;
using Avalonia.Controls;
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

// #544: the global shortcuts, pressed as keys on the real shell. The handler sits on MainView, so
// every test hosts the workspace inside it, the way both platform heads do.
public class GlobalShortcutTests
{
    // Kräfte first and Atemschutz before the ETB: a shortcut bound to a rail position would land
    // somewhere else here.
    private static readonly NavEntry[] ReorderedNavigation =
    [
        new(NavModules.Forces, null, true),
        new(NavModules.Scba, null, true),
        new(NavModules.Etb, null, true),
        new(NavModules.Tasks, null, true),
    ];

    private static (Window Window, MainWindowViewModel Shell, IncidentWorkspaceViewModel Vm) ShowShell(
        IncidentWorkspaceViewModel? vm = null,
        MasterDataSet? masterData = null)
    {
        masterData ??= WorkspaceRenderHelper.MasterData();
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
            masterData,
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());

        var dialogs = new FakeDialogs();
        var provider = new StaticMasterData(masterData);
        var home = new HomeViewModel(
            new FakeStore(),
            provider,
            new EmptyRecent(),
            dialogs,
            new FixedClock(),
            new NoopTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "0.1.0");
        var shell = new MainWindowViewModel(home, new MasterDataEditorViewModel(provider, dialogs, new NoFiles()), dialogs, "0.1.0");
        home.WorkspaceOpened!(vm);

        var view = new MainView();
        view.AttachViewModel(shell);
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell, vm);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.IsEffectivelyVisible);

    private static TabItem SelectedRailTab(Window window)
    {
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "ModuleTabs");
        return (TabItem)tabs.ContainerFromIndex(tabs.SelectedIndex)!;
    }

    private static void FocusByTab(Control control)
    {
        control.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressAndSettle(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        // Press pumps once; the module's first field is focused in a job posted from that pump.
        window.Press(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ctrl_4_selects_Kraefte_and_focuses_its_first_field(bool reorderedRail)
    {
        var masterData = WorkspaceRenderHelper.MasterData();
        if (reorderedRail)
        {
            masterData = masterData with { Navigation = ReorderedNavigation };
        }

        var (window, _, vm) = ShowShell(masterData: masterData);
        vm.SelectedNavItem = vm.NavItems.Single(i => i.ModuleKey == NavModules.Etb);
        Dispatcher.UIThread.RunJobs();
        FocusByTab(SelectedRailTab(window));

        PressAndSettle(window, PhysicalKey.Digit4, RawInputModifiers.Control);

        Assert.Equal("KRÄFTE", vm.SelectedNavItem?.Header);
        window.AssertFocused(Named<ComboBox>(window, "VehicleBox"));
    }

    [AvaloniaFact]
    public void Ctrl_N_from_Kraefte_focuses_VON()
    {
        var (window, _, vm) = ShowShell();
        PressAndSettle(window, PhysicalKey.Digit4, RawInputModifiers.Control);
        window.AssertFocused(Named<ComboBox>(window, "VehicleBox"));

        PressAndSettle(window, PhysicalKey.N, RawInputModifiers.Control);

        Assert.Equal("ETB", vm.SelectedNavItem?.Header);
        window.AssertFocused(Named<Control>(window, "FromBox"));
    }

    [AvaloniaFact]
    public void Ctrl_N_from_elsewhere_in_the_ETB_still_focuses_VON()
    {
        var (window, _, vm) = ShowShell();
        vm.SelectedNavItem = vm.NavItems.Single(i => i.ModuleKey == NavModules.Etb);
        Dispatcher.UIThread.RunJobs();
        FocusByTab(Named<CheckBox>(window, "HideSystemCheckBox"));

        PressAndSettle(window, PhysicalKey.N, RawInputModifiers.Control);

        window.AssertFocused(Named<Control>(window, "FromBox"));
    }

    [AvaloniaFact]
    public void A_module_shortcut_does_nothing_while_a_workspace_overlay_is_open()
    {
        var (window, _, vm) = ShowShell();
        var before = vm.SelectedNavItem;
        vm.CloseIncidentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.PendingConfirm);

        PressAndSettle(window, PhysicalKey.Digit4, RawInputModifiers.Control);
        PressAndSettle(window, PhysicalKey.Tab, RawInputModifiers.Control);

        Assert.Same(before, vm.SelectedNavItem);
        Assert.NotNull(vm.PendingConfirm);
    }

    [AvaloniaFact]
    public void A_module_shortcut_does_nothing_while_the_about_overlay_is_open()
    {
        var (window, shell, vm) = ShowShell();
        var before = vm.SelectedNavItem;
        shell.ShowAboutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        PressAndSettle(window, PhysicalKey.Digit4, RawInputModifiers.Control);
        PressAndSettle(window, PhysicalKey.F1);

        Assert.Same(before, vm.SelectedNavItem);
        Assert.Null(shell.PendingShortcutOverview);
        Assert.NotNull(shell.PendingAbout);
    }

    [AvaloniaFact]
    public void Ctrl_Tab_moves_to_the_next_rail_module_from_a_text_box_and_Ctrl_Shift_Tab_back()
    {
        var (window, _, vm) = ShowShell();
        var etb = vm.NavItems.Single(i => i.ModuleKey == NavModules.Etb);
        vm.SelectedNavItem = etb;
        Dispatcher.UIThread.RunJobs();
        FocusByTab(Named<Control>(window, "FromBox"));
        var next = vm.NavItems[vm.NavItems.IndexOf(etb) + 1];

        PressAndSettle(window, PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Same(next, vm.SelectedNavItem);

        PressAndSettle(window, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Same(etb, vm.SelectedNavItem);
        window.AssertFocused(Named<Control>(window, "FromBox"));
    }

    [AvaloniaFact]
    public void F9_takes_a_Rueckzugsalarm_to_the_Druck_field_of_its_Trupp()
    {
        var (window, _, vm) = ShowShell(WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars());
        Assert.True(Named<TextBlock>(window, "ScbaAlarmShortcutHint").IsEffectivelyVisible);

        PressAndSettle(window, PhysicalKey.F9);

        Assert.Equal("ATEMSCHUTZ", vm.SelectedNavItem?.Header);
        var field = window.GetVisualDescendants().OfType<NumericUpDown>()
            .Single(n => n.Name == "PressureInput" && ReferenceEquals(n.DataContext, vm.Scba.Trupps[0]));
        window.AssertFocused(field);
    }

    [AvaloniaFact]
    public void F9_puts_focus_on_the_ILS_button_when_only_the_Rueckmeldung_is_due_and_confirms_nothing()
    {
        var clock = new FixedClock();
        var ticker = new ManualTicker();
        var vm = new IncidentWorkspaceViewModel(
            TestSession.StartNew(
                new FakeStore(),
                clock,
                new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
                "/x.fwincident",
                Array.Empty<(string, bool)>(),
                Array.Empty<(string, bool)>()),
            clock,
            ticker,
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        clock.Now = clock.Now.AddMinutes(16);
        ticker.Pulse();
        var (window, _, _) = ShowShell(vm);
        Assert.True(Named<TextBlock>(window, "ReminderShortcutHint").IsEffectivelyVisible);

        PressAndSettle(window, PhysicalKey.F9);

        window.AssertFocused(Named<Button>(window, "ReminderAckButton"));
        Assert.True(vm.Reminder!.IsDue);
    }

    [AvaloniaFact]
    public void The_F1_overview_lists_exactly_the_registered_shortcuts_and_Esc_closes_it()
    {
        var (window, shell, _) = ShowShell();

        PressAndSettle(window, PhysicalKey.F1);

        var overview = window.GetVisualDescendants().OfType<ShortcutOverviewView>().Single(v => v.IsEffectivelyVisible);
        var rows = overview.GetControl<ItemsControl>("ShortcutRows").GetRealizedContainers()
            .Select(c => string.Join(" | ", c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)))
            .ToList();
        var registered = ShortcutRegistry.All.Select(s => $"{s.Chord.Display} | {s.Description}").ToList();
        Assert.Equal(registered, rows);

        PressAndSettle(window, PhysicalKey.Escape);
        Assert.Null(shell.PendingShortcutOverview);
    }

    // A reference aid, not a destination: the command bar gives it a glyph and its key, never a
    // word as wide as ÜBERSICHT. Screen readers still hear what it is.
    [AvaloniaFact]
    public void The_shortcuts_button_is_a_named_glyph_that_opens_the_overview()
    {
        var (window, shell, _) = ShowShell();
        var button = Named<Button>(window, "ShortcutsButton");

        Assert.Equal("Tastenkürzel (F1)", AutomationProperties.GetName(button));
        Assert.DoesNotContain(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "TASTENKÜRZEL");
        Assert.True(button.Bounds.Width < 2 * Named<Button>(window, "AboutButton").Bounds.Width, $"the button is {button.Bounds.Width}px wide");

        FocusByTab(button);
        PressAndSettle(window, PhysicalKey.Space);

        Assert.NotNull(shell.PendingShortcutOverview);
    }

    [AvaloniaFact]
    public void Every_module_tab_shows_its_shortcut_on_the_rail()
    {
        var (window, _, vm) = ShowShell();

        var hints = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Name == "RailShortcutHint" && t.IsEffectivelyVisible)
            .Select(t => t.Text)
            .ToList();

        Assert.Equal(vm.NavItems.Where(i => !i.IsChecklist).Select(i => ShortcutRegistry.HintFor(i.ModuleKey)), hints);
    }

    private sealed class StaticMasterData(MasterDataSet set) : IMasterDataProvider
    {
        public MasterDataSet Get() => set;

        public void Save(MasterDataSet s)
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

    private sealed class EmptyRecent : IRecentFilesStore
    {
        public IReadOnlyList<string> GetRecent() => Array.Empty<string>();

        public void Add(string path)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
