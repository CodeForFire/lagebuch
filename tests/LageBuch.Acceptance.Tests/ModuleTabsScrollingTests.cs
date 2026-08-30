using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// The left nav rail is a TabControl with TabStripPlacement="Left". On a viewport too short to fit
// all rail tabs vertically, Avalonia's WrapPanel (the default ItemsPanel, v-flipped by the theme for
// Left placement) used to wrap into a second side-by-side column -- confusing on a phone-height
// window, and the second column can run off the right edge entirely. These tests pin that the rail
// stays in ONE column and overflows into a scrollbar instead of wrapping (#146).
public class ModuleTabsScrollingTests
{
    // Short enough to force wrap regardless of which header banners are visible: 10 tabs at
    // MinHeight=50 need 500px, and the header/footer stacks eat most of the rest.
    private const double ShortHeight = 400.0;

    [AvaloniaFact]
    public void Nav_rail_stays_one_column_and_scrolls_on_a_short_viewport()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = ShortHeight,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Capture BEFORE the assertions: on the buggy build the column assertion fails, and the
        // RENDER_OUT frame of the wrapped two-column rail is exactly the "before" screenshot.
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Combine(dir, "module-tabs-scrolling.png"));
        }

        var tabs = ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");
        var tabItems = tabs.GetVisualDescendants().OfType<TabItem>().ToArray();

        // Nothing lost: every declared rail tab is still realized.
        Assert.Equal(10, tabItems.Length);

        // One column, not side-by-side columns: every tab shares the same horizontal origin.
        var columns = tabItems
            .Select(t => t.TranslatePoint(new Point(0, 0), tabs)!.Value.X)
            .Distinct()
            .ToArray();
        var columnsMessage =
            $"nav rail wrapped into {columns.Length} columns at x=" +
            string.Join(", ", columns.Select(c => c.ToString("F0", CultureInfo.InvariantCulture))) +
            " -- it must overflow into a scrollbar instead.";
        Assert.True(
            columns.Length == 1,
            columnsMessage);

        // The overflow lands in a ScrollViewer, not silent clipping.
        var strip = tabs.GetVisualDescendants().OfType<ItemsPresenter>()
            .First(p => p.Name == "PART_ItemsPresenter");
        var scroll = strip.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.True(
            scroll.Extent.Height > scroll.Viewport.Height,
            $"rail is {scroll.Extent.Height:F0}px tall in a {scroll.Viewport.Height:F0}px viewport -- no overflow to scroll.");
    }
}