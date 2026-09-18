using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The subscriber bookkeeping and fan-out behind an <see cref="ITicker"/>, with no timer and no UI
/// framework attached so it can be tested without a dispatcher.
/// <para>
/// Two things it guarantees that a bare list does not. Subscribers are notified from a cached
/// snapshot rebuilt only when the set changes, so a tick — once per second, for the lifetime of an
/// open Einsatz — allocates nothing. And each subscriber is invoked in isolation: one that throws
/// no longer skips the ones after it, which on the UI thread also meant the exception escaped the
/// timer's Tick handler as an unhandled dispatcher exception.
/// </para>
/// </summary>
public sealed class TickSubscribers
{
    private readonly object _gate = new();
    private readonly List<Action> _subscribers = new();
    private readonly Action<Exception> _onSubscriberError;

    private Action[] _snapshot = [];

    /// <param name="onSubscriberError">
    /// Where a throwing subscriber's exception goes. Defaults to <see cref="Trace"/> — isolation
    /// must not turn a broken subscriber into a silent one, so there is deliberately no option to
    /// discard it.
    /// </param>
    public TickSubscribers(Action<Exception>? onSubscriberError = null) =>
        _onSubscriberError = onSubscriberError ?? (ex => Trace.TraceError("Tick subscriber failed: {0}", ex));

    /// <summary>How many subscriptions are currently alive. For tests and diagnostics.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    /// <summary>
    /// Adds a subscriber and, if it is the first, runs <paramref name="onFirstSubscriber"/> — under
    /// the same lock, so a caller can start its timer without racing a concurrent
    /// <see cref="Remove"/> into stopping it (issue #202).
    /// </summary>
    public void Add(Action onTick, Action? onFirstSubscriber = null)
    {
        ArgumentNullException.ThrowIfNull(onTick);

        lock (_gate)
        {
            _subscribers.Add(onTick);
            _snapshot = [.. _subscribers];
            if (_subscribers.Count == 1)
            {
                onFirstSubscriber?.Invoke();
            }
        }
    }

    /// <summary>
    /// Removes one subscription and, once none are left, runs <paramref name="onLastSubscriberGone"/>
    /// under the same lock. Removal is by delegate identity and drops a single entry, so subscribing
    /// the same delegate twice needs two disposals — the hazard #212's test pins down.
    /// </summary>
    public void Remove(Action onTick, Action? onLastSubscriberGone = null)
    {
        lock (_gate)
        {
            _subscribers.Remove(onTick);
            _snapshot = [.. _subscribers];
            if (_subscribers.Count == 0)
            {
                onLastSubscriberGone?.Invoke();
            }
        }
    }

    /// <summary>
    /// Notifies every current subscriber. Reads the snapshot once — a reference read is atomic, so
    /// no lock is taken on the tick path and a subscriber that unsubscribes mid-tick still sees the
    /// set as it was when the tick began.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Deliberately broad: isolating subscribers is the point, and the exception is reported rather than dropped.")]
    public void Notify()
    {
        foreach (var subscriber in _snapshot)
        {
            try
            {
                subscriber();
            }
            catch (Exception ex)
            {
                _onSubscriberError(ex);
            }
        }
    }
}
