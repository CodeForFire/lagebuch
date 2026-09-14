namespace LageBuch.AppLogic.Services;

/// <summary>
/// Remembers the host address last used to join a hosted incident, so the join dialog can prefill
/// it next time instead of starting empty -- in practice the host (a station's ELW, a fixed
/// Tailscale node) is set up once and then reused for an entire Einsatz series.
/// </summary>
public interface ILastJoinHostStore
{
    string? GetLastHost();

    void SetLastHost(string host);
}
