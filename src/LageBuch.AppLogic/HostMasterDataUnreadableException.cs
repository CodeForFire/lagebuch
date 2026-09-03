namespace LageBuch.AppLogic;

/// <summary>
/// Thrown when a joining client cannot make sense of the host's Stammdaten payload (#183): the JSON
/// is truncated, or well-formed but shaped unlike a <c>MasterDataSet</c> — a non-object root, a
/// required field missing, a value of the wrong kind. The version handshake guarantees both ends run
/// the same app build, so this always means corruption in transit or something past the
/// Trust-on-First-Use pin, never an ordinary version skew. Deliberately declared here rather than in
/// <c>LageBuch.Sync</c>: it is thrown and caught entirely within <c>HomeViewModel.JoinDeviceAsync</c>,
/// and <c>LageBuch.Sync</c> cannot reference <c>LageBuch.Persistence</c> (where the parse that raises
/// it lives).
/// </summary>
public sealed class HostMasterDataUnreadableException : Exception
{
    public HostMasterDataUnreadableException()
        : base("Stammdaten des Hosts konnten nicht gelesen werden.")
    {
    }

    public HostMasterDataUnreadableException(string message)
        : base(message)
    {
    }

    public HostMasterDataUnreadableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
