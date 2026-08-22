namespace LageBuch.AppLogic.Services;

/// <summary>
/// Platform hook for hosting an incident on the network. The workspace toggles it; the desktop head
/// implements it with the embedded Kestrel/SignalR host (<c>LageBuch.Sync.Hosting</c>), while heads
/// that can't host (e.g. Android for now) supply <see cref="NoopIncidentHostController"/>. Kept in
/// AppLogic so the cross-platform ViewModel depends only on this interface, never on ASP.NET Core.
/// </summary>
public interface IIncidentHostController
{
    /// <summary>Whether this platform can host at all (false hides the toggle).</summary>
    bool CanHost { get; }

    bool IsHosting { get; }

    /// <summary>A short line to show while hosting — e.g. the address other devices dial.</summary>
    string? ShareHint { get; }

    /// <summary>The PIN a joining device must enter while hosting; null when not hosting.</summary>
    string? SharePin { get; }

    Task StartAsync(LocalIncidentSession session);

    Task StopAsync();
}

/// <summary>No-op controller for heads that cannot host; the toggle stays hidden (<see cref="CanHost"/> is false).</summary>
public sealed class NoopIncidentHostController : IIncidentHostController
{
    public bool CanHost => false;
    public bool IsHosting => false;
    public string? ShareHint => null;
    public string? SharePin => null;
    public Task StartAsync(LocalIncidentSession session) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}
