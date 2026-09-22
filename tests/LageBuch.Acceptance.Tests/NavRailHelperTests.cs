using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// The rest of the acceptance suite reaches the module it wants through
// WorkspaceRenderHelper.SelectTab, so a bug in that helper -- silently landing on one tab, or
// matching nothing and leaving the selection where it was -- would weaken every one of those
// tests at once without failing any of them. These pin the helper itself.
public class NavRailHelperTests
{
    private static Window ShowWorkspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Also the "before" record of the rail the configurable-navigation work replaces: this exact
    // order is what an empty Stammdaten layout has to keep reproducing.
    [AvaloniaFact]
    public void Rail_headers_read_in_order()
    {
        var window = ShowWorkspace();

        Assert.Equal(
            new[]
            {
                "AUFBAU", "ETB", "AUFGABEN", "FUNKTIONEN", "KRÄFTE",
                "ATEMSCHUTZ", "CO-MESSUNG", "DATEIEN", "LINKS", "KONTAKTE", "ABBAU",
            },
            WorkspaceRenderHelper.RailHeaders(window));
    }

    [AvaloniaFact]
    public void Select_tab_lands_on_the_tab_it_names()
    {
        var window = ShowWorkspace();

        foreach (var (header, expectedIndex) in new[]
                 {
                     ("ATEMSCHUTZ", 5), ("AUFBAU", 0), ("ABBAU", 10), ("ETB", 1), ("CO-MESSUNG", 6),
                 })
        {
            var tabs = WorkspaceRenderHelper.SelectTab(window, header);
            Assert.Equal(expectedIndex, tabs.SelectedIndex);
        }
    }

    // A header that moved or was renamed must fail loudly, naming what the rail does show --
    // never quietly leave the previous tab selected and let the caller assert against it.
    [AvaloniaFact]
    public void Select_tab_throws_for_a_header_the_rail_does_not_have()
    {
        var window = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");

        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkspaceRenderHelper.SelectTab(window, "NACHBEREITUNG"));

        Assert.Contains("NACHBEREITUNG", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ATEMSCHUTZ", ex.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Selected_tab_content_is_the_selected_module_not_a_previously_visited_one()
    {
        var window = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "DATEIEN");
        var files = WorkspaceRenderHelper.SelectedTabContent(window);
        Assert.Contains(files.GetVisualDescendants().OfType<Control>(), c => c.Name == "FilesDropZone");

        // The presenter reuses the realized Border and re-binds it, so the visual identity is not
        // the thing to assert -- what matters is that its subtree is now the other module's.
        WorkspaceRenderHelper.SelectTab(window, "LINKS");
        var links = WorkspaceRenderHelper.SelectedTabContent(window);
        Assert.Contains(links.GetVisualDescendants().OfType<Control>(), c => c.Name == "LinkSearchBox");
        Assert.DoesNotContain(links.GetVisualDescendants().OfType<Control>(), c => c.Name == "FilesDropZone");
    }
}
