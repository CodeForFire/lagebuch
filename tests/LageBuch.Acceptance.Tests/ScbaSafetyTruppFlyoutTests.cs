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
    private static (Window Window, ScbaView View, ScbaViewModel Scba) Build()
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

        var view = new ScbaView { DataContext = scba };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
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
        Assert.Equal("Trupp " + standby.TruppNumber, scba.Trupps.Single(r => r.Id == row.Id).SafetyTruppButtonText);
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
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(picker.Flyout!.IsOpen);
    }
}
