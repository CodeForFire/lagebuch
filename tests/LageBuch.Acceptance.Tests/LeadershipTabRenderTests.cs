using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The FÜHRUNG tab sits behind ATEMSCHUTZ in the rail; this pins that its board is wired into a live
// workspace. The board itself is LeadershipBoardRenderTests'.
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
    public void The_rail_carries_a_fuehrung_tab_right_after_atemschutz()
    {
        var window = ShowWorkspace();
        var headers = WorkspaceRenderHelper.RailHeaders(window).ToList();

        Assert.Equal(headers.IndexOf("ATEMSCHUTZ") + 1, headers.IndexOf("FÜHRUNG"));
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
        Assert.Contains("FÜHRUNG", texts);
        Assert.Contains("Einsatzleitung", texts);
        Assert.True(view.GetControl<Button>("LeadershipAddAbschnittButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_fuehrung_tab_is_told_when_the_window_is_narrow()
    {
        var window = ShowWorkspace(412, 915);
        WorkspaceRenderHelper.SelectTab(window, "FÜHRUNG");

        var vm = Assert.IsType<IncidentWorkspaceViewModel>(((IncidentWorkspaceView)window.Content!).DataContext);
        Assert.True(vm.Leadership.IsNarrow);
    }
}
