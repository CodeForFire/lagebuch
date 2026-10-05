using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Behaviors;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #537: the open questions from the keyboard audit (#545), each answered by measuring Avalonia 12.1
// as this app ships it rather than by reading its source. The later issues build on these answers,
// so a framework upgrade that changes one of them shows up here first. Whether ConfirmDialogView's
// synchronous Focus() lands is answered by OverlayContractTests (Workspace.Confirm/FocusInside).
public class KeyboardRuntimeFactsTests
{
    private static (Window Window, ScbaView View, ScbaViewModel Vm) ShowScba()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var md = MasterDataSet.Empty with
        {
            TruppTypes = new[] { new TruppType("Angriffstrupp") },
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", null, null),
                new Person("Musterfrau", "Erika", "GF", null, null),
            },
        };
        var vm = new ScbaViewModel(session, md, clock, new NoopTicker(), new NoopAlarmService(), () => { });
        var view = new ScbaView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    // #466: Tab never takes a match the user cannot see. "Must" is a prefix of two people, and Tab
    // keeps what was typed. (A prefix of exactly one is completed visibly by SuggestionBox first; see
    // SuggestionBoxKeyboardTests.)
    [AvaloniaFact]
    public void Tab_on_an_open_suggestion_list_closes_it_moves_on_and_keeps_what_was_typed()
    {
        var (window, view, vm) = ShowScba();
        var box = view.GetControl<AutoCompleteBox>("TruppfuehrerBox");
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        window.Type("Must");
        Assert.True(box.IsDropDownOpen);

        window.Tab();

        window.AssertFocused(view.GetControl<AutoCompleteBox>("TruppmannBox"));
        Assert.False(box.IsDropDownOpen);
        Assert.Equal("Must", vm.NewTruppfuehrer);
    }

    // Avalonia's box as it ships, without this app's SuggestionBox on top.
    private static (Window Window, AutoCompleteBox Box) ShowBareBox(params string[] items)
    {
        var box = new AutoCompleteBox { ItemsSource = items, FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 0 };
        SuggestionBox.SetIsEnabled(box, false);
        var window = new Window { Content = new StackPanel { Children = { box, new TextBox() } }, Width = 600, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();
        box.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        return (window, box);
    }

    // #466: ContainsOrdinal ignores case, umlauts included, so no box needs another FilterMode.
    [AvaloniaFact]
    public void ContainsOrdinal_ignores_case_including_umlauts()
    {
        var (_, box) = ShowBareBox("Müller, Hans");

        Assert.NotNull(box.TextFilter);
        Assert.True(box.TextFilter("müll", "Müller, Hans"));
    }

    // #466: Avalonia's own text completion takes the first prefix match even with several left, so
    // SuggestionBox switches it on only for a single one.
    [AvaloniaFact]
    public void Avalonia_text_completion_takes_the_first_of_several_prefix_matches()
    {
        var (window, box) = ShowBareBox("Mustermann, Max", "Musterfrau, Erika");
        box.IsTextCompletionEnabled = true;

        window.Type("Must");

        Assert.Equal("Mustermann, Max", box.Text);
    }

    // #466: SearchText keeps what was typed while the box shows an arrowed row, and Esc closes the
    // list and puts it back. That is what SuggestionBox.ClaimsEnter reads.
    [AvaloniaFact]
    public void SearchText_keeps_what_was_typed_and_Esc_puts_it_back()
    {
        var (window, box) = ShowBareBox("Mustermann, Max", "Musterfrau, Erika");
        window.Type("Must");
        window.Press(PhysicalKey.ArrowDown);
        Assert.Equal("Mustermann, Max", box.Text);
        Assert.Equal("Must", box.SearchText);

        window.Press(PhysicalKey.Escape);

        Assert.False(box.IsDropDownOpen);
        Assert.Equal("Must", box.Text);
    }

    // The match the user can see: arrowing onto a suggestion already writes it into the box, and Tab
    // keeps it.
    [AvaloniaFact]
    public void Tab_after_arrowing_onto_a_suggestion_keeps_that_suggestion()
    {
        var (window, view, vm) = ShowScba();
        var box = view.GetControl<AutoCompleteBox>("TruppfuehrerBox");
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        window.Type("Must");
        window.Press(PhysicalKey.ArrowDown);
        Assert.Equal("Mustermann, Max", box.Text);

        window.Tab();

        window.AssertFocused(view.GetControl<AutoCompleteBox>("TruppmannBox"));
        Assert.Equal("Mustermann, Max", vm.NewTruppfuehrer);
    }

    // #282 says Tab reaches buttons in DataGrid cells in one press (verified in #262); Forces pins it
    // in ForcesTabRenderTests, this pins it for the ETB's row actions.
    [AvaloniaFact]
    public void Tab_from_the_ETB_grid_reaches_its_row_buttons_in_one_press()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, new FixedClock(), WorkspaceRenderHelper.MasterData(), () => { });
        vm.NewText = "Lagemeldung übermittelt";
        vm.AddEntryCommand.Execute(null);
        var view = new EtbView { DataContext = vm };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = view.GetControl<DataGrid>("EtbGrid");
        var edit = grid.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Bearbeiten");
        grid.Focus();
        Dispatcher.UIThread.RunJobs();

        window.Tab();

        window.AssertFocused(edit);
    }

    // #544: Ctrl+Tab does reach the window, but only on the way down. Avalonia's keyboard navigation
    // then treats it as a plain Tab: it moves focus and marks the event handled, so a bubbling
    // handler never sees it unhandled, and a TabControl does not switch tabs. A global Ctrl+Tab
    // has to be a tunnelling handler on the window.
    [AvaloniaTheory]
    [InlineData("TextBox")]
    [InlineData("DataGrid")]
    [InlineData("TabControl")]
    public void Ctrl_Tab_reaches_the_window_only_while_tunnelling_and_then_moves_focus_like_Tab(string focusedKind)
    {
        var textBox = new TextBox { Name = "TextBox" };
        var grid = new DataGrid { Name = "DataGrid", ItemsSource = new[] { "a", "b" }, AutoGenerateColumns = true };
        var tabs = new TabControl { Name = "TabControl", ItemsSource = new[] { "Eins", "Zwei", "Drei" } };
        var window = new Window { Content = new StackPanel { Children = { textBox, grid, tabs } }, Width = 800, Height = 600 };
        var tunnelled = false;
        var bubbledUnhandled = false;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => tunnelled |= e.Key == Key.Tab, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => bubbledUnhandled |= e.Key == Key.Tab, RoutingStrategies.Bubble);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Control focused = focusedKind switch
        {
            "TextBox" => textBox,
            "DataGrid" => grid,
            _ => (TabItem)tabs.ContainerFromIndex(0)!,
        };
        focused.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Tab, RawInputModifiers.Control);

        Assert.True(tunnelled);
        Assert.False(bubbledUnhandled);
        Assert.False(window.IsFocused(focused), $"Ctrl+Tab left focus on {focusedKind}");
        Assert.Equal(0, tabs.SelectedIndex);
    }

    // #538: the share/PIN flyout needs no focus handling of its own. Its two SelectableTextBlocks are
    // tab stops, focus lands on the first one when it opens, Tab stays in the flyout, and Esc
    // closes it by light dismiss.
    [AvaloniaFact]
    public async Task Share_flyout_takes_focus_on_its_address_and_closes_on_Esc()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(new FakeHost());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        await vm.ToggleSharingCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShareInfoButton");
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        button.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var content = Assert.IsAssignableFrom<Control>(flyout.Content);
        window.AssertFocused(content.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Name == "ShareAddressValue"));

        window.Tab();
        window.AssertFocused(content.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Name == "ShareKennungValue"));
        Assert.True(flyout.IsOpen);

        window.Press(PhysicalKey.Escape);
        Assert.False(flyout.IsOpen);
    }

    private sealed class FakeHost : IIncidentHostController
    {
        public bool CanHost => true;

        public bool IsHosting { get; private set; }

        public string? ShareHint => "Im Netzwerk: https://192.168.0.5:5859";

        public string? SharePin => IsHosting ? "1234" : null;

        public bool JoinsClosed => false;

        public event EventHandler? JoinsClosedChanged;

        public string? ShareKennung => IsHosting ? "7K2Q-M9XD-4HPA" : null;

        public void CloseJoins() => JoinsClosedChanged?.Invoke(this, EventArgs.Empty);

        public void RenewPin()
        {
        }

        public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default)
        {
            IsHosting = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsHosting = false;
            return Task.CompletedTask;
        }
    }
}
