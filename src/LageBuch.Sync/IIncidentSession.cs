using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Sync;

/// <summary>
/// The mutation surface the ViewModels drive, with two implementations behind it: a local session
/// (apply to the in-memory <see cref="Incident"/>, save, raise <see cref="Changed"/>) and a remote
/// session (POST the command to the host, write nothing locally — the UI updates only when the
/// host's broadcast lands). Mutations are <b>fire-and-forget</b>: the ViewModels never use a return
/// value; they react to <see cref="Changed"/> and re-read <see cref="Incident"/>, so the exact same
/// code path serves an edit made on this device or on another one.
///
/// The session owns the clock and operator, so these signatures drop the <c>IClock</c>/
/// <see cref="SessionOperator"/> arguments the domain methods take.
/// </summary>
public interface IIncidentSession
{
    /// <summary>The current incident state — a live aggregate locally, or the latest host snapshot remotely.</summary>
    Incident Incident { get; }

    SessionOperator? Operator { get; }

    bool IsReadOnly { get; }

    /// <summary>
    /// True on a joined client. Autonomous, time-driven logging (SCBA Rückzugsalarm, ILS reminder
    /// acknowledgements) must run only on the authoritative device, so the host isn't double-logged
    /// — ViewModels gate those writes on <c>!IsRemote</c>.
    /// </summary>
    bool IsRemote { get; }

    /// <summary>
    /// How far the authoritative device's clock is ahead of this one's. Zero on the device that owns
    /// the incident; on a joined client, what the last reconcile pass measured.
    /// </summary>
    TimeSpan HostClockOffset { get; }

    /// <summary>Raised after the incident state changes (a local mutation, or a host broadcast).</summary>
    [SuppressMessage("Design", "CA1003", Justification = "In-process fire-and-forget event with C#-only subscribers; Action matches the pervasive Action event convention (GoHomeRequested etc.).")]
    event Action? Changed;

    /// <summary>
    /// Hands the session to a new Lagebuchführer (#469): every later mutation is credited to
    /// <paramref name="op"/>, and the handover is logged. A no-op when <paramref name="op"/> is the
    /// current operator.
    /// </summary>
    void ChangeOperator(SessionOperator op);

    /// <returns>
    /// The new entry's id, known at once even on a joined device: it mints the id and the host
    /// keeps it, so an Aufgabe can be linked to the entry before the host's snapshot arrives.
    /// </returns>
    Guid AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null);

    void EditJournalEntry(Guid entryId, string text);

    void ToggleChecklistItem(Guid itemId);

    void AssignRole(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null,
        Guid? nodeId = null,
        RoleSlot slot = RoleSlot.None);

    /// <summary>Ends a running assignment and starts a new one for the same role/section — a handover.</summary>
    void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null);

    /// <summary>Corrects a role assignment's phone number. Logs to the ETB only on a real change.</summary>
    void EditRolePhone(Guid assignmentId, string? phone);

    /// <summary>
    /// Corrects a running assignment's person, Funkrufname, Handynummer and free-text Abschnitt
    /// without a handover. Logs to the ETB only on a real change.
    /// </summary>
    void EditRoleAssignment(Guid assignmentId, string personName, string? callSign, string? phone, string? section);

    /// <summary>Forms an Abschnitt, Unterabschnitt, Bereitstellung or partner Einsatzleitung.</summary>
    void AddCommandNode(
        CommandNodeKind kind,
        string name,
        Guid? parentId = null,
        string? auftrag = null,
        string? standort = null,
        string? funkgruppe = null,
        string? rufname = null,
        string? telefon = null);

    void UpdateCommandNode(
        Guid nodeId, string name, string? auftrag, string? standort, string? funkgruppe, string? rufname, string? telefon);

    void DissolveCommandNode(Guid nodeId);

    /// <summary>
    /// Moves a node under <paramref name="parentId"/> (null: the top level), before
    /// <paramref name="beforeNodeId"/> (null: at the end); see <c>Incident.MoveCommandNode</c>.
    /// </summary>
    void MoveCommandNode(Guid nodeId, Guid? parentId, Guid? beforeNodeId);

    /// <summary>Places a unit in a node; a null <paramref name="nodeId"/> returns it to "nicht zugeordnet".</summary>
    void MoveForceUnit(Guid unitId, Guid? nodeId);

    /// <summary>Marks the Einsatzort on the Lagekarte; null takes the mark off. See <c>Incident.SetLocation</c>.</summary>
    void SetIncidentLocation(GeoPoint? location);

    /// <summary>Puts a unit on the Lagekarte; null takes it off. See <c>Incident.PlaceForceUnit</c>.</summary>
    void PlaceForceUnit(Guid unitId, GeoPoint? position);

    /// <summary>Places a running Funktion in a node and board slot; a null <paramref name="nodeId"/> is the Einsatzleitung.</summary>
    void PlaceRoleAssignment(Guid assignmentId, Guid? nodeId, RoleSlot slot);

    /// <summary>Sets the Führungsstufe; null clears it.</summary>
    void SetFuehrungsstufe(Fuehrungsstufe? stufe);

    void UpdateLeadershipPost(string? standort, string? rufname, string? telefon, string? funkgruppe);

    void AddForceUnit(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0,
        int zugfuehrerCount = 0);

    void UpdateForceUnit(Guid unitId, string? status, string? notes);

    /// <summary>Corrects a unit's Stärke (ZF / GF / Gesamt / davon AGT). Logs to the ETB and retains
    /// the prior values on the unit — but only on a real change (#76, #216).</summary>
    void UpdateForceStrength(Guid unitId, int officerCount, int personnelCount, int scbaCount, int zugfuehrerCount = 0);

    /// <summary>Takes a unit back completely: row, Wert-Historie and totals go, the ETB records
    /// the removal (#76 follow-up).</summary>
    void RemoveForceUnit(Guid unitId);

    /// <summary>Records a task (#88). The timer's minutes land as DueAt relative to the owning
    /// device's/host's clock — never sent as an absolute time over the wire.</summary>
    // sourceEntryId: the ETB entry the Aufgabe was given out from, if any.
    void AddTask(string text, string? assignee, TaskImportance importance, TaskUrgency urgency, int timerMinutes, Guid? sourceEntryId = null);

    /// <summary>Stamps/clears a task's completion (#88).</summary>
    void SetTaskCompleted(Guid taskId, bool isDone);

    /// <summary>Records a Beteiligte/n (house owner, vehicle owner, police contact). No ETB line.</summary>
    void AddInvolvedParty(string name, string? phone, string? notes);

    /// <summary>Corrects a Beteiligte/n in place. No ETB line.</summary>
    void UpdateInvolvedParty(Guid partyId, string name, string? phone, string? notes);

    /// <summary>Takes a Beteiligte/n back completely. No ETB line.</summary>
    void RemoveInvolvedParty(Guid partyId);

    /// <summary>The Wohnung a Beteiligte/r was found in (#702); null clears it. No ETB line.</summary>
    void SetInvolvedPartyDwelling(Guid partyId, Guid? dwellingId);

    /// <summary>Where a Beteiligte/r is now, stamped by the host; blank clears it. No ETB line.</summary>
    void SetInvolvedPartyWhereabouts(Guid partyId, string? whereabouts);

    /// <summary>Starts a Lagemeldung as a draft (#592). No ETB line until it is sent.</summary>
    void AddSituationReport(
        string? reporter, string? location, string? situation, string? measures, string? units, string? requests);

    /// <summary>Corrects a draft Lagemeldung in place. No ETB line.</summary>
    void UpdateSituationReport(
        Guid reportId, string? reporter, string? location, string? situation, string? measures, string? units, string? requests);

    /// <summary>Discards a draft Lagemeldung. No ETB line.</summary>
    void RemoveSituationReport(Guid reportId);

    /// <summary>Sends a draft Lagemeldung: numbers it and writes its Funkspruch to the ETB as an
    /// outgoing entry to <paramref name="dispatchCentreName"/>.</summary>
    void SendSituationReport(Guid reportId, string dispatchCentreName);

    /// <summary>Hands a Gerät out (Ausgabeliste), stamped by the host. No ETB line.</summary>
    void IssueItem(string device, string issuedTo, string? issuedToPhone, string? issuedToDetails, string? remark);

    /// <summary>Corrects a line of the Ausgabeliste in place. No ETB line.</summary>
    void UpdateIssuedItem(Guid itemId, string device, string issuedTo, string? issuedToPhone, string? issuedToDetails, string? remark);

    /// <summary>Records that a Gerät came back, stamped by the host. No ETB line.</summary>
    void ReturnIssuedItem(Guid itemId);

    /// <summary>Takes a return back, for a ZURÜCK pressed on the wrong line. No ETB line.</summary>
    void UndoIssuedItemReturn(Guid itemId);

    /// <summary>Takes a line of the Ausgabeliste back completely. No ETB line.</summary>
    void RemoveIssuedItem(Guid itemId);

    /// <summary>Corrects a task's fields (#246): typo fixes, reassignment, re-prioritizing.</summary>
    void UpdateTask(Guid taskId, string text, string? assignee, TaskImportance importance, TaskUrgency urgency);

    /// <summary>Adds minutes to a task's due time (#246 "+5" quick action).</summary>
    void ExtendTaskTimer(Guid taskId, int minutes);

    void AddScbaTrupp(
        string designation,
        IEnumerable<TruppMember> members,
        int entryPressure,
        int? truppNumber = null,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes);

    void StartScbaTrupp(Guid truppId);

    void RecordScbaPressure(Guid truppId, int bar, string? location = null);

    void WithdrawScbaTrupp(Guid truppId);

    void MarkScbaRemoved(Guid truppId);

    /// <summary>Designates the Sicherheitstrupp standing by for a Trupp (#399), or clears it
    /// with a null <paramref name="safetyTruppId"/>.</summary>
    void SetScbaSafetyTrupp(Guid truppId, Guid? safetyTruppId);

    void SetIncidentNumber(IncidentNumber? number);

    void SetKeyword(string? keyword);

    void SetAddress(string? street, string? district, string? houseNumber = null, string? postalCode = null, string? place = null);

    void SetStatus(string? status);

    /// <summary>
    /// Records the state of an incident-level timer (keyed) so it survives a reopen/crash. Driven
    /// only by the authoritative device (the ILS reminder is gated <c>!IsRemote</c>), so a joined
    /// client never calls this.
    /// </summary>
    void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning);

    /// <summary>
    /// Closes the incident for good. Only the authoritative device may: a joined client
    /// (<see cref="IsRemote"/>) throws <see cref="InvalidOperationException"/>, and the host refuses
    /// a peer's close command (#465).
    /// </summary>
    void Close();

    /// <summary>
    /// Attaches a file. Unlike every mutation above, this is genuinely awaited rather than
    /// fire-and-forget: a multi-MB upload is neither instant nor safe to fail silently, so the
    /// caller needs to show a spinner and surface a thrown exception (closed incident, unsupported
    /// type, over the size cap, or — on a joined client — a network failure).
    /// </summary>
    Task AddFileAsync(string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken = default);

    /// <summary>Null when the bytes are unavailable — never throws, so a caller (a file-row "open"
    /// action, or the PDF exporter) degrades quietly rather than crashing on a missing attachment.</summary>
    Task<byte[]?> GetFileBytesAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Corrects a file's display label. Silent — no ETB entry, matching UpdateForceUnit's
    /// Bemerkung field. Null/blank resets the label back to the file's original name.</summary>
    void RenameFile(Guid fileId, string? displayName);

    /// <summary>
    /// Removes an attachment: metadata and bytes both go, and the ETB records the removal (#262
    /// UX follow-up), mirroring RemoveForceUnit. Like <see cref="AddFileAsync"/> — and unlike every
    /// other mutation above — this is genuinely awaited rather than fire-and-forget: a real disk
    /// delete is worth surfacing a failure for.
    /// </summary>
    Task RemoveFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    void AddCoBuilding(
        string name,
        int floorCount,
        int apartmentsPerFloor,
        int undergroundFloorCount = 0,
        IReadOnlyDictionary<string, string?>? apartmentLabels = null);

    void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0);

    void RemoveCoBuilding(Guid buildingId);

    void RecordCoValue(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue);

    void SetDwellingStatus(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status);

    void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, KeyAvailability keyAvailable, string? keyNumber = null);

    void SetFloorDescription(Guid buildingId, int floorOrdinal, string? description);

    void SetApartmentLabel(Guid buildingId, int floorOrdinal, int apartmentNumber, string? label);

    void SetApartmentCount(Guid buildingId, int floorOrdinal, int count);

    /// <summary>Removes the named Wohnungen from one floor, closing the gap behind them
    /// (#419) -- the pick-which counterpart to <see cref="SetApartmentCount"/>.</summary>
    void RemoveDwellings(Guid buildingId, int floorOrdinal, IReadOnlyList<int> apartmentNumbers);
}
