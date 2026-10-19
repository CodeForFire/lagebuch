using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class SerialAudioQueueTests
{
    // How long a test waits for something it expects to happen. Generous on purpose: a wait returns
    // as soon as it is signalled, so a passing run pays nothing for it, while CI has frozen this
    // testhost for 4-11 s at a time (every assembly runs at once on a 4-core runner), and a 5 s
    // budget turned that stall into a red build.
    private static readonly TimeSpan Liveness = TimeSpan.FromSeconds(30);

    // How long a gated action holds before giving up on its own. Longer than Liveness, so an
    // action can never end by itself inside a wait and pass a test the queue should have failed.
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(60);

    [Fact]
    public void Enqueued_actions_do_not_overlap()
    {
        // No watchdog: a stall longer than the default timeout would stop the first action and
        // serve the second early, which is the watchdog working, not the queue overlapping.
        var queue = new SerialAudioQueue(perItemTimeout: Timeout.InfiniteTimeSpan);
        var firstEnded = false;
        var secondSawFirstEnded = false;
        using var secondRan = new ManualResetEventSlim();

        queue.Enqueue(_ =>
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(150));
            Volatile.Write(ref firstEnded, true);
        });
        queue.Enqueue(_ =>
        {
            secondSawFirstEnded = Volatile.Read(ref firstEnded);
            secondRan.Set();
        });

        Assert.True(secondRan.Wait(Liveness), "queued actions never completed");
        Assert.True(secondSawFirstEnded, "second action started before the first one finished");
    }

    [Fact]
    public void Enqueued_actions_run_in_fifo_order()
    {
        var order = new List<int>();
        var done = new CountdownEvent(5);
        var queue = new SerialAudioQueue(perItemTimeout: Timeout.InfiniteTimeSpan);

        for (var i = 0; i < 5; i++)
        {
            var id = i;
            queue.Enqueue(_ =>
            {
                lock (order)
                {
                    order.Add(id);
                }

                done.Signal();
            });
        }

        Assert.True(done.Wait(Liveness), "queued actions never completed");
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, order);
    }

    [Fact]
    public void Enqueue_returns_without_waiting_for_playback_to_finish()
    {
        var queue = new SerialAudioQueue();
        var started = new ManualResetEventSlim();
        var finish = new ManualResetEventSlim();
        var playbackEnded = false;

        try
        {
            // Gated rather than a fixed sleep, so the action is provably still in flight when the
            // second Enqueue runs below, and so its thread is released when this test ends rather
            // than lingering afterwards.
            queue.Enqueue(_ =>
            {
                started.Set();
                finish.Wait(Hold, CancellationToken.None);
                Volatile.Write(ref playbackEnded, true);
            });

            Assert.True(started.Wait(Liveness), "action never started");

            queue.Enqueue(_ => { });

            // Not a stopwatch: an Enqueue that waited for playback could only return once the gated
            // action had ended, and nothing releases it before the finally below.
            Assert.False(Volatile.Read(ref playbackEnded), "Enqueue blocked the caller until playback ended");
        }
        finally
        {
            finish.Set();
        }
    }

    [Fact]
    public void A_stuck_action_does_not_permanently_block_later_ones()
    {
        var queue = new SerialAudioQueue(
            perItemTimeout: TimeSpan.FromMilliseconds(200),
            cancelGrace: TimeSpan.FromMilliseconds(200));
        var laterRan = new ManualResetEventSlim();
        var unstick = new ManualResetEventSlim();

        try
        {
            // A hung player that ignores its token, as Windows' PlaySound does: the watchdog
            // abandons it after perItemTimeout plus the cancel grace, but the action itself runs
            // until it returns. Gate it rather than sleeping so the abandoned thread goes away
            // with the test instead of idling on for a minute.
            queue.Enqueue(_ => unstick.Wait(Hold, CancellationToken.None));
            queue.Enqueue(_ => laterRan.Set());

            Assert.True(
                laterRan.Wait(Liveness),
                "later action never ran; the stuck item wedged the queue");
        }
        finally
        {
            unstick.Set();
        }
    }

    [Fact]
    public void An_overrunning_action_is_told_to_stop()
    {
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.Zero);
        var stopRequested = new ManualResetEventSlim();

        // A hung player: it only ends once the queue asks it to. With a zero timeout the
        // watchdog asks straight away, so this proves the token is cancelled, not just abandoned.
        queue.Enqueue(ct =>
        {
            if (ct.WaitHandle.WaitOne(Hold))
            {
                stopRequested.Set();
            }
        });

        Assert.True(
            stopRequested.Wait(Liveness),
            "the overrunning action was never told to stop");
    }

    [Fact]
    public void An_action_that_finishes_in_time_is_never_cancelled()
    {
        // No watchdog, so a stall cannot make "in time" late. The token handed over must still be
        // the queue's own, cancellable one.
        var queue = new SerialAudioQueue(perItemTimeout: Timeout.InfiniteTimeSpan);
        var token = CancellationToken.None;
        var laterRan = new ManualResetEventSlim();

        queue.Enqueue(ct => token = ct);

        // The queue serves the next action only after it is done with the first, so by the time
        // this one runs the watchdog has had its chance to cancel and must not have taken it.
        queue.Enqueue(_ => laterRan.Set());

        Assert.True(laterRan.Wait(Liveness), "queued actions never completed");
        Assert.True(token.CanBeCanceled, "the action was not handed the queue's token");
        Assert.False(token.IsCancellationRequested, "an action that finished in time was cancelled");
    }

    [Fact]
    public void A_stopped_action_ends_before_the_next_one_starts()
    {
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.Zero, cancelGrace: Hold);
        var firstEnded = false;
        var secondSawFirstEnded = false;
        var secondRan = new ManualResetEventSlim();

        queue.Enqueue(ct =>
        {
            ct.WaitHandle.WaitOne(Hold);
            Volatile.Write(ref firstEnded, true);
        });
        queue.Enqueue(_ =>
        {
            secondSawFirstEnded = Volatile.Read(ref firstEnded);
            secondRan.Set();
        });

        Assert.True(secondRan.Wait(Liveness), "the next action never ran");
        Assert.True(secondSawFirstEnded, "the next action started while the stopped one was still playing");
    }

    [Fact]
    public void An_action_that_ignores_its_stop_is_abandoned_once_the_grace_runs_out()
    {
        // No grace at all, for a cue that ignores its token (Windows' PlaySound cannot be
        // interrupted): the next action is served while the first is still playing. How long that
        // takes is not asserted -- a wall-clock bound here would fail whenever CI stalls the testhost.
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.Zero, cancelGrace: TimeSpan.Zero);
        using var release = new ManualResetEventSlim();
        using var secondRan = new ManualResetEventSlim();

        queue.Enqueue(_ => release.Wait(Hold, CancellationToken.None));
        queue.Enqueue(_ => secondRan.Set());

        try
        {
            Assert.True(secondRan.Wait(Liveness), "the next action waited for a cue that ignores its stop");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void Each_action_plays_on_its_own_named_background_thread()
    {
        // Background, so an abandoned cue can never hold up the app's exit.
        var queue = new SerialAudioQueue();
        var isBackground = false;
        string? name = null;
        using var ran = new ManualResetEventSlim();

        queue.Enqueue(_ =>
        {
            isBackground = Thread.CurrentThread.IsBackground;
            name = Thread.CurrentThread.Name;
            ran.Set();
        });

        Assert.True(ran.Wait(Liveness), "the action never ran");
        Assert.True(isBackground, "the action ran on a foreground thread");
        Assert.Equal("SerialAudioQueue item", name);
    }
}
