namespace LageBuch.Sync.Hosting.Tests;

public class PinRateLimiterTests
{
    [Fact]
    public void An_unknown_ip_is_never_throttled()
    {
        var limiter = new PinRateLimiter();
        Assert.False(limiter.ShouldThrottle("10.0.0.1", out _));
    }

    [Fact]
    public void A_failure_opens_a_backoff_window_for_the_next_attempt()
    {
        var limiter = new PinRateLimiter(new FakeTimeProvider());
        limiter.RecordFailure("10.0.0.1");
        Assert.True(limiter.ShouldThrottle("10.0.0.1", out var retryAfter));
        Assert.InRange(retryAfter, 1, 60);
    }

    [Fact]
    public void Backoff_grows_exponentially_with_more_failures()
    {
        var t = new FakeTimeProvider();
        var limiter = new PinRateLimiter(t);
        var ip = "10.0.0.1";

        limiter.RecordFailure(ip);
        Assert.True(limiter.ShouldThrottle(ip, out var afterOne));
        Assert.True(afterOne >= 1);

        t.Advance(TimeSpan.FromSeconds(10));
        limiter.RecordFailure(ip);
        Assert.True(limiter.ShouldThrottle(ip, out var afterTwo));
        Assert.True(afterTwo > afterOne);
    }

    [Fact]
    public void Throttle_is_per_ip_not_global()
    {
        var limiter = new PinRateLimiter(new FakeTimeProvider());
        limiter.RecordFailure("10.0.0.1");
        Assert.False(limiter.ShouldThrottle("10.0.0.2", out _));
    }

    [Fact]
    public void Backoff_caps_at_60_seconds()
    {
        var t = new FakeTimeProvider();
        var limiter = new PinRateLimiter(t);
        for (var i = 0; i < 10; i++)
        {
            limiter.RecordFailure("10.0.0.1");
        }

        Assert.True(limiter.ShouldThrottle("10.0.0.1", out var retryAfter));
        Assert.Equal(60, retryAfter);
    }

    [Fact]
    public void Success_resets_the_failure_count()
    {
        var limiter = new PinRateLimiter(new FakeTimeProvider());
        limiter.RecordFailure("10.0.0.1");
        limiter.RecordFailure("10.0.0.1");
        limiter.RecordSuccess("10.0.0.1");
        Assert.False(limiter.ShouldThrottle("10.0.0.1", out _));
    }

    [Fact]
    public void Window_elapsing_allows_an_immediate_retry()
    {
        var t = new FakeTimeProvider();
        var limiter = new PinRateLimiter(t);
        limiter.RecordFailure("10.0.0.1");
        Assert.True(limiter.ShouldThrottle("10.0.0.1", out _));

        t.Advance(TimeSpan.FromSeconds(61));
        Assert.False(limiter.ShouldThrottle("10.0.0.1", out _));
    }
}
