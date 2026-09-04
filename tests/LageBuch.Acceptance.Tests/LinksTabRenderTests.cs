using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// Issue #74: the new "Links" quick-access tab. Doubles as the PR before/after screenshot
// capture (RENDER_OUT), same idiom as FilesTabRenderTests.
public class LinksTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, name));
    }

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void Workspace_renders_eight_tabs_before_links_is_opened()
    {
        var (window, _) = ShowWorkspace();
        var tabs = Tabs(window);

        Assert.Equal(10, tabs.Items.Count);
        Capture(window, "links-before.png");
    }

    [AvaloniaFact]
    public void Selecting_the_links_tab_shows_the_seeded_links()
    {
        var (window, vm) = ShowWorkspace();

        var tabs = Tabs(window);
        tabs.SelectedIndex = 8; // LINKS
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("LINKS", ((TabItem)tabs.SelectedItem!).Header);
        Assert.Equal(2, vm.Links.Links.Count);
        Assert.Contains(vm.Links.Links, l => l.Name == "Wetterdienst" && l.Url == "https://dwd.de");
        Capture(window, "links-after.png");
    }

    // Issue #197: the ⚠ error banner glyph used to be Unicode text on a TextBlock, which defaults
    // to Barlow -- a font that doesn't carry it. Now a PathIcon like the ETB grid's row actions,
    // so the icon is drawn from bundled vector data.
    [AvaloniaFact]
    public void Error_banner_renders_a_laid_out_icon()
    {
        var (window, vm) = ShowWorkspace();
        var tabs = Tabs(window);
        tabs.SelectedIndex = 8; // LINKS
        vm.Links.ErrorMessage = "Fehler beim Öffnen.";
        Dispatcher.UIThread.RunJobs();

        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ErrorBanner");
        var icon = Assert.Single(banner.GetVisualDescendants().OfType<PathIcon>());
        Assert.True(icon.Bounds.Width > 0, "the error banner icon has zero width -- nothing is drawn");
    }
}
