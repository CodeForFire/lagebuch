using LageBuch.Persistence.MasterData;

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

    /// <summary>
    /// The address(es) other devices dial while hosting, one per line; shown on demand behind the
    /// PIN rather than on the header line. Null when not hosting.
    /// </summary>
    string? ShareHint { get; }

    /// <summary>The PIN a joining device must enter while hosting; null when not hosting.</summary>
    string? SharePin { get; }

    /// <summary>
    /// Whether too many wrong PINs have closed joins (#288). Devices already joined keep working;
    /// new ones are refused, even with the right PIN, until <see cref="RenewPin"/>.
    /// </summary>
    bool JoinsClosed { get; }

    /// <summary>Raised when <see cref="JoinsClosed"/> flips, possibly on a non-UI thread.</summary>
    event EventHandler? JoinsClosedChanged;

    /// <summary>
    /// Draws a new <see cref="SharePin"/> and reopens joins. Devices already joined keep the PIN
    /// they joined with. Does nothing while not hosting.
    /// </summary>
    void RenewPin();

    /// <summary>
    /// Starts hosting. <paramref name="masterData"/> is the Stammdaten this workspace is running
    /// on: the host is the Stammdaten master (#183), so joined clients run on this set for the
    /// session instead of their own local one.
    /// </summary>
    Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>No-op controller for heads that cannot host; the toggle stays hidden (<see cref="CanHost"/> is false).</summary>
public sealed class NoopIncidentHostController : IIncidentHostController
{
    public bool CanHost => false;

    public bool IsHosting => false;

    public string? ShareHint => null;

    public string? SharePin => null;

    public bool JoinsClosed => false;

    public event EventHandler? JoinsClosedChanged
    {
        add { }
        remove { }
    }

    public void RenewPin()
    {
    }

    public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
