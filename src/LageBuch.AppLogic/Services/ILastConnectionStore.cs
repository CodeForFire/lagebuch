namespace LageBuch.AppLogic.Services;

/// <summary>
/// Remembers the last successful join (#464): the host address, so the join dialog can prefill it
/// -- in practice the host (a station's ELW, a fixed Tailscale node) is set up once and then reused
/// for an entire Einsatz series -- and the Stichwort and time, so the Home screen can say where this
/// device was last connected. The PIN is deliberately not kept: the host makes a new one every time
/// sharing starts.
/// </summary>
public interface ILastConnectionStore
{
    LastConnection? GetLast();

    void SetLast(LastConnection connection);
}

/// <summary>
/// The last successful join, persisted by <see cref="ILastConnectionStore"/>. <see cref="Host"/> is
/// the address as typed into the join dialog, possibly with a <c>:port</c>.
/// </summary>
public readonly record struct LastConnection(string Host, string? Keyword, DateTimeOffset ConnectedAt);
