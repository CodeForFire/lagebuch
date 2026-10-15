using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
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
        var session = TestSession.StartNew(
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
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void Workspace_renders_the_rail_before_links_is_opened()
    {
        var (window, _) = ShowWorkspace();

        Assert.Equal(12, WorkspaceRenderHelper.RailHeaders(window).Count);
        Capture(window, "links-before.png");
    }

    [AvaloniaFact]
    public void Selecting_the_links_tab_shows_the_seeded_links()
    {
        var (window, vm) = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "LINKS");

        Assert.Equal(4, vm.Links.Links.Count);
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
        WorkspaceRenderHelper.SelectTab(window, "LINKS");
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

    // Rows the Lagebuchführer can actually see: since #518 LinksList holds groups, and a collapsed
    // group's rows still exist in the tree, so count the effectively visible ÖFFNEN buttons.
    private static List<Button> RenderedOpenButtons(Visual root) =>
        root.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "OpenLinkButton" && b.IsEffectivelyVisible)
            .ToList();

    private static int RenderedLinkCount(Visual root) => RenderedOpenButtons(root).Count;

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowLinksTab()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "LINKS");
        return (window, vm);
    }

    [AvaloniaFact]
    public void Typing_a_search_term_narrows_the_rendered_link_list()
    {
        var (window, vm) = ShowLinksTab();
        Assert.Equal(4, RenderedLinkCount(window));

        vm.Links.FilterText = "wetter";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, RenderedLinkCount(window));
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

        Assert.Equal(4, RenderedLinkCount(window));
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

        window.Type("wetter");

        Assert.Equal("wetter", vm.Links.FilterText);
        Assert.Equal(1, RenderedLinkCount(window));

        window.Press(PhysicalKey.Escape);

        Assert.Equal(string.Empty, vm.Links.FilterText);
        Assert.Equal(string.Empty, box.Text);
        Assert.Equal(4, RenderedLinkCount(window));
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
        WorkspaceRenderHelper.SelectTab(window, "LINKS");

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

        var open = RenderedOpenButtons(window)[0];

        var tip = Assert.IsType<string>(ToolTip.GetTip(open));
        Assert.Contains("Browser", tip, StringComparison.Ordinal);
        Assert.NotEmpty(open.GetVisualDescendants().OfType<PathIcon>());
    }

    // --- Issue #518: groups, ÖFFNEN beside the name, banding ---
    private static List<Button> GroupHeaders(Visual root) =>
        root.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "LinkGroupHeader" && b.IsEffectivelyVisible)
            .ToList();

    private static Window ShowLinksView(params Link[] links)
    {
        var view = new LinksView { DataContext = new LinksViewModel(links, new FakeDialogs()) };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_open_button_sits_left_of_the_link_name()
    {
        var (window, _) = ShowLinksTab();

        var open = RenderedOpenButtons(window)[0];
        var name = ((Visual)open.Parent!).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "LinkNameText");
        var openRight = open.TranslatePoint(new Point(open.Bounds.Width, 0), window)!.Value.X;
        var nameLeft = name.TranslatePoint(new Point(0, 0), window)!.Value.X;

        Assert.True(openRight <= nameLeft, $"ÖFFNEN ends at {openRight}px, right of the name starting at {nameLeft}px");
    }

    [AvaloniaFact]
    public void Each_group_gets_a_header_in_stammdaten_order_with_the_ungrouped_last()
    {
        var (window, _) = ShowLinksTab();

        var names = GroupHeaders(window)
            .Select(h => h.GetVisualDescendants().OfType<TextBlock>().First().Text)
            .ToList();

        Assert.Equal(new[] { "Gefahrgut", "Karten", "Ohne Gruppe" }, names);
        Assert.All(GroupHeaders(window), h => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(h))));
        Capture(window, "links-grouped.png");
    }

    [AvaloniaFact]
    public void Clicking_a_group_header_hides_and_shows_its_links()
    {
        var (window, _) = ShowLinksTab();
        var header = GroupHeaders(window)[0];

        header.Command!.Execute(header.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, RenderedLinkCount(window));
        Capture(window, "links-group-collapsed.png");

        header.Command.Execute(header.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, RenderedLinkCount(window));
    }

    [AvaloniaFact]
    public void Collapse_all_and_expand_all_fold_every_group()
    {
        var (window, _) = ShowLinksTab();
        var collapse = Named<Button>(window, "CollapseAllLinkGroupsButton");
        var expand = Named<Button>(window, "ExpandAllLinkGroupsButton");
        Assert.True(collapse.IsEffectivelyVisible);
        Assert.Equal("Alle Gruppen zuklappen", AutomationProperties.GetName(collapse));
        Assert.Equal("Alle Gruppen aufklappen", AutomationProperties.GetName(expand));

        collapse.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, RenderedLinkCount(window));
        Assert.Equal(3, GroupHeaders(window).Count);

        expand.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, RenderedLinkCount(window));
    }

    // A Wehr that never uses groups keeps exactly the flat list it had: no header, nothing to fold.
    [AvaloniaFact]
    public void Without_any_group_the_list_shows_no_headers_and_no_fold_buttons()
    {
        var window = ShowLinksView(new Link("Wetterdienst", "https://dwd.de"), new Link("Kartendienst", "https://example.org/karte"));

        Assert.Empty(GroupHeaders(window));
        Assert.False(Named<Button>(window, "CollapseAllLinkGroupsButton").IsEffectivelyVisible);
        Assert.Equal(2, RenderedLinkCount(window));
    }

    [AvaloniaFact]
    public void Every_other_row_is_tinted()
    {
        var window = ShowLinksView(
            new Link("A", "a.example"), new Link("B", "b.example"), new Link("C", "c.example"));

        var rows = RenderedOpenButtons(window)
            .Select(b => b.FindAncestorOfType<ContentPresenter>()!)
            .ToList();

        Assert.Null(rows[0].Background);
        Assert.NotNull(rows[1].Background);
        Assert.Null(rows[2].Background);
    }
}
