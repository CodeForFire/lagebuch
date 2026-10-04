using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// #539: a Druckkontrolle from the warning bar by keyboard alone. The bar leads into the due Trupp's
// Druck field, Enter records what was typed, and the caret moves on to the next due Trupp. Driven
// through real key events, because "Enter does nothing here" was a key-routing question.
public class DruckkontrolleKeyboardTests
{
    [AvaloniaFact]
    public void The_druckabfrage_bar_by_keyboard_lands_in_the_due_trupps_druck_field_and_enter_records_it()
    {
        var (window, view, vm, _) = ShowWorkspace();
        var due = vm.Scba.Trupps[0];
        Assert.True(due.IsControlDue);

        Activate(window, view.GetControl<Button>("ScbaControlJumpButton"));

        window.AssertFocused(PressureField(window, due));

        window.Type("270");
        window.Press(PhysicalKey.Enter);

        Assert.Equal("zuletzt 270", due.PressurePlaceholder);
        Assert.Null(due.PressureInput);
        Assert.False(due.IsControlDue); // the Druckabfrage timer starts over from this reading
        vm.Etb.HideSystemEntries = false; // the Druckkontrolle line is app-written, hidden by default
        Assert.Contains(vm.Etb.Entries, e => e.Text.Contains("Druckkontrolle", StringComparison.Ordinal)
            && e.Text.Contains("270 bar", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Enter_in_an_empty_druck_field_records_nothing_and_shows_the_hint()
    {
        var (window, view, vm, _) = ShowWorkspace();
        var due = vm.Scba.Trupps[0];
        Activate(window, view.GetControl<Button>("ScbaControlJumpButton"));

        window.Press(PhysicalKey.Enter);

        Assert.True(due.IsControlDue);
        Assert.Equal("zuletzt 300", due.PressurePlaceholder);
        var hint = Message(window, due, "pressureError");
        Assert.True(hint.IsEffectivelyVisible);
        Assert.Equal(ValidationMessages.ControlPressure, hint.Text);
        window.AssertFocused(PressureField(window, due));
    }

    [AvaloniaFact]
    public void An_implausible_druck_is_not_recorded_on_the_first_enter()
    {
        var (window, view, vm, _) = ShowWorkspace();
        var due = vm.Scba.Trupps[0];
        Activate(window, view.GetControl<Button>("ScbaControlJumpButton"));

        window.Type("270");
        window.Press(PhysicalKey.Enter);
        Assert.Equal("zuletzt 270", due.PressurePlaceholder);

        // Moments later, a finger slipping off 270 types 27 -- a Rückzugsalarm and a Bericht line
        // nobody meant. No other Trupp is due, so the caret stayed in this field.
        window.Type("27");
        window.Press(PhysicalKey.Enter);

        Assert.Equal("zuletzt 270", due.PressurePlaceholder);
        Assert.Equal(27, due.PressureInput);
        Assert.True(Message(window, due, "pressureWarning").IsEffectivelyVisible);

        // Asked once, never refused: a leaking cylinder does lose air this fast.
        window.Press(PhysicalKey.Enter);
        Assert.Equal("zuletzt 27", due.PressurePlaceholder);
    }

    [AvaloniaFact]
    public void With_two_trupps_due_enter_on_the_first_moves_on_to_the_second()
    {
        var (window, view, vm, clock) = ShowWorkspace();
        var first = vm.Scba.Trupps[0];
        var second = vm.Scba.Trupps[1];
        clock.Now = clock.Now.AddMinutes(11);
        Assert.True(first.IsControlDue && second.IsControlDue);

        Activate(window, view.GetControl<Button>("ScbaControlJumpButton"));
        window.AssertFocused(PressureField(window, first));

        window.Type("250");
        window.Press(PhysicalKey.Enter);

        Assert.False(first.IsControlDue);
        window.AssertFocused(PressureField(window, second));
    }

    [AvaloniaFact]
    public void Clicking_druck_leaves_focus_where_the_pointer_put_it()
    {
        var (window, view, vm, clock) = ShowWorkspace();
        var first = vm.Scba.Trupps[0];
        var second = vm.Scba.Trupps[1];
        clock.Now = clock.Now.AddMinutes(11);
        Activate(window, view.GetControl<Button>("ScbaControlJumpButton"));
        window.Type("250");

        Click(window, Row(window, first).GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && Equals(b.Content, "DRUCK")));

        Assert.False(first.IsControlDue);
        Assert.False(window.IsFocusWithin(PressureField(window, second)));
    }

    private static void Click(Window window, Control control)
    {
        var centre = Avalonia.VisualExtensions.TranslatePoint(
            control,
            new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Window, IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm, FixedClock Clock) ShowWorkspace()
    {
        var clock = new FixedClock();
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(clock: clock);

        // A second Trupp under air, not due yet: "the bar leads to the due Trupp" needs a wrong
        // row to land on, and moving the clock on makes it the second due one.
        vm.Scba.NewDesignation = "Sicherheitstrupp";
        vm.Scba.NewTruppfuehrer = "Huber";
        vm.Scba.NewTruppmann = "Berger";
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);

        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, clock);
    }

    // Tab focus plus Space: the bar is reached and pressed the way a keyboard user does it.
    private static void Activate(Window window, Button bar)
    {
        Assert.True(bar.IsEffectivelyVisible);
        bar.Focus(NavigationMethod.Tab);
        window.Press(PhysicalKey.Space);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridRow Row(Window window, ScbaTruppRow row) =>
        window.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, row));

    private static NumericUpDown PressureField(Window window, ScbaTruppRow row) =>
        Row(window, row).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "PressureInput");

    private static TextBlock Message(Window window, ScbaTruppRow row, string cssClass) =>
        Row(window, row).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains(cssClass));
}
