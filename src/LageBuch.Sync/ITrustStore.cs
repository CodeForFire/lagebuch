namespace LageBuch.Sync;

/// <summary>
/// Persists the trusted TLS thumbprint a joined client pins to a host address (Trust-on-First-Use,
/// § P0 #2). Keyed by the dialed host address so each peer is tracked independently.
/// </summary>
public interface ITrustStore
{
    string? GetThumbprint(string hostAddress);

    void SaveThumbprint(string hostAddress, string thumbprint);

    /// <summary>
    /// Forgets the trusted thumbprint for an address, so the next connect re-pins whatever
    /// certificate the host presents (§181: the user's way out of a stale/duplicate-host TOFU banner).
    /// A no-op when nothing is stored for it.
    /// </summary>
    void RemoveThumbprint(string hostAddress);
}
