namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The anti-entropy net (#295): a joined client polls the host's revision and re-fetches when the two
/// disagree, so a broadcast that never arrived — for whatever reason, including ones nothing can
/// enumerate — heals within one poll interval instead of never.
/// <para>
/// These drive a <see cref="ScriptedSnapshotHost"/>, because a lost broadcast is not reachable through
/// the real host; its remarks record why and which alternatives were rejected.
/// </para>
/// </summary>
public class SnapshotReconcileTests
{
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(10);

    /// <summary>Completes when <paramref name="subscribe"/>'s event fires, so no test sleeps.</summary>
    private static async Task WaitForEvent(
        Action<Action> subscribe, Action<Action> unsubscribe, string description, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource();
        void Handler() => tcs.TrySetResult();
        subscribe(Handler);
        try
        {
            await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Timed out waiting for {description}.", ex);
        }
        finally
        {
            unsubscribe(Handler);
        }
    }

    [Fact]
    public async Task One_reconcile_pass_heals_a_broadcast_that_never_arrived()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);

        // The interval is set past the end of the test so the timer never fires by itself: this asserts
        // what one pass does, not when the timer happens to run.
        await using var client = await SnapshotFixture.ConnectAsync(host, Never);

        // The host moved on and the broadcast was lost — no PushAsync at all. This is the case nothing
        // else in the design can see: SignalR stayed up, so no reconnect fires and no resync follows.
        host.Current = SnapshotFixture.Revised(basis, 9, "Neun");
        Assert.DoesNotContain("Neun", SnapshotFixture.JournalOf(client));

        await client.ReconcileAsync();
        await SnapshotFixture.WaitForClient(
            client, () => SnapshotFixture.JournalOf(client).Contains("Neun"), "the reconcile pass to catch up");

        Assert.Equal(new[] { "Neun" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task A_reconcile_pass_that_is_already_level_changes_nothing()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.Revised(basis, 4, "Vier"));
        await using var client = await SnapshotFixture.ConnectAsync(host, Never);

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        await client.ReconcileAsync();

        Assert.Equal(0, Volatile.Read(ref applied));
        Assert.Equal(new[] { "Vier" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task A_host_that_restarted_sharing_at_a_lower_revision_is_adopted()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.Revised(basis, 7, "Sieben"));
        await using var client = await SnapshotFixture.ConnectAsync(host, Never);

        Assert.Equal(new[] { "Sieben" }, SnapshotFixture.JournalOf(client));

        // A host whose counter went back to zero. Comparing with != rather than > is what notices this;
        // newer-only would leave the client showing revision 7 forever.
        host.Current = SnapshotFixture.Revised(basis, 1, "Eins");

        await client.ReconcileAsync();
        await SnapshotFixture.WaitForClient(
            client, () => SnapshotFixture.JournalOf(client).Contains("Eins"), "the restarted host to be adopted");

        Assert.Equal(new[] { "Eins" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task The_timer_heals_a_missed_broadcast_without_being_driven()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, TimeSpan.FromMilliseconds(50));

        host.Current = SnapshotFixture.Revised(basis, 3, "Drei");

        // The one timing-dependent test here; everything else drives ReconcileAsync directly.
        await SnapshotFixture.WaitForClient(
            client, () => SnapshotFixture.JournalOf(client).Contains("Drei"), "the poll timer to heal the gap");
    }

    [Fact]
    public async Task A_reconcile_pass_that_cannot_reach_the_host_reports_failure()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, TimeSpan.FromMilliseconds(50));

        // The host goes away while SignalR may still believe otherwise. The poll is the only thing that
        // can tell the operator "I cannot confirm this is current", so it must say so rather than
        // faulting the loop and going quiet.
        await host.DisposeAsync();

        await WaitForEvent(
            h => client.ReconcileFailed += h,
            h => client.ReconcileFailed -= h,
            "the failed reconcile pass to be reported",
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_successful_reconcile_pass_is_reported_so_the_footer_can_confirm_currency()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, TimeSpan.FromMilliseconds(50));

        await WaitForEvent(
            h => client.Reconciled += h,
            h => client.Reconciled -= h,
            "a successful reconcile pass to be reported",
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Disposing_the_session_stops_the_poll_without_faulting_it()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);

        // A 1 ms interval makes a GET very likely to be in flight at the moment of disposal. Disposal
        // has to cancel the loop and wait for it before disposing the HttpClient it is using, or that
        // GET lands on a disposed client inside a task nobody observes.
        var client = await SnapshotFixture.ConnectAsync(host, TimeSpan.FromMilliseconds(1));
        await SnapshotFixture.WaitForClient(client, () => true, "the client to settle");

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Disposing_the_session_twice_is_safe()
    {
        // IncidentWorkspaceViewModel.LeaveAsync disposes the session, and whoever owns it disposes it
        // again — an `await using` in a test, the shell on shutdown in the app. Everything torn down
        // here has always tolerated that; the reconcile loop's CancellationTokenSource must too, or the
        // second call throws ObjectDisposedException from a thread-pool thread and takes the process
        // with it.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        var client = await SnapshotFixture.ConnectAsync(host, TimeSpan.FromMilliseconds(50));

        await client.DisposeAsync();
        await client.DisposeAsync();
    }
}
