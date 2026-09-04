using Avalonia.Threading;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Shared.Services;

/// <summary>
/// ITicker backed by Avalonia's DispatcherTimer — fires on the UI thread once per second.
/// A single shared DispatcherTimer multiplexes all current subscribers; it runs only while
/// at least one subscription is alive.
/// </summary>
public sealed class DispatcherTimerTicker : ITicker
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Action> _subscribers = new();
    private readonly object _gate = new();

    public DispatcherTimerTicker() => _timer.Tick += (_, _) => Notify();

    public IDisposable Subscribe(Action onTick)
    {
        lock (_gate)
        {
            _subscribers.Add(onTick);
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        return new Subscription(this, onTick);
    }

    private void Notify()
    {
        Action[] subscribers;
        lock (_gate)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var s in subscribers)
        {
            s();
        }
    }

    private void Unsubscribe(Action onTick)
    {
        lock (_gate)
        {
            _subscribers.Remove(onTick);
            if (_subscribers.Count == 0)
            {
                _timer.Stop();
            }
        }
    }

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
