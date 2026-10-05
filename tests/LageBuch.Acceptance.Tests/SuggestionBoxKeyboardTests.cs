using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #466: one keyboard model for every suggestion box. A single match that starts with what was typed
// is completed inline and only then taken; a substring match, or one of several, never is. Enter
// takes what the box shows when that is not what was typed, and otherwise submits in one press.
// The ETB is the case that matters: Funkrufnamen are prefixes of each other, and a wrong one
// silently puts a message under another unit.
public class SuggestionBoxKeyboardTests
{
    private static (Window Window, EtbView View, EtbViewModel Vm) ShowEtb()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var md = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("Testort", "Florian X 1/46-1", 9),
                new Vehicle("Nachbarort", "Aich 42/1", 6),
                new Vehicle("Nachbarort", "Aich 42/2", 6),
            },
        };
        var vm = new EtbViewModel(session, clock, md, () => { });
        var view = new EtbView { DataContext = vm };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static AutoCompleteBox FocusFrom(Window window, EtbView view)
    {
        var from = view.GetControl<AutoCompleteBox>("FromBox");
        from.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        return from;
    }

    private static string SelectedText(AutoCompleteBox box)
    {
        var textBox = box.GetVisualDescendants().OfType<TextBox>().First();
        return textBox.SelectedText;
    }

    [AvaloniaFact]
    public void A_single_prefix_match_is_shown_as_selected_completion_and_Tab_takes_it()
    {
        var (window, view, vm) = ShowEtb();
        var from = FocusFrom(window, view);

        window.Type("Florian X 1");

        Assert.Equal("Florian X 1/46-1", from.Text);
        Assert.Equal("/46-1", SelectedText(from));

        window.Tab();

        window.AssertFocused(view.GetControl<AutoCompleteBox>("ToBox"));
        Assert.Equal("Florian X 1/46-1", vm.NewFrom);
    }

    // The Risk the issue names: "Florian X 1/46" is a unit of its own, typed as free text.
    [AvaloniaFact]
    public void Backspace_drops_a_visible_completion_and_Tab_then_keeps_what_was_typed()
    {
        var (window, view, vm) = ShowEtb();
        var from = FocusFrom(window, view);

        window.Type("Florian X 1/46");
        Assert.Equal("Florian X 1/46-1", from.Text);
        Assert.Equal("-1", SelectedText(from));

        window.Press(PhysicalKey.Backspace);
        Assert.Equal("Florian X 1/46", from.Text);

        window.Tab();

        window.AssertFocused(view.GetControl<AutoCompleteBox>("ToBox"));
        Assert.Equal("Florian X 1/46", vm.NewFrom);
    }

    [AvaloniaTheory]
    [InlineData("1/46")] // the only match, but not at the start
    [InlineData("Aich 42")] // a prefix of two
    [InlineData("Aich 42/1")] // already the match
    public void Tab_keeps_what_was_typed_unless_one_match_starts_with_it(string typed)
    {
        var (window, view, vm) = ShowEtb();
        var from = FocusFrom(window, view);

        window.Type(typed);
        Assert.Equal(typed, from.Text);

        window.Tab();

        window.AssertFocused(view.GetControl<AutoCompleteBox>("ToBox"));
        Assert.Equal(typed, vm.NewFrom);
    }

    [AvaloniaFact]
    public void The_completion_ignores_case()
    {
        var (window, view, vm) = ShowEtb();
        FocusFrom(window, view);

        window.Type("flor");
        window.Tab();

        Assert.Equal("Florian X 1/46-1", vm.NewFrom);
    }

    [AvaloniaFact]
    public void Shift_Tab_never_takes_a_match()
    {
        var (window, view, vm) = ShowEtb();
        var from = FocusFrom(window, view);
        window.Tab();
        var to = view.GetControl<AutoCompleteBox>("ToBox");

        window.Type("flor");
        Assert.Equal("Florian X 1/46-1", to.Text);
        window.ShiftTab();

        window.AssertFocused(from);
        Assert.Equal("flor", vm.NewTo);
    }

    [AvaloniaFact]
    public void Shift_Tab_after_arrowing_onto_a_row_puts_back_what_was_typed()
    {
        var (window, view, vm) = ShowEtb();
        var from = FocusFrom(window, view);
        window.Tab();

        window.Type("Aich");
        window.Press(PhysicalKey.ArrowDown);
        Assert.Equal("Aich 42/1", view.GetControl<AutoCompleteBox>("ToBox").Text);
        window.ShiftTab();

        window.AssertFocused(from);
        Assert.Equal("Aich", vm.NewTo);
    }

    [AvaloniaFact]
    public void Esc_after_arrowing_onto_a_row_closes_the_list_and_puts_back_what_was_typed()
    {
        var (window, view, vm) = ShowEtb();
        var from = FocusFrom(window, view);

        window.Type("Aich");
        window.Press(PhysicalKey.ArrowDown);
        window.Press(PhysicalKey.Escape);

        Assert.False(from.IsDropDownOpen);
        Assert.Equal("Aich", vm.NewFrom);
        window.AssertFocused(from);
    }

    // #259 keeps the list open from focus onwards, which made every submit from a suggestion box
    // cost two presses of Enter.
    [AvaloniaFact]
    public void Enter_with_the_list_open_and_nothing_picked_submits_in_one_press()
    {
        var (window, view, vm) = ShowEtb();
        view.GetControl<TextBox>("EtbTextBox").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Type("Lagemeldung übermittelt");
        var from = FocusFrom(window, view);
        window.Type("Aich 42");
        Assert.True(from.IsDropDownOpen);

        window.Press(PhysicalKey.Enter);

        Assert.Equal("Aich 42", Assert.Single(vm.Entries, e => e.Text == "Lagemeldung übermittelt").From);
        window.AssertFocused(from); // back at VON for the next entry
        Assert.True(string.IsNullOrEmpty(from.Text));
    }

    // A taken match is what the box holds now: coming back to it, the reopened list has nothing
    // pending, and Enter submits in one press.
    [AvaloniaFact]
    public void Enter_after_returning_to_a_box_whose_match_was_taken_submits_in_one_press()
    {
        var (window, view, vm) = ShowEtb();
        view.GetControl<TextBox>("EtbTextBox").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Type("Lagemeldung übermittelt");
        var from = FocusFrom(window, view);
        window.Type("flor");
        window.Press(PhysicalKey.Enter);
        window.Tab();
        window.ShiftTab();
        window.AssertFocused(from);
        Assert.True(from.IsDropDownOpen);

        window.Press(PhysicalKey.Enter);

        Assert.Equal("Florian X 1/46-1", Assert.Single(vm.Entries, e => e.Text == "Lagemeldung übermittelt").From);
    }

    [AvaloniaFact]
    public void Enter_on_a_visible_completion_takes_it_and_does_not_submit()
    {
        var (window, view, vm) = ShowEtb();
        view.GetControl<TextBox>("EtbTextBox").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Type("Lagemeldung übermittelt");
        var from = FocusFrom(window, view);
        window.Type("flor");
        var before = vm.Entries.Count;

        window.Press(PhysicalKey.Enter);

        Assert.Equal(before, vm.Entries.Count);
        Assert.Equal("Florian X 1/46-1", from.Text);
        Assert.False(from.IsDropDownOpen);

        window.Press(PhysicalKey.Enter);

        Assert.Equal("Florian X 1/46-1", Assert.Single(vm.Entries, e => e.Text == "Lagemeldung übermittelt").From);
    }
}
