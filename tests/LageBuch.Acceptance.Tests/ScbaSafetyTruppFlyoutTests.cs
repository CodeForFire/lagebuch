using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// Designating a Sicherheitstrupp through the real control (#399).
/// </summary>
/// <remarks>
/// This is the test the previous design could not have. While the picker was a ComboBox, choosing
/// an entry crashed inside Avalonia's own selection commit — the write re-entered through the
/// incident's Changed event and altered the collection the SelectionModel was still holding indices
/// into — and headless could not reproduce it even with simulated clicks, because the failure lived
/// in a real popup window. A flyout entry is an ordinary button running an ordinary command, so the
/// whole interaction is reachable here.
/// </remarks>
public class ScbaSafetyTruppFlyoutTests
{
    private static (Window Window, ScbaView View, ScbaViewModel Scba) Build(bool narrow = false)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        foreach (var designation in new[] { "Sicherheitstrupp", "Angriffstrupp" })
        {
            scba.NewDesignation = designation;
            scba.NewTruppfuehrer = "Müller";
            scba.NewTruppmann = "Maier";
            scba.NewZweiterTruppmann = string.Empty;
            scba.AddTruppCommand.Execute(null);
        }

        // A phone's width folds the grid into cards, set up the way ScbaLocationInputTests does it.
        vm.IsNarrow = narrow;
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, scba);
    }

    private static Button PickerButton(ScbaView view, ScbaTruppRow row) =>
        view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "SafetyTruppButton" && ReferenceEquals(b.DataContext, row));

    private static (Window Window, ScbaViewModel Scba, Button Picker) OpenPicker()
    {
        var (window, view, scba) = Build();
        var picker = PickerButton(view, scba.Trupps[^1]);
        picker.Flyout!.ShowAt(picker);
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(1400, 900));
        window.Arrange(new Avalonia.Rect(0, 0, 1400, 900));
        Dispatcher.UIThread.RunJobs();
        return (window, scba, picker);
    }

    [AvaloniaFact]
    public void Choosing_an_entry_designates_it_and_does_not_take_the_app_down()
    {
        var (window, scba, _) = OpenPicker();
        var row = scba.Trupps[^1];
        var standby = scba.Trupps.Single(r => r.Designation == "Sicherheitstrupp");

        var entry = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is SafetyTruppChoice c && c.Id == standby.Id);
        entry.Command!.Execute(entry.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(standby.Id, scba.Trupps.Single(r => r.Id == row.Id).SafetyTruppChoices.Single(c => c.IsCurrent).Id);
        Assert.Equal(standby.DisplayName, scba.Trupps.Single(r => r.Id == row.Id).SafetyTruppButtonText);
    }

    // #832: an entry is a Button, and a Button inside a Flyout does not dismiss it. Pressed for
    // real rather than through its command, because running the command alone never closed anything.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Picking_a_sicherheitstrupp_designates_it_and_closes_the_flyout(bool narrow)
    {
        var (_, view, scba) = Build(narrow);
        var row = scba.Trupps[^1];
        var standby = scba.Trupps.Single(r => r.Designation == "Sicherheitstrupp");
        var picker = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.DataContext, row)
                && AutomationProperties.GetName(b) == "Sicherheitstrupp wählen");

        Click(picker);
        var flyout = Assert.IsType<Flyout>(picker.Flyout);
        Assert.True(flyout.IsOpen);
        var entry = Assert.IsAssignableFrom<Control>(flyout.Content).GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is SafetyTruppChoice c && c.Id == standby.Id);
        Click(entry);

        Assert.Equal(standby.DisplayName, scba.Trupps.Single(r => r.Id == row.Id).SafetyTruppButtonText);
        Assert.False(flyout.IsOpen);
    }

    private static void Click(Control control)
    {
        // The phone's cards stack below the form, so the last Trupp's picker starts off screen.
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var topLevel = TopLevel.GetTopLevel(control)!;
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;
        topLevel.MouseDown(centre, MouseButton.Left);
        topLevel.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_flyout_offers_kein_first_and_marks_the_entry_in_force()
    {
        var (window, scba, _) = OpenPicker();
        var row = scba.Trupps[^1];

        var entries = window.GetVisualDescendants().OfType<Button>()
            .Select(b => b.DataContext).OfType<SafetyTruppChoice>().ToList();

        Assert.Equal(SafetyTruppChoice.NoneDisplay, entries[0].Display);
        Assert.Single(entries, c => c.IsCurrent);
        Assert.True(entries[0].IsCurrent, "Nothing is designated yet, so \"— kein —\" is the entry in force.");
        Assert.DoesNotContain(entries, c => c.Id == row.Id);
    }

    [AvaloniaFact]
    public void Escape_dismisses_the_flyout()
    {
        var (window, _, picker) = OpenPicker();

        // The entries are buttons and therefore focusable, so the flyout can route Escape without
        // the Opened="…" focus workaround ForcesView's read-only history list needs.
        window.Press(PhysicalKey.Escape);

        Assert.False(picker.Flyout!.IsOpen);
    }

    // #538: Avalonia's Flyout already keeps the overlay contract here, so this pins it rather than
    // fixing anything. Opened from the keyboard, focus goes to the first entry, and Esc hands it
    // back to the button.
    [AvaloniaFact]
    public void The_picker_opened_by_keyboard_takes_focus_and_gives_it_back_on_Esc()
    {
        var (window, view, scba) = Build();
        var picker = PickerButton(view, scba.Trupps[^1]);
        picker.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        var flyout = Assert.IsType<Flyout>(picker.Flyout);

        window.Press(PhysicalKey.Enter);

        Assert.True(flyout.IsOpen);
        var first = window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is SafetyTruppChoice);
        window.AssertFocused(first);

        window.Press(PhysicalKey.Escape);

        Assert.False(flyout.IsOpen);
        window.AssertFocused(picker);
    }
}
