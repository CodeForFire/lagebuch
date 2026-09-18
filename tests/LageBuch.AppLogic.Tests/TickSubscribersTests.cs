using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class TickSubscribersTests
{
    [Fact]
    public void Notify_calls_every_subscriber()
    {
        var calls = new List<string>();
        var subscribers = new TickSubscribers();
        subscribers.Add(() => calls.Add("a"));
        subscribers.Add(() => calls.Add("b"));

        subscribers.Notify();

        Assert.Equal(new[] { "a", "b" }, calls);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_stop_the_others()
    {
        // The whole point: on the UI thread an unhandled tick exception used to abort the rest of
        // the tick, so whichever of Atemschutz / Aufgaben / ILS-Erinnerung came after the failing
        // one simply stopped updating.
        var reached = new List<string>();
        var subscribers = new TickSubscribers(_ => { });
        subscribers.Add(() => reached.Add("before"));
        subscribers.Add(() => throw new InvalidOperationException("kaputt"));
        subscribers.Add(() => reached.Add("after"));

        subscribers.Notify();

        Assert.Equal(new[] { "before", "after" }, reached);
    }

    [Fact]
    public void A_throwing_subscriber_is_reported()
    {
        var reported = new List<Exception>();
        var subscribers = new TickSubscribers(reported.Add);
        subscribers.Add(() => throw new InvalidOperationException("kaputt"));

        subscribers.Notify();

        Assert.Equal("kaputt", Assert.Single(reported).Message);
    }

    [Fact]
    public void A_subscriber_that_keeps_throwing_keeps_being_reported()
    {
        // Isolation must not silently drop a broken subscriber either — it stays subscribed and
        // stays noisy, so the failure is still discoverable on the tenth tick.
        var reported = 0;
        var subscribers = new TickSubscribers(_ => reported++);
        subscribers.Add(() => throw new InvalidOperationException("kaputt"));

        subscribers.Notify();
        subscribers.Notify();
        subscribers.Notify();

        Assert.Equal(3, reported);
    }

    [Fact]
    public void A_removed_subscriber_is_not_notified()
    {
        var calls = 0;
        void OnTick() => calls++;
        var subscribers = new TickSubscribers();
        subscribers.Add(OnTick);

        subscribers.Remove(OnTick);
        subscribers.Notify();

        Assert.Equal(0, calls);
        Assert.Equal(0, subscribers.Count);
    }

    [Fact]
    public void Unsubscribing_mid_tick_still_completes_the_tick_that_is_running()
    {
        // Notify reads the snapshot once, so the set cannot change under its feet part-way through.
        var reached = new List<string>();
        var subscribers = new TickSubscribers();
        void Second() => reached.Add("second");

        subscribers.Add(() =>
        {
            reached.Add("first");
            subscribers.Remove(Second);
        });
        subscribers.Add(Second);

        subscribers.Notify();

        Assert.Equal(new[] { "first", "second" }, reached);

        // ...but the next tick sees the removal.
        reached.Clear();
        subscribers.Notify();
        Assert.Equal(new[] { "first" }, reached);
    }

    [Fact]
    public void The_first_add_and_the_last_remove_fire_their_callbacks_once()
    {
        var started = 0;
        var stopped = 0;
        var subscribers = new TickSubscribers();

        void A()
        {
        }

        void B()
        {
        }

        subscribers.Add(A, onFirstSubscriber: () => started++);
        subscribers.Add(B, onFirstSubscriber: () => started++);
        Assert.Equal(1, started);

        subscribers.Remove(A, onLastSubscriberGone: () => stopped++);
        Assert.Equal(0, stopped);

        subscribers.Remove(B, onLastSubscriberGone: () => stopped++);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void Removing_a_delegate_that_was_never_added_still_reports_the_set_as_empty()
    {
        // Matches the previous List.Remove behaviour: a no-op removal on an empty set leaves it
        // empty, and the "nothing left" callback is what stops the timer.
        var stopped = 0;
        var subscribers = new TickSubscribers();

        subscribers.Remove(() => { }, onLastSubscriberGone: () => stopped++);

        Assert.Equal(1, stopped);
        Assert.Equal(0, subscribers.Count);
    }

    [Fact]
    public void The_same_delegate_subscribed_twice_needs_two_removals()
    {
        var calls = 0;
        void OnTick() => calls++;
        var subscribers = new TickSubscribers();
        subscribers.Add(OnTick);
        subscribers.Add(OnTick);

        subscribers.Remove(OnTick);
        subscribers.Notify();

        Assert.Equal(1, calls);
        Assert.Equal(1, subscribers.Count);
    }

    [Fact]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The assertion is that no exception of any shape escapes; a narrower catch would let an unanticipated one kill the thread instead of failing the test.")]
    public void Add_and_remove_from_many_threads_do_not_throw_or_lose_the_count()
    {
        // Mirrors DispatcherTimerTickerTests' concurrency fact (#202, #212) at this layer, with a
        // per-iteration captured local so each delegate has its own identity.
        var errors = new ConcurrentBag<Exception>();
        var subscribers = new TickSubscribers();

        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 2000; i++)
                {
                    var iterationId = i;
                    void OnTick() => _ = iterationId;
                    subscribers.Add(OnTick);
                    subscribers.Remove(OnTick);
                }
            }
            catch (Exception ex)
            {
                // Deliberately every shape, not just the ones an unsynchronised List<Action>
                // happens to fail with -- InvalidOperationException, ArgumentException,
                // NullReferenceException, IndexOutOfRangeException when a resize lands mid-copy.
                // The assertion below is "nothing went wrong", so a narrower catch would let an
                // unanticipated shape escape onto a bare Thread, where an unhandled exception
                // takes the test host down instead of failing this test with the exception in hand.
                errors.Add(ex);
            }
        })).ToList();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.Empty(errors);
        Assert.Equal(0, subscribers.Count);
    }
}
