namespace LageBuch.AppLogic.Services;

/// <summary>
/// Remembers the last successful join (#464), so the join dialog can prefill it and the Home screen
/// can say where this device was last connected. In practice the host (a station's ELW, a fixed
/// Tailscale node) is set up once and then reused for an entire Einsatz series, and a Lagebuchführer
/// who has to reconnect mid-Einsatz should not have to ask for the PIN again. A PIN goes stale when
/// the host starts sharing anew; that costs one rejected attempt, after which the dialog asks for it.
/// </summary>
public interface ILastConnectionStore
{
    LastConnection? GetLast();

    void SetLast(LastConnection connection);

    /// <summary>Forgets the last connection; a no-op when nothing is stored.</summary>
    void Clear();
}

/// <summary>
/// The last successful join, persisted by <see cref="ILastConnectionStore"/>. <see cref="Host"/> is
/// the address as typed into the join dialog, possibly with a <c>:port</c>. <see cref="Pin"/> comes
/// last with a default so a file written before it was kept still reads, without one.
/// </summary>
public readonly record struct LastConnection(string Host, string? Keyword, DateTimeOffset ConnectedAt, string? Pin = null);
