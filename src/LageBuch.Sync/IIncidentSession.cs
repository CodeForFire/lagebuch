using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
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

    /// <summary>Raised after the incident state changes (a local mutation, or a host broadcast).</summary>
    [SuppressMessage("Design", "CA1003", Justification = "In-process fire-and-forget event with C#-only subscribers; Action matches the pervasive Action event convention (GoHomeRequested etc.).")]
    event Action? Changed;

    void AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null);

    void EditJournalEntry(Guid entryId, string text);

    void ToggleChecklistItem(Guid itemId);

    void AssignRole(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null);

    /// <summary>Ends a running assignment and starts a new one for the same role/section — a handover.</summary>
    void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null);

    /// <summary>Corrects a role assignment's phone number. Logs to the ETB only on a real change.</summary>
    void EditRolePhone(Guid assignmentId, string? phone);

    void AddForceUnit(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0);

    void UpdateForceUnit(Guid unitId, string? status, string? notes);

    /// <summary>Corrects a unit's Stärke (GF / Gesamt / davon AGT). Logs to the ETB and retains the
    /// prior values on the unit — but only on a real change (#76).</summary>
    void UpdateForceStrength(Guid unitId, int officerCount, int personnelCount, int scbaCount);

    /// <summary>Takes a unit back completely: row, Wert-Historie and totals go, the ETB records
    /// the removal (#76 follow-up).</summary>
    void RemoveForceUnit(Guid unitId);

    /// <summary>Records a task (#88). The timer's minutes land as DueAt relative to the owning
    /// device's/host's clock — never sent as an absolute time over the wire.</summary>
    void AddTask(string text, string? assignee, TaskImportance importance, TaskUrgency urgency, int timerMinutes);

    /// <summary>Stamps/clears a task's completion (#88).</summary>
    void SetTaskCompleted(Guid taskId, bool isDone);

    /// <summary>Plans one Förderstrecke-Leitung (#150, Plan A). Silent — no ETB line, no attribution,
    /// exactly like tasks. Length/elevation ride the wire as plan inputs; the host computes the number
    /// and every derived figure.</summary>
    void AddWasserfoerderungLeitung(string? uebergabestelle, string? ansprechpartner, double lengthMeters, double elevationRiseMeters);

    void RemoveWasserfoerderungLeitung(Guid leitungId);

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

    void RecordScbaPressure(Guid truppId, int bar);

    void WithdrawScbaTrupp(Guid truppId);

    void MarkScbaRemoved(Guid truppId);

    void SetIncidentNumber(IncidentNumber? number);

    void SetKeyword(string? keyword);

    void SetAddress(string? street, string? district);

    void SetStatus(string? status);

    /// <summary>
    /// Records the state of an incident-level timer (keyed) so it survives a reopen/crash. Driven
    /// only by the authoritative device (the ILS reminder is gated <c>!IsRemote</c>), so a joined
    /// client never calls this.
    /// </summary>
    void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning);

    void Close();

    /// <summary>
    /// Attaches a file. Unlike every mutation above, this is genuinely awaited rather than
    /// fire-and-forget: a multi-MB upload is neither instant nor safe to fail silently, so the
    /// caller needs to show a spinner and surface a thrown exception (closed incident, unsupported
    /// type, over the size cap, or — on a joined client — a network failure).
    /// </summary>
    Task AddFileAsync(string fileName, string contentType, byte[] bytes);

    /// <summary>Null when the bytes are unavailable — never throws, so a caller (a file-row "open"
    /// action, or the PDF exporter) degrades quietly rather than crashing on a missing attachment.</summary>
    Task<byte[]?> GetFileBytesAsync(Guid fileId);

    /// <summary>Corrects a file's display label. Silent — no ETB entry, matching UpdateForceUnit's
    /// Bemerkung field. Null/blank resets the label back to the file's original name.</summary>
    void RenameFile(Guid fileId, string? displayName);

    void AddCoBuilding(string name, int floorCount, int apartmentsPerFloor);

    void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor);

    void RemoveCoBuilding(Guid buildingId);

    void RecordCoValue(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue);

    void SetDwellingStatus(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status);

    void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, bool? keyAvailable);

    void SetFloorDescription(Guid buildingId, int floorOrdinal, string? description);

    void SetApartmentLabel(Guid buildingId, int apartmentNumber, string? label);
}
