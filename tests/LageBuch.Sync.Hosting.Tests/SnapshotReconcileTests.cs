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

    /// <summary>The poll interval under a <see cref="FakeTimeProvider"/>; only <c>Advance</c> makes it pass.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

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

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task A_host_in_a_new_epoch_is_adopted_whatever_its_revision(long restartedAt)
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.Revised(basis, 7, "Sieben"));
        await using var client = await SnapshotFixture.ConnectAsync(host, Never);

        Assert.Equal(new[] { "Sieben" }, SnapshotFixture.JournalOf(client));

        // A host that started sharing again, counting in a new epoch. Below the client's revision a
        // newer-only rule would ignore it forever; at the *same* revision a bare counter would even
        // call it current. The epoch is what tells both apart from a stale copy.
        host.Current = SnapshotFixture.Revised(basis, restartedAt, "Neu") with { Epoch = Guid.NewGuid() };

        await client.ReconcileAsync();
        await SnapshotFixture.WaitForClient(
            client, () => SnapshotFixture.JournalOf(client).Contains("Neu"), "the new epoch to be adopted");

        Assert.Equal(new[] { "Neu" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task A_resync_that_lands_after_a_newer_push_does_not_regress_the_client()
    {
        // The reconcile fetch and the hub push are two channels with nothing ordering them: the fetch
        // may be answered with revision 3 while a push of revision 4 overtakes it on the way in.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, Never);

        var four = SnapshotFixture.Revised(basis, 4, "Vier");
        await host.PushAsync(four);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Vier"), "revision 4");

        // The host still serves the older state the fetch was answered with.
        host.Current = SnapshotFixture.Revised(basis, 3, "Drei");
        await client.ReconcileAsync();

        // Ordered behind the resync rather than asserted after a sleep: once 5 lands, 3 has been and gone.
        var five = SnapshotFixture.Revised(basis, 5, "Fünf");
        await host.PushAsync(five);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Fünf"), "revision 5");

        Assert.Equal(new[] { "Fünf" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task The_timer_heals_a_missed_broadcast_without_being_driven()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        var time = new FakeTimeProvider();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, Interval, time);

        host.Current = SnapshotFixture.Revised(basis, 3, "Drei");

        // Nothing calls ReconcileAsync here: one interval passing on the clock is all it takes.
        var healed = SnapshotFixture.WaitForClient(
            client, () => SnapshotFixture.JournalOf(client).Contains("Drei"), "the poll timer to heal the gap");
        time.Advance(Interval);
        await healed;
    }

    [Fact]
    public async Task A_reconcile_pass_that_cannot_reach_the_host_reports_failure()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        var time = new FakeTimeProvider();
        var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, Interval, time);

        // The host goes away while SignalR may still believe otherwise. The poll is the only thing that
        // can tell the operator "I cannot confirm this is current", so it must say so rather than
        // faulting the loop and going quiet.
        await host.DisposeAsync();

        var failed = WaitForEvent(
            h => client.ReconcileFailed += h,
            h => client.ReconcileFailed -= h,
            "the failed reconcile pass to be reported",
            TimeSpan.FromSeconds(10));
        time.Advance(Interval);
        await failed;
    }

    [Fact]
    public async Task A_successful_reconcile_pass_is_reported_so_the_footer_can_confirm_currency()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        var time = new FakeTimeProvider();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, Interval, time);

        var reconciled = WaitForEvent(
            h => client.Reconciled += h,
            h => client.Reconciled -= h,
            "a successful reconcile pass to be reported",
            TimeSpan.FromSeconds(10));
        time.Advance(Interval);
        await reconciled;
    }

    [Fact]
    public async Task Disposing_the_session_while_a_pass_is_in_flight_stops_the_poll_without_faulting_it()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        var time = new FakeTimeProvider();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        var client = await SnapshotFixture.ConnectAsync(host, Interval, time);

        // Held at the host, so the tick's GET is certainly in flight when disposal starts. Disposal has
        // to cancel the loop and wait for it before disposing the HttpClient that GET is using, or it
        // lands on a disposed client inside a task nobody observes.
        host.HoldRevisionRequests();
        time.Advance(Interval);
        await host.WaitForRevisionRequestAsync();

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_pass_the_host_never_answers_fails_at_its_deadline_and_the_poll_carries_on()
    {
        // A link that went silent without closing: the request is accepted and then nothing comes
        // back. The pass must give up at SyncProtocol.ReconcileTimeout rather than the HTTP client's
        // 100 s, and — the part that used to go wrong — the loop must survive that cancellation,
        // because a timeout is not the shutdown token.
        var basis = SnapshotFixture.BaseSnapshot();
        var time = new FakeTimeProvider();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host, Interval, time);

        host.HoldRevisionRequests();
        var failed = WaitForEvent(
            h => client.ReconcileFailed += h, h => client.ReconcileFailed -= h, "the silent pass to fail");
        time.Advance(Interval);
        await host.WaitForRevisionRequestAsync();
        time.Advance(SyncProtocol.ReconcileTimeout);
        await failed;

        host.ReleaseRevisionRequests();
        var reconciled = WaitForEvent(
            h => client.Reconciled += h, h => client.Reconciled -= h, "the next tick to reconcile");
        time.Advance(Interval);
        await reconciled;
    }

    [Fact]
    public async Task An_oversized_snapshot_is_refused_and_the_client_keeps_what_it_had()
    {
        // The host is outside the trust boundary: a response beyond SyncProtocol.MaxResponseBytes is
        // not buffered, the pass fails, and the Stand on screen stays the last one that was sound.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.Revised(basis, 1, "Eins"));
        await using var client = await SnapshotFixture.ConnectAsync(host, Never);

        host.Current = SnapshotFixture.Revised(basis, 2, "Zwei");
        host.ServeOversizedSnapshot = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ReconcileAsync());
        Assert.Equal(new[] { "Eins" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task A_reconnect_that_finds_nothing_missed_confirms_the_stand_at_once()
    {
        // Nothing to fetch means nothing applied and no Changed — so unless the reconnect's own pass
        // reports itself, the footer stays "nicht bestätigt" until the next tick despite being current.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var relay = FaultyRelay.Start(host.Port);
        await using var client = await SnapshotFixture.ConnectAsync(
            host, Never, reconnectPolicy: new ReconnectImmediately(), via: relay);

        var reconciled = WaitForEvent(
            h => client.Reconciled += h, h => client.Reconciled -= h, "the reconnect to confirm the Stand", TimeSpan.FromSeconds(10));
        relay.Reset();
        await reconciled;
    }

    [Fact]
    public async Task A_reconnect_whose_catch_up_fails_still_reports_the_reconnect()
    {
        // Regression: the catch-up used to run unguarded inside SignalR's Reconnected handler, so when
        // it threw, Reconnected was never raised — the workspace kept its input disabled for good.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var relay = FaultyRelay.Start(host.Port);
        await using var client = await SnapshotFixture.ConnectAsync(
            host, Never, reconnectPolicy: new ReconnectImmediately(), via: relay);

        host.FailRevisionWith = System.Net.HttpStatusCode.InternalServerError;
        var failed = WaitForEvent(
            h => client.ReconcileFailed += h, h => client.ReconcileFailed -= h, "the catch-up to fail", TimeSpan.FromSeconds(10));
        var reconnected = WaitForEvent(
            h => client.Reconnected += h, h => client.Reconnected -= h, "Reconnected despite the failed catch-up", TimeSpan.FromSeconds(10));

        relay.Reset();

        await failed;
        await reconnected;
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
        var client = await SnapshotFixture.ConnectAsync(host, Interval, new FakeTimeProvider());

        await client.DisposeAsync();
        await client.DisposeAsync();
    }
}
