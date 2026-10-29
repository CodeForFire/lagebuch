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
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 4, int.MaxValue);
        Assert.InRange(SyncProtocol.LegacyProtocolVersion, int.MinValue, SyncProtocol.MinimumProtocolVersion - 1);
    }

    [Fact]
    public void Link_groups_raise_the_protocol_but_not_the_floor()
    {
        // #518: the Stammdaten payload carries a group on each link. A peer at 4 skips the key and
        // shows its links ungrouped, and a payload without it reads as ungrouped -- so 5 and 4 still
        // connect in either direction.
        Assert.InRange(SyncProtocol.ProtocolVersion, 5, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, int.MinValue, SyncProtocol.ProtocolVersion);
    }

    [Fact]
    public void The_dispatch_centre_name_raises_the_protocol_but_not_the_floor()
    {
        // 6 adds settings.dispatchCentreName to the Stammdaten payload (#400). A peer at 4 or 5 skips
        // the key and falls back to "ILS", so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 6, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, int.MinValue, SyncProtocol.ProtocolVersion);
    }

    [Fact]
    public void Editing_an_Aufgabe_raises_the_floor_to_protocol_seven()
    {
        // 7 lets a client send updateTask and extendTaskTimer (#246). A host at 6 has neither in its
        // command allowlist and would reject the edit, so a 6 and a 7 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 7, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 7, int.MaxValue);
    }

    [Fact]
    public void Hausnummer_plz_and_ort_raise_the_floor_to_protocol_eight()
    {
        // 8 carries Hausnummer, PLZ and Ort on setAddress (#401). A client at 7 would send setAddress
        // without them, and a host at 8 reads the missing parts as cleared -- so a 7 and an 8 must
        // not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 8, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 8, int.MaxValue);
    }

    [Fact]
    public void Stichwoerter_raise_the_protocol_but_not_the_floor()
    {
        // 9 adds keywords to the Stammdaten payload (#704). A peer at 8 skips the key and offers no
        // Stichwort suggestions, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 9, int.MaxValue);
    }

    [Fact]
    public void Beteiligte_in_a_wohnung_raise_the_floor_to_protocol_ten()
    {
        // 10 lets a client send setInvolvedPartyDwelling (#702). A host at 9 has no such command in
        // its allowlist and would reject the link, so a 9 and a 10 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 10, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 10, int.MaxValue);
    }

    [Fact]
    public void Strassen_and_Ortsteile_raise_the_protocol_but_not_the_floor()
    {
        // 11 adds districts and streets to the Stammdaten payload (#461). A peer at 10 skips both
        // keys and offers no suggestions, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 11, int.MaxValue);
    }

    [Fact]
    public void The_beteiligte_aufenthaltsort_raises_the_floor_to_protocol_twelve()
    {
        // 12 lets a client send setInvolvedPartyWhereabouts (#701). A host at 11 has no such command
        // in its allowlist and would reject the Aufenthaltsort, so an 11 and a 12 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 12, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 12, int.MaxValue);
    }

    [Fact]
    public void Linking_an_aufgabe_to_its_etb_entry_raises_the_protocol_to_thirteen_but_not_the_floor()
    {
        // 13 adds entryId to addJournalEntry and sourceEntryId to addTask and the snapshot's tasks.
        // A peer at 12 ignores both and the Aufgabe is simply unlinked, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 13, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 12, int.MaxValue);
    }

    [Fact]
    public void Textbausteine_raise_the_protocol_to_fourteen_but_not_the_floor()
    {
        // 14 adds textSnippets to the Stammdaten payload. A peer at 12 or 13 skips the key and
        // simply offers no :: suggestions, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 14, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 12, int.MaxValue);
    }

    [Fact]
    public void A_vehicle_phone_number_raises_the_protocol_to_fifteen_but_not_the_floor()
    {
        // 15 adds phone to the Stammdaten payload's vehicles. A peer at 12 to 14 skips the key and
        // lists no Fahrzeugtelefon, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 15, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 12, int.MaxValue);
    }

    [Fact]
    public void Sending_a_Lagemeldung_raises_the_floor_to_protocol_sixteen()
    {
        // 16 lets a client send the four Lagemeldung commands (#592). A host at 15 has none of them in
        // its command allowlist and would reject the report, so a 15 and a 16 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 16, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 16, int.MaxValue);
    }

    [Fact]
    public void Objekte_raise_the_protocol_to_seventeen_but_not_the_floor()
    {
        // 17 adds premises to the Stammdaten payload and apartmentLabels to addCoBuilding. A peer at
        // 16 skips the key and offers no Objekte, and a host there creates the Haus without its
        // labels, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 17, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 16, int.MaxValue);
    }

    [Fact]
    public void The_host_time_on_revision_raises_the_protocol_to_eighteen_but_not_the_floor()
    {
        // 18 adds hostTime to /revision. A client at 16 or 17 ignores it and stays on its own clock,
        // and a host there omits it, which a newer client reads as unknown, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 18, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 16, int.MaxValue);
    }

    [Fact]
    public void An_ort_on_the_druckabfrage_raises_the_protocol_to_nineteen_but_not_the_floor()
    {
        // 19 adds location to recordScbaPressure and to the snapshot's readings. A peer at 16 to 18
        // ignores the key and records the bar without an Ort, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 19, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 16, int.MaxValue);
    }

    [Fact]
    public void Handing_out_a_Geraet_raises_the_floor_to_protocol_twenty()
    {
        // 20 lets a client send the five Ausgabeliste commands. A host at 19 has none of them in its
        // command allowlist and would reject the Ausgabe, so a 19 and a 20 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 20, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 20, int.MaxValue);
    }

    [Fact]
    public void A_vehicle_email_address_raises_the_protocol_to_twenty_one_but_not_the_floor()
    {
        // 21 adds email to the Stammdaten payload's vehicles. A peer at 20 skips the key and offers
        // no mail to the vehicle, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 21, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 20, int.MaxValue);
    }

    [Fact]
    public void Placement_commands_raise_the_floor_to_protocol_twenty_two()
    {
        // 22 lets a client send addCommandNode, updateCommandNode, dissolveCommandNode,
        // moveForceUnit, setFuehrungsstufe and updateLeadershipPost. A host at 21 has none of them
        // in its allowlist and would reject them, so a 21 and a 22 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 22, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 22, int.MaxValue);
    }

    [Fact]
    public void Moving_an_abschnitt_raises_the_floor_to_protocol_twenty_three()
    {
        // moveCommandNode is a command a client may send and a host forwards. A peer at 22 has no
        // such entry in its allowlist and would reject it, so a 22 and a 23 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 23, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 23, int.MaxValue);
    }

    [Fact]
    public void A_schluesselnummer_on_the_wohnung_raises_the_protocol_and_the_floor_to_twenty_four()
    {
        // 24 adds keyNumber to updateDwellingDetails and to the snapshot's Wohnungen. The command
        // carries the whole detail state, so a peer at 23 would send it without the number and a
        // host at 24 would read that as "cleared". It cannot stay joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 24, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 24, int.MaxValue);
    }

    [Fact]
    public void An_einsatznummer_format_raises_the_protocol_to_twenty_five_but_not_the_floor()
    {
        // 25 adds incidentNumberFormat to the Stammdaten payload's settings (#462). A peer at 24
        // skips the key and pre-fills no Einsatznummer, so it stays joinable.
        Assert.InRange(SyncProtocol.ProtocolVersion, 25, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 24, int.MaxValue);
    }

    [Fact]
    public void The_Lagekarte_raises_the_protocol_and_the_floor_to_twenty_six()
    {
        // 26 adds setIncidentLocation and placeForceUnit, commands a client may send. A host at 25
        // has neither in its allowlist and would reject them, so a 25 and a 26 must not connect.
        Assert.InRange(SyncProtocol.ProtocolVersion, 26, int.MaxValue);
        Assert.InRange(SyncProtocol.MinimumProtocolVersion, 26, int.MaxValue);
    }

    [Fact]
    public void Correcting_a_funktion_raises_the_protocol_and_the_floor_to_twenty_seven()
    {
        // 27 adds editRoleAssignment, a command a client may send. A host at 26 has it nowhere in
        // its allowlist and would reject it, so a 26 and a 27 must not connect.
        Assert.Equal(27, SyncProtocol.ProtocolVersion);
        Assert.Equal(27, SyncProtocol.MinimumProtocolVersion);
    }

    [Fact]
    public void The_per_Einsatz_rail_raises_the_protocol_and_the_floor_to_twenty_seven()
    {
        // 27 adds setNavVisibility and addChecklist, commands a client may send. A host at 26 has
        // neither in its allowlist and would reject them, so a 26 and a 27 must not connect.
        Assert.Equal(27, SyncProtocol.ProtocolVersion);
        Assert.Equal(27, SyncProtocol.MinimumProtocolVersion);
    }
}
