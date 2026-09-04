using System.Collections.Concurrent;
using LageBuch.App.Shared.Services;

namespace LageBuch.App.Tests;

public class DispatcherTimerTickerTests
{
    [Fact]
    public void SubscribeAndUnsubscribe_FromMultipleThreadsConcurrently_DoesNotThrow()
    {
        var ticker = new DispatcherTimerTicker();
        var exceptions = new ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            // CA1031: any exception here is a concurrency failure under test, captured for the assertion below, not swallowed.
#pragma warning disable CA1031
            try
            {
                for (var i = 0; i < 2000; i++)
                {
                    // Capture a fresh per-iteration local so the compiler can't collapse this
                    // into the single cached static delegate it uses for non-capturing lambdas —
                    // Unsubscribe removes by delegate identity, so a shared delegate here would
                    // let the test pass even if Unsubscribe started removing the wrong entry.
                    var iterationId = i;
                    var subscription = ticker.Subscribe(() => _ = iterationId);
                    subscription.Dispose();
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
#pragma warning restore CA1031
        })).ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.Empty(exceptions);
    }
}
