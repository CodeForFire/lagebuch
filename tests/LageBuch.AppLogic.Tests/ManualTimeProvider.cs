namespace LageBuch.AppLogic.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test says so, and whose timers fire
/// only on <see cref="Tick"/>, inline on the caller's thread. Due times and periods are ignored:
/// one Tick is one period of every live timer, which is all the game loop needs.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = new();

    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>How many timers are created and not yet disposed.</summary>
    public int ActiveTimers => _timers.Count;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;

    /// <summary>Fires every live timer once, <paramref name="times"/> times over.</summary>
    public void Tick(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            foreach (var timer in _timers.ToArray())
            {
                timer.Fire();
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public void Fire()
        {
            if (owner._timers.Contains(this))
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => owner._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
