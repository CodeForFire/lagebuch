using Avalonia.Threading;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Shared.Services;

/// <summary>
/// ITicker backed by Avalonia's DispatcherTimer — fires on the UI thread once per second.
/// A single shared DispatcherTimer multiplexes all current subscribers; it runs only while
/// at least one subscription is alive.
/// <para>
/// The subscriber set and the fan-out live in <see cref="TickSubscribers"/>, which has no timer and
/// no UI dependency and is therefore testable without a dispatcher. This class is only the timer:
/// it owns when ticks happen, not what a tick does.
/// </para>
/// </summary>
public sealed class DispatcherTimerTicker : ITicker
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TickSubscribers _subscribers;

    /// <param name="onSubscriberError">
    /// Where a throwing tick subscriber is reported. Left unset it goes to <c>Trace</c>; a head that
    /// has somewhere better to put it (the workspace's own error surface, as #280 did for background
    /// save failures) can pass that instead.
    /// </param>
    public DispatcherTimerTicker(Action<Exception>? onSubscriberError = null)
    {
        _subscribers = new TickSubscribers(onSubscriberError);
        _timer.Tick += (_, _) => _subscribers.Notify();
    }

    public IDisposable Subscribe(Action onTick)
    {
        // Start under TickSubscribers' own lock, so a concurrent Dispose cannot interleave between
        // the add and the Start and leave the timer stopped with a live subscriber (issue #202).
        _subscribers.Add(onTick, onFirstSubscriber: _timer.Start);
        return new Subscription(this, onTick);
    }

    private void Unsubscribe(Action onTick) =>
        _subscribers.Remove(onTick, onLastSubscriberGone: _timer.Stop);

    private sealed class Subscription : IDisposable
    {
        private readonly DispatcherTimerTicker _owner;
        private readonly Action _onTick;
        private bool _disposed;

        public Subscription(DispatcherTimerTicker owner, Action onTick)
        {
            _owner = owner;
            _onTick = onTick;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Unsubscribe(_onTick);
        }
    }
}
