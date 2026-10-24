using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The FÜHRUNG rail entry sits behind AUFGABEN and holds the board, Funktionen and Kräfte as tabs;
// this pins that they are wired into a live workspace. The board itself is LeadershipBoardRenderTests'.
public class LeadershipTabRenderTests
{
    private static Window ShowWorkspace(double width = 1920, double height = 1032)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_rail_carries_a_fuehrung_tab_right_after_aufgaben()
    {
        var window = ShowWorkspace();
        var headers = WorkspaceRenderHelper.RailHeaders(window).ToList();

        Assert.Equal(headers.IndexOf("AUFGABEN") + 1, headers.IndexOf("FÜHRUNG"));
        Assert.DoesNotContain("FUNKTIONEN", headers);
        Assert.DoesNotContain("KRÄFTE", headers);
    }

    [AvaloniaFact]
    public void Fuehrung_opens_on_organisation_beside_funktionen_and_kraefte()
    {
        var window = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "FÜHRUNG");
        var strip = WorkspaceRenderHelper.LeadershipTabStrip(window);

        Assert.Equal(
            new[] { "ORGANISATION", "FUNKTIONEN", "KRÄFTE" },
            strip.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Equal("ORGANISATION", ((TabItem)strip.SelectedItem!).Header);
        Assert.All(strip.Items.OfType<TabItem>(), t => Assert.True(t.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void The_fuehrung_tab_renders_the_board_for_the_open_incident()
    {
        var window = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "FÜHRUNG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        var view = content.GetVisualDescendants().OfType<LeadershipView>().Single();
        Assert.IsType<LeadershipViewModel>(view.DataContext);
        var texts = content.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Einsatzleitung", texts);
        Assert.True(view.GetControl<Button>("LeadershipAddAbschnittButton").IsEffectivelyVisible);
    }

    // The phone hides the rail's own tabs and navigates from the bottom bar, which only says
    // FÜHRUNG; the three tabs inside are the only way to Funktionen and Kräfte there.
    [AvaloniaFact]
    public void A_phone_still_shows_the_three_fuehrung_tabs()
    {
        var window = ShowWorkspace(412, 915);

        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        var strip = WorkspaceRenderHelper.LeadershipTabStrip(window);

        Assert.All(strip.Items.OfType<TabItem>(), t => Assert.True(t.IsEffectivelyVisible));
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        Assert.Single(content.GetVisualDescendants().OfType<ForcesView>());
    }

    [AvaloniaFact]
    public void The_fuehrung_tab_is_told_when_the_window_is_narrow()
    {
        var window = ShowWorkspace(412, 915);
        WorkspaceRenderHelper.SelectTab(window, "FÜHRUNG");

        var vm = Assert.IsType<IncidentWorkspaceViewModel>(((IncidentWorkspaceView)window.Content!).DataContext);
        Assert.True(vm.Leadership.IsNarrow);
        Assert.True(vm.Roles.IsNarrow);
        Assert.True(vm.Forces.IsNarrow);
    }
}
