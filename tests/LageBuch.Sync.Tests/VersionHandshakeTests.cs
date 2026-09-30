namespace LageBuch.Sync.Tests;

/// <summary>
/// The shape of the <c>/version</c> payload, pinned directly rather than through a live host. What
/// matters here is what a build reads when the *other* build is older or newer than it — the one
/// case that cannot be reproduced by running two copies of this commit against each other.
/// </summary>
public class VersionHandshakeTests
{
    [Fact]
    public void A_version_payload_without_protocol_fields_reads_as_the_legacy_sentinel()
    {
        // Exactly what a v0.6.1 host answers: the members did not exist in that build. They must land
        // on 0 — default(int) — so that mapping them to SyncProtocol.LegacyProtocolVersion is the
        // reader's decision and depends on nothing System.Text.Json does with declared defaults.
        var info = SyncJson.Deserialize<VersionInfo>("""{"version":"0.6.1.0"}""");

        Assert.Equal("0.6.1.0", info.Version);
        Assert.Equal(0, info.Protocol);
        Assert.Equal(0, info.MinProtocol);
    }

    [Fact]
    public void A_version_payload_carries_the_protocol_range_under_camelCase_names()
    {
        var json = SyncJson.Serialize(new VersionInfo("0.7.0.0", 3, 2));

        Assert.Contains("\"protocol\":3", json, StringComparison.Ordinal);
        Assert.Contains("\"minProtocol\":2", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_payload_from_a_newer_build_ignores_members_this_build_does_not_know()
    {
        // The mirror image of the legacy case, and the reason a protocol bump does not have to be a
        // breaking change: an unknown member is skipped, not a deserialization failure.
        var info = SyncJson.Deserialize<VersionInfo>(
            """{"version":"9.9.9.0","protocol":4,"minProtocol":2,"somethingAddedLater":true}""");

        Assert.Equal(4, info.Protocol);
        Assert.Equal(2, info.MinProtocol);
    }

    [Fact]
    public void The_snapshot_position_raises_the_floor_to_protocol_four()
    {
        // Protocol 3 raised the floor for the Beteiligte commands; 4 raises it again for the epoch and
        // revision (#295). A 4 client discards snapshots that do not supersede the one it holds, and a
        // 3 host stamps revision 0 on every one, so that pair must not connect. A pre-handshake peer
        // (LegacyProtocolVersion) stays below the floor.
        Assert.Equal(4, SyncProtocol.MinimumProtocolVersion);
        Assert.InRange(SyncProtocol.LegacyProtocolVersion, int.MinValue, SyncProtocol.MinimumProtocolVersion - 1);
    }

    [Fact]
    public void Link_groups_raise_the_protocol_but_not_the_floor()
    {
        // #518: the Stammdaten payload carries a group on each link. A peer at 4 skips the key and
        // shows its links ungrouped, and a payload without it reads as ungrouped -- so 5 and 4 still
        // connect in either direction.
        Assert.Equal(5, SyncProtocol.ProtocolVersion);
        Assert.Equal(4, SyncProtocol.MinimumProtocolVersion);
    }
}
