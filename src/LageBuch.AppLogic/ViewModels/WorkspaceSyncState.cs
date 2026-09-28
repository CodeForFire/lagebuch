namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// What the workspace footer's status dot is entitled to claim. The distinction exists because the two
/// kinds of device can honestly say different things: one saves, the other only mirrors.
/// </summary>
public enum WorkspaceSyncState
{
    /// <summary>
    /// This device owns the Einsatzdatei and writes it, so the footer reports when it last saved.
    /// </summary>
    Local,

    /// <summary>
    /// A joined client whose currency has been positively confirmed — a snapshot applied, or a reconcile
    /// pass that found nothing to fetch. It saves nothing locally, so the footer reports the host's
    /// Stand instead of a save that never happened.
    /// </summary>
    Current,

    /// <summary>
    /// A joined client that cannot confirm it is current: the connection dropped, or a reconcile pass
    /// could not reach the host. The last Stand is still shown, but marked as unconfirmed — silently
    /// showing a stale Lage as though it were live is the failure this whole mechanism exists to
    /// prevent (#295).
    /// </summary>
    Unconfirmed,
}
