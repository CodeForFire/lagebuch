using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #262 (UX review, "No accessibility support"): the Stärke-korrigieren flyout had no keyboard
// confirm at all -- only the ÜBERNEHMEN button, reachable by Tab/click. Escape-to-dismiss is
// otherwise provided by Avalonia's Flyout light-dismiss, but only once some element inside the
// popup has focus -- true automatically for the Stärke-korrigieren flyout's TextBoxes, but not
// for the read-only Verlauf flyout (nothing in it was focusable), which needed an explicit
// focus-on-open fix (see ForcesView.OnHistoryFlyoutOpened). The tests here pin both.
public class ForcesFlyoutKeyboardTests
{
    private static ForcesViewModel BuildForcesVm(out LocalIncidentSession session)
    {
        session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddForceUnit("FFB Wache 1", 6, null, "Alarmiert", null);
        var md = MasterDataSet.Empty with { Brigades = new[] { "FFB Wache 1" }, UnitStatus = new[] { "Alarmiert" } };
        return new ForcesViewModel(session, new FixedClock(), md, () => { });
    }

    private static (Window Window, ForcesViewModel Vm, Button FlyoutButton) OpenStrengthFlyout()
    {
        var vm = BuildForcesVm(out _);
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = view.GetControl<DataGrid>("ForcesGrid");
        var flyoutButton = grid.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("icon-btn") && (ToolTip.GetTip(b) as string) == "Stärke korrigieren");
        flyoutButton.Flyout!.ShowAt(flyoutButton);
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(1200, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 1200, 600));
        Dispatcher.UIThread.RunJobs();

        return (window, vm, flyoutButton);
    }

    [AvaloniaFact]
    public void Escape_closes_the_strength_correction_flyout()
    {
        var (window, _, flyoutButton) = OpenStrengthFlyout();
        Assert.True(flyoutButton.Flyout!.IsOpen);

        // Focus a field inside the flyout, the way an operator tabbing into it would.
        var zfHeader = window.GetVisualDescendants().OfType<HeaderedContentControl>()
            .Single(h => (h.Header as string) == "ZUGFÜHRER (ZF)");
        zfHeader.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var message = "Escape did not close the Stärke-korrigieren flyout -- Avalonia's built-in " +
            "Flyout light-dismiss no longer covers this, so explicit Escape handling needs to be added.";
        Assert.False(flyoutButton.Flyout!.IsOpen, message);
    }

    [AvaloniaFact]
    public void Enter_in_a_strength_field_applies_the_correction()
    {
        var (window, vm, _) = OpenStrengthFlyout();

        // The flyout's fields are unnamed, so locate ZUGFÜHRER (ZF) by its HeaderedContentControl
        // header rather than by tree order -- the DataGrid's own cell controls and the input
        // dock's AutoCompleteBoxes precede the flyout's content in visual-tree order.
        var zfHeader = window.GetVisualDescendants().OfType<HeaderedContentControl>()
            .Single(h => (h.Header as string) == "ZUGFÜHRER (ZF)");
        var zfBox = zfHeader.GetVisualDescendants().OfType<TextBox>().Single();
        zfBox.Focus();
        zfBox.SelectAll();
        window.KeyTextInput("1");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // A commit rebuilds the row (same idiom as ForcesTabRenderTests' strength-correction
        // test), so the current state has to be re-read from the collection, not the pre-commit
        // row reference.
        var updated = Assert.Single(vm.Forces);
        Assert.Equal(1, updated.ZugfuehrerCount); // keyboard-only submit committed the edit
        Assert.True(updated.HasHistory);
    }

    [AvaloniaFact]
    public void Escape_closes_the_history_flyout()
    {
        var vm = BuildForcesVm(out _);
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(vm.Forces);
        row.ZugfuehrerCount = 1;
        row.CommitStrength();
        Dispatcher.UIThread.RunJobs();
        Assert.True(Assert.Single(vm.Forces).HasHistory); // commit rebuilds the row

        var grid = view.GetControl<DataGrid>("ForcesGrid");
        var historyButton = grid.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == "Verlauf");
        historyButton.Flyout!.ShowAt(historyButton);
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(1200, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 1200, 600));
        Dispatcher.UIThread.RunJobs();
        Assert.True(historyButton.Flyout!.IsOpen);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var message = "Escape did not close the Verlauf flyout -- its content must grab focus " +
            "on open (OnHistoryFlyoutOpened) for Avalonia's built-in Flyout light-dismiss to " +
            "have a focused element inside the popup to route the key press through.";
        Assert.False(historyButton.Flyout!.IsOpen, message);
    }
}
