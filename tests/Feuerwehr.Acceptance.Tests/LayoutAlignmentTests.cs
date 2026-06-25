using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Views;

namespace Feuerwehr.Acceptance.Tests;

// The shell uses a single 24px content gutter. Every primary band — the header bars,
// the module nav rail, and the footer actions — must share the same left edge so nothing
// juts out. These tests pin that contract so the rail can't drift back to a flush x=0.
public class LayoutAlignmentTests
{
    private const double Gutter = 24.0;

    private static double LeftInWindow(Visual v, Window window) =>
        v.TranslatePoint(new Point(0, 0), window)!.Value.X;

    // The TabControl's own tab-strip presenter (there are other ItemsPresenters in the tree).
    private static ItemsPresenter TabStrip(Window window) =>
        window.GetVisualDescendants().OfType<TabControl>().First()
            .GetVisualDescendants().OfType<ItemsPresenter>()
            .First(p => p.Name == "PART_ItemsPresenter");

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

    [AvaloniaFact]
    public void Nav_rail_starts_at_the_content_gutter_not_flush_to_the_edge()
    {
        var window = ShowWorkspace();

        Assert.Equal(Gutter, LeftInWindow(TabStrip(window), window), precision: 0);
    }

    [AvaloniaFact]
    public void Header_bars_rail_and_footer_share_one_left_edge()
    {
        var window = ShowWorkspace();

        double Left(string name) => LeftInWindow(
            window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name), window);

        var reminderBarLeft = Left("ReminderBar");
        var tabStripLeft = LeftInWindow(TabStrip(window), window);
        var footerCloseLeft = Left("CloseButton");

        Assert.Equal(Gutter, reminderBarLeft, precision: 0);
        Assert.Equal(Gutter, tabStripLeft, precision: 0);
        Assert.Equal(Gutter, footerCloseLeft, precision: 0);
    }
}
