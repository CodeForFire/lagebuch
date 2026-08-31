using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The left "dispatch sidebar" module tab strip (AUFBAU, ETB, ..., WASSERFÖRDERUNG, ABBAU),
// TabStripPlacement="Left" per Theme/Styles.axaml. On a short window, the last tabs wrapped into
// a second column overlapping the first ones instead of scrolling -- reported live, screenshot
// showed WASSERFÖRDERUNG rendered directly beside AUFBAU and ABBAU beside ETB.
public class ModuleTabStripLayoutTests
{
    // CanHost=true so the header includes the "IM NETZWERK FREIGEBEN" row, matching the reported
    // screenshot exactly (a NoopIncidentHostController hides that row, understating header height).
    private sealed class HostableController : IIncidentHostController
    {
        public bool CanHost => true;
        public bool IsHosting => false;
        public string? ShareHint => null;
        public string? SharePin => null;
        public Task StartAsync(LocalIncidentSession session) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Tabs_stay_in_a_single_column_top_to_bottom_on_a_short_window()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new LageBuch.Domain.SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, clock, new ManualTicker(),
            WorkspaceRenderHelper.MasterData(), new FakeDialogs(), new NoopAlarmService(),
            new HostableController());

        // Matches the reported "relative small screen": a modest laptop-class height, well under
        // what 11 fixed-MinHeight-50 TabItems need stacked below the full header.
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1366, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabItems = window.GetVisualDescendants().OfType<TabItem>()
            .Select(t => t.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .ToList();

        Assert.True(tabItems.Count >= 10, $"Expected all module tabs to be found, got {tabItems.Count}.");
        for (var i = 1; i < tabItems.Count; i++)
        {
            Assert.True(tabItems[i] > tabItems[i - 1],
                $"Tab at index {i} (y={tabItems[i]}) is not below the previous tab (y={tabItems[i - 1]}) -- " +
                "the tab strip wrapped into a second column instead of staying single-column.");
        }

        // Not wrapping alone isn't enough -- the last tabs must still be reachable by scrolling
        // the rail into view, not merely pushed off-screen with no way back.
        var lastTab = window.GetVisualDescendants().OfType<TabItem>().Last(); // ABBAU
        var railScrollViewer = window.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(sv => sv.GetVisualDescendants().OfType<TabItem>().Any());
        Assert.NotNull(railScrollViewer);
        Assert.True(railScrollViewer!.Extent.Height > railScrollViewer.Viewport.Height,
            "Test premise: the rail must actually need scrolling at this window height " +
            $"(extent={railScrollViewer.Extent.Height}, viewport={railScrollViewer.Viewport.Height}).");

        railScrollViewer.Offset = new Vector(0, railScrollViewer.Extent.Height - railScrollViewer.Viewport.Height);
        Dispatcher.UIThread.RunJobs();
        var lastTabY = lastTab.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.InRange(lastTabY, 0, window.Bounds.Height);

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "module-tab-strip-small-screen-fixed.png"));
    }
}
