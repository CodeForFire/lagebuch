using Microsoft.AspNetCore.SignalR;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// The push channel. Clients connect and receive <see cref="SyncProtocol.SnapshotMethod"/> messages
/// carrying the full <see cref="IncidentSnapshot"/> after every applied command. A joining client
/// fetches its initial state from <c>GET /snapshot</c> and then just listens here, so the hub itself
/// carries no server-callable methods.
/// </summary>
public sealed class IncidentHub : Hub
{
}
