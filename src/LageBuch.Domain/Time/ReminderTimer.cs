namespace LageBuch.Domain.Time;

/// <summary>
/// Transient (session-only) two-stage reminder for the "Rückmeldung an ILS": the first cycle runs
/// for <see cref="IntervalMinutes"/> after <see cref="Start"/>, and every acknowledged cycle
/// thereafter runs for <see cref="RecurringIntervalMinutes"/>. Pure time math driven by a supplied
/// clock/now — no threads, no events, not persisted.
/// </summary>
public sealed class ReminderTimer
{
    public bool IsRunning { get; private set; }

    /// <summary>Length of the current cycle: the first interval until acknowledged, then recurring.</summary>
    public int IntervalMinutes { get; private set; }

    /// <summary>Length of every cycle after the first acknowledgement.</summary>
    public int RecurringIntervalMinutes { get; private set; }

    public DateTimeOffset CycleAnchor { get; private set; }

    public DateTimeOffset DueAt => CycleAnchor + TimeSpan.FromMinutes(IntervalMinutes);

    public void Start(IClock clock, int firstIntervalMinutes, int recurringIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (firstIntervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstIntervalMinutes), "Interval must be positive.");
        if (recurringIntervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(recurringIntervalMinutes), "Interval must be positive.");
        IntervalMinutes = firstIntervalMinutes;
        RecurringIntervalMinutes = recurringIntervalMinutes;
        CycleAnchor = clock.Now;
        IsRunning = true;
    }

    /// <summary>
    /// Restores a running timer from persisted state (an anchor in the past) — the rehydration
    /// counterpart to <see cref="Start"/>, used after a reopen/crash. The current cycle length is
    /// supplied directly (the first interval until the first acknowledgement, the recurring interval
    /// thereafter). May leave the timer already due when the anchor is older than the interval.
    /// </summary>
    public void Resume(DateTimeOffset anchor, int currentIntervalMinutes, int recurringIntervalMinutes)
    {
        if (currentIntervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentIntervalMinutes), "Interval must be positive.");
        if (recurringIntervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(recurringIntervalMinutes), "Interval must be positive.");
        IntervalMinutes = currentIntervalMinutes;
        RecurringIntervalMinutes = recurringIntervalMinutes;
        CycleAnchor = anchor;
        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    public void Acknowledge(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!IsRunning)
            return;
        // Subsequent cycles run on the recurring interval — the first "after 15 min" gives way to
        // the "then every 30 min" cadence once the crew has reported back at least once.
        IntervalMinutes = RecurringIntervalMinutes;
        CycleAnchor = clock.Now;
    }

    public TimeSpan Remaining(DateTimeOffset now) => DueAt - now;

    public bool IsDue(DateTimeOffset now) => IsRunning && now >= DueAt;
}
