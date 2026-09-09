using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

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

    // --- Issue #262 (UX review, "Links" section) ---
    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowLinksTab()
    {
        var (window, vm) = ShowWorkspace();
        Tabs(window).SelectedIndex = 8; // LINKS
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void Typing_a_search_term_narrows_the_rendered_link_list()
    {
        var (window, vm) = ShowLinksTab();
        Assert.Equal(2, Named<ItemsControl>(window, "LinksList").ItemCount);

        vm.Links.FilterText = "wetter";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, Named<ItemsControl>(window, "LinksList").ItemCount);
        Assert.True(Named<Button>(window, "ClearLinkSearchButton").IsVisible);
        Capture(window, "links-filtered.png");
    }

    [AvaloniaFact]
    public void A_search_term_matching_no_link_replaces_the_list_with_a_hint()
    {
        var (window, vm) = ShowLinksTab();

        vm.Links.FilterText = "Drehleiter";
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named<ItemsControl>(window, "LinksList").IsVisible);
        var hint = Named<TextBlock>(window, "NoMatchesText");
        Assert.True(hint.IsVisible);
        Assert.Contains("Drehleiter", hint.Text!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Clearing_the_search_brings_every_link_back()
    {
        var (window, vm) = ShowLinksTab();
        vm.Links.FilterText = "wetter";
        Dispatcher.UIThread.RunJobs();

        vm.Links.ClearFilterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, Named<ItemsControl>(window, "LinksList").ItemCount);
        Assert.False(Named<Button>(window, "ClearLinkSearchButton").IsVisible);
    }

    // With an empty Stammdaten list the "Keine Links hinterlegt." hint is the whole story --
    // offering a search box over nothing is just one more control to skip past.
    [AvaloniaFact]
    public void The_search_box_is_hidden_when_no_links_are_configured()
    {
        var view = new LinksView
        {
            DataContext = new LinksViewModel(Array.Empty<Link>(), new FakeDialogs()),
        };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // IsEffectivelyVisible, not IsVisible: the box itself is never hidden directly, its
        // enclosing panel is -- only the effective flag answers "can the operator see it".
        Assert.False(Named<TextBox>(window, "LinkSearchBox").IsEffectivelyVisible);
        Assert.True(Named<TextBlock>(window, "EmptyText").IsEffectivelyVisible);
    }

    /// <summary>
    /// Drives the box itself rather than the view model, so the Text binding and the Escape
    /// KeyBinding are covered: every other test here sets FilterText directly, and would still
    /// pass with the binding dropped from the XAML -- leaving the feature dead in the app.
    /// </summary>
    [AvaloniaFact]
    public void Typing_in_the_search_box_filters_and_escape_clears_it()
    {
        var (window, vm) = ShowLinksTab();
        var box = Named<TextBox>(window, "LinkSearchBox");
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyTextInput("wetter");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("wetter", vm.Links.FilterText);
        Assert.Equal(1, Named<ItemsControl>(window, "LinksList").ItemCount);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(string.Empty, vm.Links.FilterText);
        Assert.Equal(string.Empty, box.Text);
        Assert.Equal(2, Named<ItemsControl>(window, "LinksList").ItemCount);
    }

    /// <summary>
    /// The search box must flex rather than sit at a pinned width: an Auto column never shrinks,
    /// so a fixed width runs off the right edge of a phone-width window with no horizontal
    /// scroller anywhere above it to rescue it (the #146 contract).
    /// </summary>
    [AvaloniaFact]
    public void The_search_box_stays_inside_a_phone_width_window()
    {
        var (window, _) = ShowWorkspace();
        window.Width = 411;
        window.Height = 872;
        Tabs(window).SelectedIndex = 8; // LINKS
        Dispatcher.UIThread.RunJobs();

        var box = Named<TextBox>(window, "LinkSearchBox");
        var right = box.TranslatePoint(new Point(box.Bounds.Width, 0), window)!.Value.X;

        Assert.True(right <= 411, $"the search box runs {right - 411}px past the right edge");
    }

    /// <summary>
    /// The second #262 "Links" finding: ÖFFNEN gave no clue that it leaves Lagebuch for the
    /// system browser. The tooltip carries the explanation, and the glyph carries it on Android,
    /// where there is no hover to reveal a tooltip at all.
    /// </summary>
    [AvaloniaFact]
    public void The_open_button_shows_and_says_that_it_leaves_the_app()
    {
        var (window, _) = ShowLinksTab();

        var open = Named<ItemsControl>(window, "LinksList")
            .GetVisualDescendants().OfType<Button>().First();

        var tip = Assert.IsType<string>(ToolTip.GetTip(open));
        Assert.Contains("Browser", tip, StringComparison.Ordinal);
        Assert.NotEmpty(open.GetVisualDescendants().OfType<PathIcon>());
    }
}
