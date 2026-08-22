using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The workspace header's Stichwort hero + Einsatznummer add-later affordance (#69). FakeStore,
// FakeDialogs, FixedClock, NoopTicker, NoopAlarmService are shared from WorkspaceAcceptanceTests.cs.
public class HeaderHeroTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

    private static IncidentWorkspaceViewModel BuildWorkspace(string? keyword)
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>(), keyword: keyword);
        return new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(), Md(),
            new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
    }

    private static Window Show(IncidentWorkspaceViewModel vm)
    {
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1280, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void With_a_keyword_and_no_number_the_hero_is_the_keyword_and_the_add_affordance_shows()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        var hero = window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "EinsatznummerValue");
        Assert.Equal("B3P", hero.Text);

        var addButton = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AddIncidentNumberButton");
        Assert.True(addButton.IsVisible);
        var chip = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "IncidentNumberChip");
        Assert.False(chip.IsVisible);
    }

    [AvaloniaFact]
    public void With_no_keyword_and_no_number_the_hero_falls_back_to_a_placeholder()
    {
        var vm = BuildWorkspace(null);
        var window = Show(vm);

        var hero = window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "EinsatznummerValue");
        Assert.Equal("Unbenannter Einsatz", hero.Text);

        var addButton = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AddIncidentNumberButton");
        Assert.False(addButton.IsVisible);
    }

    // Empirically guards against a known Avalonia trap in this codebase: a data-bound text element
    // stranded inside an IsVisible-collapsed container measures to zero width the first time the
    // container un-collapses if its content changes in the same update (see the sharing row's
    // ShareStatus/SharePin fix). Adding the Einsatznummer flips the chip from collapsed to visible
    // in the very same command that also sets its text, so this is exactly that risk.
    [AvaloniaFact]
    public void Adding_the_einsatznummer_shows_a_correctly_sized_chip_not_a_zero_width_one()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        vm.BeginEditIncidentNumberCommand.Execute(null);
        vm.IncidentNumberEditInput = "B 1.2 260715 123";
        vm.ConfirmIncidentNumberCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var chip = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "IncidentNumberChip");
        Assert.True(chip.IsVisible);
        Assert.True(chip.Bounds.Width > 20,
            $"the Einsatznummer chip rendered at {chip.Bounds.Width:F0}px wide — looks like the collapsed-ancestor zero-width trap.");

        var addButton = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AddIncidentNumberButton");
        Assert.False(addButton.IsVisible);
    }

    [AvaloniaFact]
    public void Cancelling_the_edit_returns_to_the_add_affordance()
    {
        var vm = BuildWorkspace("B3P");
        var window = Show(vm);

        vm.BeginEditIncidentNumberCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editRow = window.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "IncidentNumberEditRow");
        Assert.True(editRow.IsVisible);

        vm.CancelEditIncidentNumberCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(editRow.IsVisible);
        var addButton = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AddIncidentNumberButton");
        Assert.True(addButton.IsVisible);
    }
}
