using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// Decides whether a request's share PIN lets it in (#288). Beside the per-address backoff in
/// <see cref="PinRateLimiter"/>, it spends one failure budget per PIN across every address: that
/// backoff alone is keyed per source IP, so a peer with many addresses (IPv4 aliases, an IPv6 /64)
/// walked the 10,000 four-digit PINs in minutes.
/// <para>
/// Once the budget is gone, joins close and stay closed until the host draws a new PIN through
/// <see cref="ReplacePin"/>. Deliberately not automatic: a PIN that renews itself only spreads the
/// same guesses across rotations, each still hitting 1 in 10,000. Needing a person to act is what
/// caps the attacker's odds, at <see cref="MaxFailuresPerPin"/> in 10,000 per renewal, and the
/// closed state is the Lagebuchführer's alarm that someone is guessing.
/// </para>
/// <para>
/// Devices already admitted keep working throughout: an address is remembered together with the
/// PIN it succeeded with, and that pair is accepted for the rest of the share session, even after
/// a renewal. Only someone who already knew that PIN gets in that way, so nothing is weakened.
/// </para>
/// </summary>
internal sealed class JoinGate
{
    /// <summary>Wrong PINs, across all addresses, after which joins close.</summary>
    public const int MaxFailuresPerPin = 10;

    private readonly object _lock = new();
    private readonly HashSet<(string Ip, string Pin)> _admitted = new();
    private string _pin;
    private int _failures;

    public JoinGate(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        _pin = pin;
    }

    /// <summary>Raised once, from the request thread, when the budget for the current PIN runs out.</summary>
    public event Action? Closed;

    public string Pin
    {
        get
        {
            lock (_lock)
            {
                return _pin;
            }
        }
    }

    public bool JoinsClosed
    {
        get
        {
            lock (_lock)
            {
                return _failures >= MaxFailuresPerPin;
            }
        }
    }

    /// <summary>Wrong PINs counted against the current PIN; it stops at the budget.</summary>
    public int FailuresForCurrentPin
    {
        get
        {
            lock (_lock)
            {
                return _failures;
            }
        }
    }

    /// <summary>
    /// Whether a request from <paramref name="ip"/> carrying <paramref name="header"/> is let in.
    /// Deciding and counting happen in one step, so a burst of concurrent wrong PINs cannot get
    /// more comparisons than the budget.
    /// </summary>
    public bool Check([NotNullWhen(true)] string? ip, StringValues header)
    {
        // A peer with no address cannot be admitted or told apart from any other, so it is refused
        // outright rather than pooled into one shared bucket. Kestrel always knows the address of a
        // TCP peer; this is the in-process edge case, and it does not spend the budget.
        if (ip is null)
        {
            return false;
        }

        // Exactly one header. A missing or duplicated one is refused, as a wrong one is. The
        // comparison is ordinal: the PIN is a short numeric string carried over TLS, not a secret
        // to defend against timing analysis (the documented exception to FixedTimeEquals).
        var offered = header.Count == 1 ? header[0] : null;
        var justClosed = false;
        lock (_lock)
        {
            if (offered is not null && _admitted.Contains((ip, offered)))
            {
                return true;
            }

            var open = _failures < MaxFailuresPerPin;
            if (open && string.Equals(offered, _pin, StringComparison.Ordinal))
            {
                _admitted.Add((ip, _pin));
                return true;
            }

            if (open)
            {
                _failures++;
                justClosed = _failures == MaxFailuresPerPin;
            }
        }

        // Outside the lock: a subscriber must not be able to stall every other request.
        if (justClosed)
        {
            Closed?.Invoke();
        }

        return false;
    }

    /// <summary>
    /// Makes <paramref name="pin"/> the PIN new joins need, with a fresh budget, and reopens joins.
    /// Devices admitted under an earlier PIN keep it.
    /// </summary>
    public void ReplacePin(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        lock (_lock)
        {
            _pin = pin;
            _failures = 0;
        }
    }
}
