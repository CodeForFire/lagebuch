using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The workspace header's Stichwort hero, Einsatznummer chip, address line and the Einsatzdaten
// edit affordance (#69, now a dialog instead of an inline editor). FakeStore, FakeDialogs,
// FixedClock, NoopTicker, NoopAlarmService are shared from WorkspaceAcceptanceTests.cs.
public class HeaderHeroTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

    private static IncidentWorkspaceViewModel BuildWorkspace(string? keyword)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>(),
            keyword: keyword);
        return new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            Md(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
    }

    private static Window Show(IncidentWorkspaceViewModel vm)
    {
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1280, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    // Fills the dialog the way an operator would and saves it.
    private static void SaveViaDialog(IncidentWorkspaceViewModel vm, string? number = null, string? street = null, string? district = null)
    {
        vm.EditIncidentDataCommand.Execute(null);
        var dialog = vm.PendingIncidentDataDialog!;
        dialog.IncidentNumber = number ?? dialog.IncidentNumber;
        dialog.Street = street ?? dialog.Street;
        dialog.District = district ?? dialog.District;
        dialog.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void With_a_keyword_and_no_number_the_hero_is_the_keyword_and_the_pencil_shows()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        Assert.Equal("B3P", Find<TextBlock>(window, "EinsatznummerValue").Text);
        Assert.True(Find<Button>(window, "EditIncidentDataButton").IsVisible);
        Assert.False(Find<Button>(window, "AddIncidentDataButton").IsVisible);
        Assert.False(Find<TextBlock>(window, "IncidentNumberChip").IsVisible);
        Assert.False(Find<TextBlock>(window, "AddressLine").IsVisible);
    }

    [AvaloniaFact]
    public void With_no_head_data_at_all_the_hero_falls_back_to_a_placeholder_and_offers_the_add_affordance()
    {
        var vm = BuildWorkspace(null);
        var window = Show(vm);

        Assert.Equal("Unbenannter Einsatz", Find<TextBlock>(window, "EinsatznummerValue").Text);

        // Regression guard for #250: without any head data there must still be an obvious way in.
        Assert.True(Find<Button>(window, "AddIncidentDataButton").IsVisible);
        Assert.False(Find<Button>(window, "EditIncidentDataButton").IsVisible);
    }

    // Empirically guards against a known Avalonia trap in this codebase: a data-bound text element
    // stranded inside an IsVisible-collapsed container measures to zero width the first time the
    // container un-collapses if its content changes in the same update (see the sharing row's
    // ShareStatus/SharePin fix). Saving the dialog flips the chip and the address line from
    // collapsed to visible in the very same update that sets their text, so this is exactly that risk.
    [AvaloniaFact]
    public void Saving_the_dialog_shows_a_correctly_sized_chip_and_address_line_not_zero_width_ones()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        SaveViaDialog(vm, number: "B 1.2 260715 123", street: "Hauptstr. 12", district: "FFB");

        var chip = Find<TextBlock>(window, "IncidentNumberChip");
        Assert.True(chip.IsVisible);
        Assert.True(
            chip.Bounds.Width > 20,
            $"the Einsatznummer chip rendered at {chip.Bounds.Width:F0}px wide — looks like the collapsed-ancestor zero-width trap.");

        var address = Find<TextBlock>(window, "AddressLine");
        Assert.True(address.IsVisible);
        Assert.Equal("Hauptstr. 12, FFB", address.Text);
        Assert.True(
            address.Bounds.Width > 20,
            $"the address line rendered at {address.Bounds.Width:F0}px wide — looks like the collapsed-ancestor zero-width trap.");

        Assert.Null(vm.PendingIncidentDataDialog);
    }

    // Issue #197: the ✎ affordance used to be Unicode text on a Button, which defaults to Oswald --
    // a font that carries none of those codepoints. Now a PathIcon like the ETB grid's row actions,
    // so the icon comes from bundled vector data.
    [AvaloniaFact]
    public void The_edit_affordance_renders_a_laid_out_icon()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        var pencil = Find<Button>(window, "EditIncidentDataButton");
        var icon = Assert.Single(pencil.GetVisualDescendants().OfType<PathIcon>());
        Assert.True(icon.Bounds.Width > 0, "the edit button's icon has zero width -- nothing is drawn");
    }

    [AvaloniaFact]
    public void The_edit_affordance_opens_the_dialog_overlay_and_cancel_closes_it()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        vm.EditIncidentDataCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var dialogView = Assert.Single(window.GetVisualDescendants().OfType<IncidentDataDialogView>());
        Assert.True(dialogView.IsVisible);

        vm.PendingIncidentDataDialog!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(window.GetVisualDescendants().OfType<IncidentDataDialogView>());
        Assert.True(Find<Button>(window, "EditIncidentDataButton").IsVisible);
    }
}
