namespace Feuerwehr.Sync;

/// <summary>Shared protocol constants and the version-handshake payload (see §7).</summary>
public static class SyncProtocol
{
    /// <summary>Fixed port the host binds on its Tailscale address; clients dial the same.</summary>
    public const int Port = 5859;

    public const string CommandPath = "/command";
    public const string SnapshotPath = "/snapshot";
    public const string VersionPath = "/version";
    public const string HubPath = "/hub";

    /// <summary>SignalR method the host pushes the full snapshot on, after every applied command.</summary>
    public const string SnapshotMethod = "snapshot";
}

/// <summary>Exchanged on connect; a client refuses a host whose <see cref="Version"/> differs (§7).</summary>
public sealed record VersionInfo(string Version);
