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

    [AvaloniaFact]
    public void The_banner_is_hidden_while_PersistenceError_is_null()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        Assert.Null(vm.PersistenceError);
        Assert.False(Banner(window).IsVisible);
        Capture(window, "persistence-error-before.png");
    }

    [AvaloniaFact]
    public void Setting_PersistenceError_shows_the_banner_with_its_message()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        vm.PersistenceError = "Speichern fehlgeschlagen: Datenträger voll — Änderungen werden NICHT gesichert.";
        Dispatcher.UIThread.RunJobs();

        var banner = Banner(window);
        Assert.True(banner.IsVisible);
        var text = banner.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PersistenceErrorText");
        Assert.Equal(vm.PersistenceError, text.Text);
        Capture(window, "persistence-error-after.png");
    }

    [AvaloniaFact]
    public void Clearing_PersistenceError_hides_the_banner_again()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        vm.PersistenceError = "Speichern fehlgeschlagen: Datenträger voll — Änderungen werden NICHT gesichert.";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Banner(window).IsVisible);

        vm.PersistenceError = null;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Banner(window).IsVisible);
    }
}
