namespace LageBuch.Sync;

/// <summary>
/// Persists the trusted TLS thumbprint a joined client pins to a host address (Trust-on-First-Use,
/// § P0 #2). Keyed by the dialed host address so each peer is tracked independently.
/// </summary>
public interface ITrustStore
{
    string? GetThumbprint(string hostAddress);
    void SaveThumbprint(string hostAddress, string thumbprint);
}
