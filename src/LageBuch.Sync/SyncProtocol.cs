namespace LageBuch.Sync;

/// <summary>Shared protocol constants and the version-handshake payload (see §7).</summary>
public static class SyncProtocol
{
    /// <summary>Fixed port the host binds on every interface; clients dial the same.</summary>
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
    /// <para>
    /// 2: the Stammdaten payload carries <c>isOwn</c> on vehicles and personnel (#458). A peer at 1
    /// skips the key, and a payload without it reads as all own, so the floor stays at 1.
    /// </para>
    /// <para>
    /// 3: the Beteiligte — <c>addInvolvedParty</c>, <c>updateInvolvedParty</c> and
    /// <c>removeInvolvedParty</c> commands and the snapshot's <c>involvedParties</c>. A host at 2
    /// would reject those commands, so the floor rises to 3 as well.
    /// </para>
    /// <para>
    /// 4: the snapshot carries <c>epoch</c> and <c>revision</c>, and the host serves them at
    /// <see cref="RevisionPath"/> (#295). A client at 4 discards any snapshot that does not supersede
    /// the one it holds, and a host at 3 sends revision 0 on every one — so such a client would drop
    /// every update after the first and sit on the Lage it joined with. The floor rises to 4.
    /// </para>
    /// <para>
    /// 5: the Stammdaten payload carries <c>group</c> on links (#518). A peer at 4 skips the key and
    /// shows the links ungrouped, and a payload without it reads as ungrouped, so the floor stays
    /// at 4.
    /// </para>
    /// <para>
    /// 6: the Stammdaten payload's <c>settings</c> carry <c>dispatchCentreName</c> (#400). A peer at
    /// 4 or 5 skips the key, and a payload without it reads as "ILS", so the floor stays at 4.
    /// </para>
    /// <para>
    /// 7: a client may send <c>updateTask</c> and <c>extendTaskTimer</c> (#246). A host at 6 would
    /// reject both commands, so the floor rises to 7.
    /// </para>
    /// <para>
    /// 8: the address carries Hausnummer, PLZ and Ort (#401), on <c>setAddress</c> and in the
    /// snapshot. A peer at 7 would not just ignore them: a client at 7 sends <c>setAddress</c>
    /// without the new fields, which a host at 8 reads as "cleared", wiping what another device
    /// entered. The floor rises to 8.
    /// </para>
    /// <para>
    /// 9: the Stammdaten payload carries <c>keywords</c>, the Stichwörter (#704). A peer at 8 skips
    /// the key and offers no suggestions, and a payload without it reads as none, so the floor stays
    /// at 8.
    /// </para>
    /// <para>
    /// 10: a client may send <c>setInvolvedPartyDwelling</c>, and a snapshot's Beteiligte carry
    /// <c>dwellingId</c> and <c>dwellingLabel</c> (#702). A host at 9 would reject the command, so
    /// the floor rises to 10.
    /// </para>
    /// <para>
    /// 11: the Stammdaten payload carries <c>districts</c> and <c>streets</c>, the Ortsteile and
    /// Straßen (#461). A peer at 10 skips both keys and offers no suggestions, and a payload without
    /// them reads as none, so the floor stays at 10.
    /// </para>
    /// <para>
    /// 12: a client may send <c>setInvolvedPartyWhereabouts</c>, and a snapshot's Beteiligte carry
    /// <c>whereabouts</c> and <c>whereaboutsAt</c>, the Aufenthaltsort (#701). A host at 11 would
    /// reject the command, so the floor rises to 12.
    /// </para>
    /// <para>
    /// 13: <c>addJournalEntry</c> may carry the <c>entryId</c> the client minted, <c>addTask</c> a
    /// <c>sourceEntryId</c>, and the snapshot's tasks the same. A host at 12 ignores both fields --
    /// the Aufgabe is simply not linked -- and a client at 12 drops the snapshot's, so the floor
    /// stays at 12.
    /// </para>
    /// <para>
    /// 14: the Stammdaten payload carries the Textbausteine, <c>textSnippets</c>, each marked with
    /// the areas it is offered in (<c>etb</c>, <c>tasks</c>). A peer at 12 or 13 skips the key and
    /// offers no <c>::</c> suggestions, so the floor stays at 12.
    /// </para>
    /// <para>
    /// 15: the Stammdaten payload carries <c>phone</c> on vehicles. A peer at 12 to 14 skips the key
    /// and lists no Fahrzeugtelefon in its Kontakte, and a payload without it reads as no number,
    /// so the floor stays at 12.
    /// </para>
    /// <para>
    /// 16: the Lagemeldungen (#592) — <c>addSituationReport</c>, <c>updateSituationReport</c>,
    /// <c>removeSituationReport</c> and <c>sendSituationReport</c> commands and the snapshot's
    /// <c>situationReports</c>. A host at 15 would reject those commands, so the floor rises to 16.
    /// </para>
    /// <para>
    /// 17: the Stammdaten payload carries the Objekte, <c>premises</c>, and <c>addCoBuilding</c> an
    /// optional <c>apartmentLabels</c> (#420). A peer at 16 skips the key and offers no Objekte, and
    /// a host there ignores the labels and creates the Haus without them, so the floor stays at 16.
    /// </para>
    /// <para>
    /// 18: <c>/revision</c> carries <c>hostTime</c>, the host's clock, so a joined client runs its
    /// countdowns on host time and warns when its own clock is off. A client at 16 or 17 ignores the
    /// field and stays on its own clock, and a host there omits it, so the floor stays at 16.
    /// </para>
    /// <para>
    /// 19: <c>recordScbaPressure</c> may carry the <c>location</c> of the Druckabfrage, and the
    /// snapshot's readings the same. A host at 16 to 18 ignores it -- the reading is recorded
    /// without an Ort -- and a client there drops the snapshot's, so the floor stays at 16.
    /// </para>
    /// <para>
    /// 20: the Ausgabeliste — <c>issueItem</c>, <c>updateIssuedItem</c>, <c>returnIssuedItem</c>,
    /// <c>undoIssuedItemReturn</c> and <c>removeIssuedItem</c> commands, the snapshot's
    /// <c>issuedItems</c>, and the Stammdaten payload's <c>devices</c> with their counts. A host
    /// at 19 would reject those commands, so the floor rises to 20.
    /// </para>
    /// <para>
    /// 21: the Stammdaten payload carries <c>email</c> on vehicles. A peer at 20 skips the key and
    /// offers no mail to the vehicle in its Kontakte, and a payload without it reads as no address,
    /// so the floor stays at 20.
    /// </para>
    /// <para>
    /// 22: the Führungsorganisation. A client may send <c>addCommandNode</c>,
    /// <c>updateCommandNode</c>, <c>dissolveCommandNode</c>, <c>moveForceUnit</c>,
    /// <c>setFuehrungsstufe</c> and <c>updateLeadershipPost</c>, and <c>assignRole</c> carries
    /// <c>nodeId</c> and <c>slot</c>. A snapshot carries <c>commandNodes</c>, <c>fuehrungsstufe</c>
    /// and the Einsatzleitung's post, its units and Funktionen carry <c>nodeId</c>, and the
    /// Stammdaten payload gains <c>talkGroups</c> and a role <c>slot</c>. A host at 21 would reject
    /// the six commands, so the floor rises to 22. <c>addCommandNode</c> also carries the optional
    /// <c>auftrag</c>, <c>standort</c>, <c>funkgruppe</c>, <c>rufname</c> and <c>telefon</c>, so an
    /// Abschnitt is created with its details in one command. <c>placeRoleAssignment</c> moves an
    /// existing Funktion into an Abschnitt and a board slot, as <c>moveForceUnit</c> does for a unit.
    /// </para>
    /// <para>
    /// 23 adds <c>moveCommandNode</c>, which reorders a node or moves it between the two levels. A
    /// peer at 22 would reject it, so the floor rises with it.
    /// </para>
    /// <para>
    /// 24: <c>updateDwellingDetails</c> carries the <c>keyNumber</c> of a handed-over key, and the
    /// snapshot's Wohnungen the same. The command carries the Wohnung's whole detail state, so a
    /// client at 23 editing the Bewohner would send it without the number, which a host at 24 reads
    /// as "cleared" -- wiping what another device entered, the <c>setAddress</c> case of 8. The
    /// floor rises to 24.
    /// </para>
    /// <para>
    /// 25: the Stammdaten payload's <c>settings</c> carry <c>incidentNumberFormat</c> (#462). A peer
    /// at 24 skips the key and pre-fills no Einsatznummer, and a payload without it reads as no
    /// format, so the floor stays at 24.
    /// </para>
    /// <para>
    /// 26: the Lagekarte. A client may send <c>setIncidentLocation</c> and <c>placeForceUnit</c>,
    /// which a host at 25 does not know, so the floor rises to 26. The snapshot carries the
    /// Einsatzort and each unit's position as optional numbers.
    /// </para>
    /// <para>
    /// 27: a client may send <c>editRoleAssignment</c>, which corrects a Funktion's person without a
    /// handover. A host at 26 does not know it, so the floor rises to 27.
    /// </para>
    /// <para>
    /// 28: per-Einsatz rail. A client may send <c>setNavVisibility</c> and <c>addChecklist</c>,
    /// which a host at 27 does not know, so the floor rises to 28. The snapshot carries
    /// <c>navOverrides</c>.
    /// </para>
    /// <para>
    /// 29: an Einsatzleitung's TMO and DMO Funkgruppen (#828). <c>updateLeadershipPost</c> carries
    /// <c>funkgruppenTmo</c> and <c>funkgruppenDmo</c> in place of <c>funkgruppe</c>, and
    /// <c>addCommandNode</c> and <c>updateCommandNode</c> carry them for a partner; the snapshot's
    /// post and nodes the same. Each command carries the whole post or node, so a client at 28 editing
    /// one would send no lists, which a host at 29 reads as "none" -- wiping what another device
    /// entered, the <c>setAddress</c> case of 8. The floor rises to 29.
    /// </para>
    /// </summary>
    public const int ProtocolVersion = 29;

    /// <summary>
    /// The oldest contract this build still speaks. A peer below it is refused with a message naming
    /// which end to update; a peer at or above it is served. See <see cref="ProtocolVersion"/> for
    /// when to raise this.
    /// </summary>
    public const int MinimumProtocolVersion = 29;

    /// <summary>
    /// What an absent or zero protocol number on the wire means: the contract as it stood at v0.6.1,
    /// before this handshake existed. Builds up to and including that release send no protocol
    /// number at all. Such a peer stayed joinable until protocol 3 raised
    /// <see cref="MinimumProtocolVersion"/> above this value; it is now refused like any other
    /// peer below the floor.
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

    /// <summary>
    /// The host's current snapshot position — epoch and revision — and nothing else. A joined client
    /// polls this as an anti-entropy net (#295): a broadcast that never arrived leaves the host's
    /// position different from the client's, and the next poll re-fetches <see cref="SnapshotPath"/>
    /// to catch up. Deliberately tiny — it is requested every few seconds per client, unlike the
    /// whole-incident snapshot.
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

    /// <summary>
    /// Request header carrying the client's <see cref="ProtocolVersion"/>, so the host can refuse a
    /// peer whose contract it no longer serves rather than failing later on a command it cannot
    /// parse. Absent means a pre-negotiation client (v0.6.1 or older), i.e.
    /// <see cref="LegacyProtocolVersion"/>.
    /// </summary>
    public const string ProtocolHeader = "X-Lagebuch-Protocol";

    /// <summary>
    /// The most a joined client buffers from any one response of the host's: a snapshot, a command's
    /// answer, an attachment. The host is outside the client's trust boundary, so a response that
    /// would not fit is refused rather than read. Sized to hold the largest attachment
    /// (<see cref="LageBuch.Domain.Files.IncidentFile.MaxSizeBytes"/>) with room to spare; a snapshot
    /// is text and runs to megabytes at the very most.
    /// </summary>
    public const long MaxResponseBytes = LageBuch.Domain.Files.IncidentFile.MaxSizeBytes + (8 * 1024 * 1024);

    /// <summary>
    /// How often a joined client polls <see cref="RevisionPath"/>. This is the ceiling on how long a
    /// device can sit on stale state after a lost broadcast, so it is chosen to be shorter than anyone
    /// would spend reading a screen before acting on it — and the request is a few bytes over a LAN.
    /// </summary>
    public static readonly TimeSpan DefaultReconcileInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long one reconcile pass may take before it counts as failed. Without it a pass on a link
    /// that went silent without closing — a roaming WLAN, an access point asleep — would wait out the
    /// HTTP client's 100 s default, and the footer would keep claiming a confirmed Stand for all of it.
    /// With it, a device learns it cannot confirm its Stand within one interval plus this.
    /// </summary>
    public static readonly TimeSpan ReconcileTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a joined client waits for the host to answer one command. On a link that went silent
    /// the command would otherwise wait out the HTTP client's 100 s default; with it, the
    /// Lagebuchführer learns within this much that the input may not have arrived. Generous, because
    /// the answer carries the whole snapshot and a weak WLAN is slow, not dead.
    /// </summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The longest <see cref="RevisionPath"/> round trip whose <see cref="RevisionInfo.HostTime"/> still
    /// counts. The host read its clock somewhere inside the round trip, so half of it is the error of
    /// the estimate; a slower answer is dropped rather than averaged in, and the last good one stays.
    /// </summary>
    public static readonly TimeSpan MaxClockSampleRoundTrip = TimeSpan.FromSeconds(1);
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
/// The host's current <see cref="IncidentSnapshot.Epoch"/> and <see cref="IncidentSnapshot.Revision"/>,
/// served by <see cref="SyncProtocol.RevisionPath"/>. A joined client compares the pair against the
/// one it last applied; any difference means re-fetch the snapshot (#295).
/// <para>
/// <see cref="HostTime"/> is the host's clock when it answered. Null from a host older than protocol
/// 18, which leaves the client on its own clock.
/// </para>
/// </summary>
public sealed record RevisionInfo(long Revision, Guid Epoch = default, DateTimeOffset? HostTime = null);

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
/// Thrown when the host refused a command with 400 — a domain guard, a closed incident, or an id it
/// does not know. <see cref="Exception.Message"/> is the host's own German reason, verbatim, because
/// that sentence is what the operator needs to read; the alternative they used to get was nothing at
/// all (#295).
/// </summary>
public sealed class CommandRejectedException : Exception
{
    public CommandRejectedException()
        : this("Die Änderung wurde vom Host abgelehnt.")
    {
    }

    public CommandRejectedException(string message)
        : base(message)
    {
    }

    public CommandRejectedException(string message, Exception innerException)
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
