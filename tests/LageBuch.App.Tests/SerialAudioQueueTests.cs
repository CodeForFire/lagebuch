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

        queue.Enqueue(() =>
        {
            started.Set();
            Thread.Sleep(TimeSpan.FromSeconds(2));
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)), "action never started");

        var sw = Stopwatch.StartNew();
        queue.Enqueue(() => { });
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), "Enqueue blocked the caller");
    }

    [Fact]
    public void A_stuck_action_does_not_permanently_block_later_ones()
    {
        var queue = new SerialAudioQueue(perItemTimeout: TimeSpan.FromMilliseconds(200));
        var laterRan = new ManualResetEventSlim();

        queue.Enqueue(() => Thread.Sleep(TimeSpan.FromSeconds(30))); // simulates a hung player
        queue.Enqueue(() => laterRan.Set());

        // The later action's own Task.Run still has to wait its turn for a thread-pool thread,
        // which under CI load can take longer than the 200ms watchdog itself — same 5s budget as
        // this file's other CountdownEvent/ManualResetEventSlim waits, not a tighter one.
        Assert.True(
            laterRan.Wait(TimeSpan.FromSeconds(5)),
            "later action never ran; the stuck item wedged the queue");
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
