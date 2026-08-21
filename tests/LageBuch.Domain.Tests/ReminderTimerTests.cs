using LageBuch.Domain.Time;

namespace LageBuch.Domain.Tests;

public class ReminderTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void New_timer_is_not_running()
    {
        var timer = new ReminderTimer();
        Assert.False(timer.IsRunning);
        Assert.False(timer.IsDue(T0));
    }

    [Fact]
    public void Start_sets_running_interval_and_anchor()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();

        timer.Start(clock, 15, 30);

        Assert.True(timer.IsRunning);
        Assert.Equal(15, timer.IntervalMinutes);
        Assert.Equal(T0, timer.CycleAnchor);
        Assert.Equal(T0.AddMinutes(15), timer.DueAt);
    }

    [Fact]
    public void Not_due_before_interval_elapses()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();
        timer.Start(clock, 15, 30);

        Assert.False(timer.IsDue(T0.AddMinutes(14)));
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining(T0.AddMinutes(14)));
    }

    [Fact]
    public void Due_at_and_after_interval()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();
        timer.Start(clock, 15, 30);

        Assert.True(timer.IsDue(T0.AddMinutes(15)));
        Assert.True(timer.IsDue(T0.AddMinutes(20)));
        Assert.True(timer.Remaining(T0.AddMinutes(20)) < TimeSpan.Zero);
    }

    [Fact]
    public void Acknowledge_reanchors_to_now()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();
        timer.Start(clock, 15, 15); // equal intervals to isolate re-anchoring

        clock.Now = T0.AddMinutes(20); // overdue
        timer.Acknowledge(clock);

        Assert.False(timer.IsDue(clock.Now));
        Assert.Equal(T0.AddMinutes(20), timer.CycleAnchor);
        Assert.Equal(T0.AddMinutes(35), timer.DueAt);
    }

    [Fact]
    public void First_cycle_uses_the_first_interval_then_acknowledge_switches_to_recurring()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();

        timer.Start(clock, 15, 30);
        Assert.Equal(15, timer.IntervalMinutes);
        Assert.Equal(T0.AddMinutes(15), timer.DueAt); // first alert after 15

        clock.Now = T0.AddMinutes(15);
        timer.Acknowledge(clock);

        Assert.Equal(30, timer.IntervalMinutes);            // now on the recurring cadence
        Assert.Equal(T0.AddMinutes(45), timer.DueAt);       // then every 30
    }

    [Fact]
    public void Stop_clears_running_and_due()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();
        timer.Start(clock, 15, 30);

        timer.Stop();

        Assert.False(timer.IsRunning);
        Assert.False(timer.IsDue(T0.AddMinutes(20)));
    }

    [Fact]
    public void Acknowledge_when_not_running_is_noop()
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();

        timer.Acknowledge(clock); // no throw

        Assert.False(timer.IsRunning);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Start_rejects_nonpositive_interval(int minutes)
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Start(clock, minutes, 30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Start_rejects_nonpositive_recurring_interval(int minutes)
    {
        var clock = new FixedClock(T0);
        var timer = new ReminderTimer();

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Start(clock, 15, minutes));
    }

    [Fact]
    public void Resume_restores_a_running_cycle_from_a_past_anchor()
    {
        var timer = new ReminderTimer();

        // Anchored at T0 on the recurring cadence — as if reopened mid-cycle.
        timer.Resume(T0, currentIntervalMinutes: 30, recurringIntervalMinutes: 30);

        Assert.True(timer.IsRunning);
        Assert.Equal(30, timer.IntervalMinutes);
        Assert.Equal(T0.AddMinutes(30), timer.DueAt);
        Assert.False(timer.IsDue(T0.AddMinutes(29)));
        Assert.True(timer.IsDue(T0.AddMinutes(30)));   // already due when reopened past the interval
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resume_rejects_nonpositive_intervals(int minutes)
    {
        var timer = new ReminderTimer();

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Resume(T0, minutes, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Resume(T0, 15, minutes));
    }
}
