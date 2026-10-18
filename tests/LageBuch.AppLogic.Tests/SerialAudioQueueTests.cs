using System.Diagnostics;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class SerialAudioQueueTests
{
    [Fact]
    public void Enqueued_actions_do_not_overlap()
    {
        var sw = Stopwatch.StartNew();
        var events = new List<(string Id, long StartMs, long EndMs)>();
        var done = new CountdownEvent(2);
        var queue = new SerialAudioQueue();

        queue.Enqueue(_ => RecordTimedRun("a", sw, events, done, TimeSpan.FromMilliseconds(150)));
        queue.Enqueue(_ => RecordTimedRun("b", sw, events, done, TimeSpan.FromMilliseconds(50)));

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "queued actions never completed");
        Assert.Equal(2, events.Count);
        Assert.True(
            events[1].StartMs >= events[0].EndMs,
            "second action started before the first one finished");
    }

    [Fact]
    public void Enqueued_actions_run_in_fifo_order()
    {
        var order = new List<int>();
        var done = new CountdownEvent(5);
        var queue = new SerialAudioQueue();

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

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "queued actions never completed");
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, order);
    }

    [Fact]
    public void Enqueue_returns_without_waiting_for_playback_to_finish()
    {
        var queue = new SerialAudioQueue();
        var started = new ManualResetEventSlim();
        var finish = new ManualResetEventSlim();

        try
        {
            // Gated rather than a fixed sleep, so the action is provably still in flight when the
            // second Enqueue runs below, and so its thread is released when this test ends rather
            // than lingering for a couple of seconds afterwards.
            queue.Enqueue(_ =>
            {
                started.Set();
                finish.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            });

            // Liveness, not latency: same 5s budget as this file's other waits.
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "action never started");

            var sw = Stopwatch.StartNew();
            queue.Enqueue(_ => { });
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), "Enqueue blocked the caller");
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
            // until it returns. Gate it rather than sleeping 30s so the abandoned thread goes away
            // with the test instead of idling on for half a minute.
            queue.Enqueue(_ => unstick.Wait(TimeSpan.FromSeconds(30), CancellationToken.None));
            queue.Enqueue(_ => laterRan.Set());

            Assert.True(
                laterRan.Wait(TimeSpan.FromSeconds(5)),
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
            if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)))
            {
                stopRequested.Set();
            }
        });

        Assert.True(
            stopRequested.Wait(TimeSpan.FromSeconds(5)),
            "the overrunning action was never told to stop");
    }

    [Fact]
    public void An_action_that_finishes_in_time_is_never_cancelled()
    {
        var queue = new SerialAudioQueue();
        var token = CancellationToken.None;
        var laterRan = new ManualResetEventSlim();

        queue.Enqueue(ct => token = ct);

        // The queue serves the next action only after it is done with the first, so by the time
        // this one runs the watchdog has had its chance to cancel and must not have taken it.
        queue.Enqueue(_ => laterRan.Set());

        Assert.True(laterRan.Wait(TimeSpan.FromSeconds(5)), "queued actions never completed");
        Assert.True(token.CanBeCanceled, "the action was not handed the queue's token");
        Assert.False(token.IsCancellationRequested, "an action that finished in time was cancelled");
    }

    [Fact]
    public void A_stopped_action_ends_before_the_next_one_starts()
    {
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.Zero, cancelGrace: TimeSpan.FromSeconds(30));
        var firstEnded = false;
        var secondSawFirstEnded = false;
        var secondRan = new ManualResetEventSlim();

        queue.Enqueue(ct =>
        {
            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            Volatile.Write(ref firstEnded, true);
        });
        queue.Enqueue(_ =>
        {
            secondSawFirstEnded = Volatile.Read(ref firstEnded);
            secondRan.Set();
        });

        Assert.True(secondRan.Wait(TimeSpan.FromSeconds(5)), "the next action never ran");
        Assert.True(secondSawFirstEnded, "the next action started while the stopped one was still playing");
    }

    [Fact]
    public void An_action_that_ignores_its_stop_is_abandoned_once_the_grace_runs_out()
    {
        // No grace at all, so the next action must not wait the default two seconds for a cue that
        // ignores its token (Windows' PlaySound cannot be interrupted).
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.Zero, cancelGrace: TimeSpan.Zero);
        using var release = new ManualResetEventSlim();
        using var secondRan = new ManualResetEventSlim();

        queue.Enqueue(_ => release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None));
        queue.Enqueue(_ => secondRan.Set());

        try
        {
            Assert.True(secondRan.Wait(TimeSpan.FromSeconds(1)), "the next action waited longer than the configured grace");
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

        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)), "the action never ran");
        Assert.True(isBackground, "the action ran on a foreground thread");
        Assert.Equal("SerialAudioQueue item", name);
    }

    private static void RecordTimedRun(
        string id,
        Stopwatch sw,
        List<(string Id, long StartMs, long EndMs)> events,
        CountdownEvent done,
        TimeSpan sleep)
    {
        var start = sw.ElapsedMilliseconds;
        Thread.Sleep(sleep);
        var end = sw.ElapsedMilliseconds;
        lock (events)
        {
            events.Add((id, start, end));
        }

        done.Signal();
    }
}
