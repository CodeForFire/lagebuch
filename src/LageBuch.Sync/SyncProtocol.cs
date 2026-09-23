namespace LageBuch.Sync;

/// <summary>Shared protocol constants and the version-handshake payload (see §7).</summary>
public static class SyncProtocol
{
    /// <summary>Fixed port the host binds on its Tailscale address; clients dial the same.</summary>
    public const int Port = 5859;

    public const string CommandPath = "/command";
    public const string SnapshotPath = "/snapshot";
    public const string VersionPath = "/version";
    public const string HubPath = "/hub";

    /// <summary>
    /// The host's current snapshot revision, and nothing else. A joined client polls this as an
    /// anti-entropy net (#295): a broadcast that never arrived leaves the host's revision ahead of
    /// the client's, and the next poll re-fetches <see cref="SnapshotPath"/> to catch up. Deliberately
    /// tiny — it is requested every few seconds per client, unlike the whole-incident snapshot.
    /// </summary>
    public const string RevisionPath = "/revision";

    /// <summary>Route template for the on-demand attachment-bytes pull, keyed by <see cref="LageBuch.Domain.Files.IncidentFile.Id"/>.</summary>
    public const string FilesRouteTemplate = "/files/{id:guid}";

    public static string FilesPath(Guid id) => $"/files/{id}";

    /// <summary>
    /// The host's Stammdaten, served in the <c>MasterDataJson</c> interchange format (#183). The
    /// host is the master: a joined client runs on this set for the session instead of its own
    /// local one, so both devices offer the same Wachen/Funkrufnamen/Personen and — the part that
    /// matters operationally — the same Einsatzzeiten and Rückzugsdruck.
    /// </summary>
    public const string MasterDataPath = "/masterdata";

    /// <summary>SignalR method the host pushes the full snapshot on, after every applied command.</summary>
    public const string SnapshotMethod = "snapshot";

    /// <summary>
    /// Request header carrying the share PIN. Sent on every client request — the version/snapshot/command
    /// HTTP calls and the SignalR hub connection alike — so a single host middleware gates them all.
    /// </summary>
    public const string PinHeader = "X-Lagebuch-Pin";
}

/// <summary>Exchanged on connect; a client refuses a host whose <see cref="Version"/> differs (§7).</summary>
public sealed record VersionInfo(string Version);

/// <summary>
/// The host's current <see cref="IncidentSnapshot.Revision"/>, served by
/// <see cref="SyncProtocol.RevisionPath"/>. A joined client compares it against the revision it last
/// applied; any difference means re-fetch the snapshot (#295).
/// </summary>
public sealed record RevisionInfo(long Revision);

/// <summary>
/// Thrown when the host rejects the join because the supplied share PIN is wrong or missing (§ #64).
/// Surfaced to the joining user on the same banner as <see cref="VersionMismatchException"/>.
/// </summary>
public sealed class PinRejectedException : Exception
{
    public PinRejectedException()
        : this("Falsche PIN.")
    {
    }

    public PinRejectedException(string message)
        : base(message)
    {
    }

    public PinRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a joining client's app version differs from the host's. Mixed versions across a
/// volunteer-run, un-auto-updated fleet are a realistic scenario to guard against explicitly (§7).
/// </summary>
public sealed class VersionMismatchException : Exception
{
    public VersionMismatchException(string localVersion, string hostVersion)
        : base($"Version stimmt nicht überein: dieses Gerät {localVersion}, Host {hostVersion}.")
    {
        LocalVersion = localVersion;
        HostVersion = hostVersion;
    }

    public VersionMismatchException()
        : this("unbekannt", "unbekannt")
    {
    }

    public VersionMismatchException(string message)
        : base(message)
    {
    }

    public VersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string LocalVersion { get; } = string.Empty;

    public string HostVersion { get; } = string.Empty;
}
