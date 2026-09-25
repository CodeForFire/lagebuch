namespace LageBuch.Sync;

/// <summary>Shared protocol constants and the version-handshake payload (see §7).</summary>
public static class SyncProtocol
{
    /// <summary>Fixed port the host binds on its Tailscale address; clients dial the same.</summary>
    public const int Port = 5859;

    /// <summary>
    /// The version of the wire contract itself — the <see cref="SyncCommand"/> allowlist, the shape
    /// of <see cref="IncidentSnapshot"/>, the Stammdaten payload. Deliberately *not* the app
    /// version: a release changes the app version every time and the contract almost never, and two
    /// devices in a volunteer-run fleet are routinely on different releases (an Android update
    /// additionally waits on Play review) while speaking an identical protocol.
    /// <para>
    /// Bump it when the contract changes, and only then. A change a peer sitting at
    /// <see cref="MinimumProtocolVersion"/> can simply ignore — a new optional command field with a
    /// defaulted constructor parameter, a new snapshot property an older client drops on the floor —
    /// raises this number alone. A change such a peer *cannot* handle — a new
    /// <c>[JsonDerivedType]</c> command a client may now send, a renamed or removed field, a changed
    /// enum contract — raises <see cref="MinimumProtocolVersion"/> to match. There is no per-feature
    /// capability negotiation: choosing between those two cases is the whole mechanism.
    /// </para>
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// The oldest contract this build still speaks. A peer below it is refused with a message naming
    /// which end to update; a peer at or above it is served. See <see cref="ProtocolVersion"/> for
    /// when to raise this.
    /// </summary>
    public const int MinimumProtocolVersion = 1;

    /// <summary>
    /// What an absent or zero protocol number on the wire means: the contract as it stood at v0.6.1,
    /// before this handshake existed. Builds up to and including that release send no protocol
    /// number at all, and a host still running one must stay joinable.
    /// <para>
    /// This equals 1 because the v0.6.1 contract *is* protocol 1 — true only as long as the release
    /// introducing the handshake changes nothing else on the wire (it adds two JSON members an old
    /// reader skips and a request header an old host ignores, and nothing more). Should another wire
    /// change ever ride along, this has to become a distinct value below
    /// <see cref="MinimumProtocolVersion"/>, and that minimum has to rise with it.
    /// </para>
    /// </summary>
    public const int LegacyProtocolVersion = 1;

    public const string CommandPath = "/command";
    public const string SnapshotPath = "/snapshot";
    public const string VersionPath = "/version";
    public const string HubPath = "/hub";

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

    /// <summary>
    /// Request header carrying the client's <see cref="ProtocolVersion"/>, so the host can refuse a
    /// peer whose contract it no longer serves rather than failing later on a command it cannot
    /// parse. Absent means a pre-negotiation client (v0.6.1 or older), i.e.
    /// <see cref="LegacyProtocolVersion"/>.
    /// </summary>
    public const string ProtocolHeader = "X-Lagebuch-Protocol";
}

/// <summary>
/// Exchanged on connect (§7). <see cref="Protocol"/> and <see cref="MinProtocol"/> are the range of
/// wire contracts the sender speaks, and are what decides compatibility; <see cref="Version"/> is
/// the app version, carried so a refusal can name the build a human has to update, never gating on
/// its own.
/// <para>
/// Both numbers default to <c>0</c> rather than to <see cref="SyncProtocol.LegacyProtocolVersion"/>
/// so that a payload from a pre-negotiation host, which carries neither member, lands on
/// <c>default(int)</c>. Nothing then depends on System.Text.Json honouring a constructor
/// parameter's declared default for an absent property, nor on
/// <c>RespectRequiredConstructorParameters</c> staying off. Whoever reads them maps <c>0</c> onto
/// <see cref="SyncProtocol.LegacyProtocolVersion"/>.
/// </para>
/// </summary>
public sealed record VersionInfo(string Version, int Protocol = 0, int MinProtocol = 0);

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
/// Thrown when the two devices' wire contracts do not overlap — see
/// <see cref="SyncProtocol.ProtocolVersion"/>. Mixed *app* versions are expected across a
/// volunteer-run, un-auto-updated fleet and are no longer a reason to refuse; a mismatched
/// *protocol* is, and the message names which of the two devices has to be updated (§7).
/// </summary>
public sealed class VersionMismatchException : Exception
{
    public VersionMismatchException()
        : this("unbekannt", "unbekannt", thisDeviceIsTooOld: true)
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

    private VersionMismatchException(string localVersion, string hostVersion, bool thisDeviceIsTooOld)
        : base(thisDeviceIsTooOld
            ? $"Dieses Gerät ist zu alt für den Host (Host {hostVersion}, dieses Gerät {localVersion}). Bitte dieses Gerät aktualisieren."
            : $"Der Host ist zu alt für dieses Gerät (Host {hostVersion}, dieses Gerät {localVersion}). Bitte den Host aktualisieren.")
    {
        LocalVersion = localVersion;
        HostVersion = hostVersion;
    }

    public string LocalVersion { get; } = string.Empty;

    public string HostVersion { get; } = string.Empty;

    /// <summary>The host speaks a contract newer than anything this build understands.</summary>
    public static VersionMismatchException ThisDeviceIsTooOld(string localVersion, string hostVersion) =>
        new(localVersion, hostVersion, thisDeviceIsTooOld: true);

    /// <summary>The host's newest contract is older than the oldest this build still speaks.</summary>
    public static VersionMismatchException HostIsTooOld(string localVersion, string hostVersion) =>
        new(localVersion, hostVersion, thisDeviceIsTooOld: false);
}
