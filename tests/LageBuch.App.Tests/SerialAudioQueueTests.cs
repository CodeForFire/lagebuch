using System.Diagnostics;
using LageBuch.App.Services;

namespace LageBuch.App.Tests;

public class SerialAudioQueueTests
{
    [Fact]
    public void Enqueued_actions_do_not_overlap()
    {
        var sw = Stopwatch.StartNew();
        var events = new List<(string Id, long StartMs, long EndMs)>();
        var done = new CountdownEvent(2);
        var queue = new SerialAudioQueue();

        queue.Enqueue(() => RecordTimedRun("a", sw, events, done, TimeSpan.FromMilliseconds(150)));
        queue.Enqueue(() => RecordTimedRun("b", sw, events, done, TimeSpan.FromMilliseconds(50)));

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
            queue.Enqueue(() =>
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
            queue.Enqueue(() =>
            {
                started.Set();
                finish.Wait(TimeSpan.FromSeconds(30));
            });

            // Liveness, not latency: same 5s budget as this file's other waits.
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "action never started");

            var sw = Stopwatch.StartNew();
            queue.Enqueue(() => { });
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
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.FromMilliseconds(200));
        var laterRan = new ManualResetEventSlim();
        var unstick = new ManualResetEventSlim();

        try
        {
            // A hung player: the watchdog abandons it after perItemTimeout, but the action itself
            // runs until it returns. Gate it rather than sleeping 30s so the abandoned thread goes
            // away with the test instead of idling on for half a minute.
            queue.Enqueue(() => unstick.Wait(TimeSpan.FromSeconds(30)));
            queue.Enqueue(() => laterRan.Set());

            Assert.True(
                laterRan.Wait(TimeSpan.FromSeconds(5)),
                "later action never ran; the stuck item wedged the queue");
        }
        finally
        {
            unstick.Set();
        }
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
