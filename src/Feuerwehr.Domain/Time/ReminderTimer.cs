namespace Feuerwehr.Domain.Time;

/// <summary>
/// Transient (session-only) recurring reminder for the "Rückmeldung an ILS".
/// Pure time math driven by a supplied clock/now — no threads, no events, not persisted.
/// </summary>
public sealed class ReminderTimer
{
    public bool IsRunning { get; private set; }
    public int IntervalMinutes { get; private set; }
    public DateTimeOffset CycleAnchor { get; private set; }

    public DateTimeOffset DueAt => CycleAnchor + TimeSpan.FromMinutes(IntervalMinutes);

    public void Start(IClock clock, int intervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (intervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), "Interval must be positive.");
        IntervalMinutes = intervalMinutes;
        CycleAnchor = clock.Now;
        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    public void Acknowledge(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!IsRunning)
            return;
        CycleAnchor = clock.Now;
    }

    public TimeSpan Remaining(DateTimeOffset now) => DueAt - now;

    public bool IsDue(DateTimeOffset now) => IsRunning && now >= DueAt;
}
