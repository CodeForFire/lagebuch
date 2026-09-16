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
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Issue #167 review follow-up: IncidentStore's background writer can silently fail a queued write
// (disk full, read-only, locked/corrupt DB) and nothing in src/ used to surface that -- the screen
// kept saying "gespeichert" while nothing reached disk. IncidentWorkspaceViewModel now exposes
// PersistenceError, and this pins the red banner it drives. FakeStore, FakeDialogs, FixedClock,
// NoopTicker, NoopAlarmService are shared from WorkspaceAcceptanceTests.cs.
public class PersistenceErrorBannerTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

    private static IncidentWorkspaceViewModel BuildWorkspace()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>(),
            keyword: "B3P");
        return new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            Md(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
    }

    private static Window Show(IncidentWorkspaceViewModel vm)
    {
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1280, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
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

    private static Border Banner(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PersistenceErrorBanner");

    private static StackPanel SavedStatus(Window window) =>
        window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SavedStatus");

    private static StackPanel NotSavedStatus(Window window) =>
        window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "NotSavedStatus");

    [AvaloniaFact]
    public void The_banner_is_hidden_while_PersistenceError_is_null()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        Assert.Null(vm.PersistenceError);
        Assert.False(Banner(window).IsVisible);

        // The green "gespeichert" footer is the healthy default; its red counterpart must not show.
        Assert.True(SavedStatus(window).IsVisible);
        Assert.False(NotSavedStatus(window).IsVisible);
        Capture(window, "persistence-error-before.png");
    }

    // Regression guard (review fix round 1): the green "● gespeichert HH:mm:ss" footer used to keep
    // showing while the red banner was up -- LastSavedAt is set when Save() is *called*, not when
    // the write actually lands, so the two flatly contradicted each other. They must now be
    // mutually exclusive.
    [AvaloniaFact]
    public void Setting_PersistenceError_shows_the_banner_and_swaps_the_footer_to_not_saved()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        vm.PersistenceError = "Speichern fehlgeschlagen: Datenträger voll — Änderungen werden NICHT gesichert.";
        Dispatcher.UIThread.RunJobs();

        var banner = Banner(window);
        Assert.True(banner.IsVisible);
        var text = banner.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PersistenceErrorText");
        Assert.Equal(vm.PersistenceError, text.Text);

        // The contradictory pairing this fixes: green "gespeichert" must be gone while the banner
        // (and the red footer state) are up.
        Assert.False(SavedStatus(window).IsVisible);
        Assert.True(NotSavedStatus(window).IsVisible);
        Capture(window, "persistence-error-after.png");
    }

    [AvaloniaFact]
    public void Clearing_PersistenceError_hides_the_banner_and_restores_the_saved_footer()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        vm.PersistenceError = "Speichern fehlgeschlagen: Datenträger voll — Änderungen werden NICHT gesichert.";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Banner(window).IsVisible);
        Assert.True(NotSavedStatus(window).IsVisible);

        vm.PersistenceError = null;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Banner(window).IsVisible);
        Assert.True(SavedStatus(window).IsVisible);
        Assert.False(NotSavedStatus(window).IsVisible);
    }
}
