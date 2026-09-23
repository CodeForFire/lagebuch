using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #295: a joined client used to report "gespeichert HH:mm:ss" in the footer despite never writing a
// byte, and it had no way of saying that what is on screen might no longer be the Lage. This pins the
// three mutually exclusive footer states and the rejected-command banner, and doubles as the PR's
// before/after capture. Modelled on PersistenceErrorBannerTests; FakeStore, FakeDialogs, FixedClock,
// NoopTicker, NoopAlarmService are shared from WorkspaceAcceptanceTests.cs.
//
// SyncState is driven directly rather than through a live host: it is seeded from session.IsRemote at
// construction but stays settable, which is what lets this run headless with no Kestrel, no TLS and no
// third IIncidentSession forwarder (see #298 on the two that already exist).
public class RemoteSyncStatusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

    private static IncidentWorkspaceViewModel BuildWorkspace()
    {
        var session = TestSession.StartNew(
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
        frame.SavePng(Path.Join(dir, name));
    }

    private static StackPanel Panel(Window window, string name) =>
        window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == name);

    private static Border Banner(Window window, string name) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);

    [AvaloniaFact]
    public void A_hosting_device_reports_a_saved_state_and_no_sync_state()
    {
        var vm = BuildWorkspace();
        var window = Show(vm);

        Assert.Equal(WorkspaceSyncState.Local, vm.SyncState);
        Assert.True(Panel(window, "SavedStatus").IsVisible);
        Assert.False(Panel(window, "SyncCurrentStatus").IsVisible);
        Assert.False(Panel(window, "SyncUnconfirmedStatus").IsVisible);
        Assert.False(Banner(window, "CommandRejectedBanner").IsVisible);

        Capture(window, "sync-status-before.png");
    }

    [AvaloniaFact]
    public void A_joined_client_reports_the_hosts_stand_instead_of_a_save()
    {
        var vm = BuildWorkspace();
        vm.SyncState = WorkspaceSyncState.Current;
        vm.LastSyncedAt = new DateTimeOffset(2026, 8, 12, 9, 14, 32, TimeSpan.Zero);
        var window = Show(vm);

        Assert.False(Panel(window, "SavedStatus").IsVisible);
        Assert.True(Panel(window, "SyncCurrentStatus").IsVisible);
        Assert.False(Panel(window, "SyncUnconfirmedStatus").IsVisible);
    }

    [AvaloniaFact]
    public void An_unconfirmed_client_marks_its_stand_and_shows_a_rejected_command()
    {
        var vm = BuildWorkspace();
        vm.SyncState = WorkspaceSyncState.Unconfirmed;
        vm.LastSyncedAt = new DateTimeOffset(2026, 8, 12, 9, 14, 32, TimeSpan.Zero);
        vm.CommandRejected = "Änderung nicht übernommen: Der Einsatz ist abgeschlossen und schreibgeschützt.";
        var window = Show(vm);

        Assert.False(Panel(window, "SavedStatus").IsVisible);
        Assert.False(Panel(window, "SyncCurrentStatus").IsVisible);
        Assert.True(Panel(window, "SyncUnconfirmedStatus").IsVisible);
        Assert.True(Banner(window, "CommandRejectedBanner").IsVisible);

        Capture(window, "sync-status-after.png");
    }

    [AvaloniaFact]
    public void Dismissing_a_rejected_command_hides_its_banner()
    {
        var vm = BuildWorkspace();
        vm.CommandRejected = "Änderung nicht übernommen: ETB-Eintrag nicht gefunden.";
        var window = Show(vm);

        Assert.True(Banner(window, "CommandRejectedBanner").IsVisible);

        vm.DismissCommandRejectedCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Banner(window, "CommandRejectedBanner").IsVisible);
    }

    [AvaloniaFact]
    public void A_persistence_failure_still_wins_over_the_local_saved_state()
    {
        // ShowLocalSavedStatus folds in the PersistenceError check the binding used to carry directly, so
        // the footer and the red banner cannot contradict each other. Pinned here because that condition
        // moved from the view into the view-model.
        var vm = BuildWorkspace();
        vm.PersistenceError = "Speichern fehlgeschlagen: Datenträger voll.";
        var window = Show(vm);

        Assert.False(Panel(window, "SavedStatus").IsVisible);
        Assert.True(Panel(window, "NotSavedStatus").IsVisible);
    }
}
