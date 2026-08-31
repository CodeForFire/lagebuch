using System.Collections.Concurrent;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// Per-source-IP brute-force guard for the share PIN (§ P0 #3). After a failed attempt the next
/// attempt from that IP is delayed by 2^(failures-1) seconds (capped at <see cref="MaxBackoffSeconds"/>);
/// a correct PIN clears the counter. One instance lives for the lifetime of the host middleware,
/// so state is deliberately lost on restart (share sessions are short-lived).
/// </summary>
internal sealed class PinRateLimiter
{
    private readonly ConcurrentDictionary<string, State> _attempts = new();
    private readonly TimeProvider _time;

    public PinRateLimiter(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public static int MaxBackoffSeconds { get; } = 60;

    /// <summary>
    /// True if <paramref name="ip"/> is inside a backoff window; <paramref name="retryAfterSeconds"/>
    /// is the remaining seconds it must wait, rounded up, else 0.
    /// </summary>
    public bool ShouldThrottle(string ip, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!_attempts.TryGetValue(ip, out var state))
            return false;

        var elapsed = (_time.GetUtcNow() - state.LastFailure).TotalSeconds;
        var delay = Math.Min(BackoffSeconds(state.Failures), MaxBackoffSeconds);
        if (elapsed < delay)
        {
            retryAfterSeconds = (int)Math.Ceiling(delay - elapsed);
            return true;
        }
        return false;
    }

    public void RecordFailure(string ip) =>
        _attempts.AddOrUpdate(ip,
            _ => new State(1, _time.GetUtcNow()),
            (_, s) => new State(s.Failures + 1, _time.GetUtcNow()));

    public void RecordSuccess(string ip) => _attempts.TryRemove(ip, out _);

    private static double BackoffSeconds(int failures) => Math.Pow(2, failures - 1);

    private sealed record State(int Failures, DateTimeOffset LastFailure);
}
