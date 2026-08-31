using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Sync;

/// <summary>
/// A JSON-serializable snapshot of the whole <see cref="Incident"/> aggregate — the wire form the
/// host broadcasts and a joining client reconstructs from. The fields mirror
/// <see cref="Incident.Rehydrate"/> exactly, so <see cref="SnapshotMapper"/> is a JSON-shaped
/// sibling of the SQL mapping in <c>LageBuch.Persistence.IncidentRepository</c>.
/// </summary>
public sealed record IncidentSnapshot(
    Guid Id,
    DateTimeOffset StartedAt,
    IncidentState State,
    string? IncidentNumber,
    string? Keyword,
    string? Street,
    string? District,
    string? Status,
    DateTimeOffset? ClosedAt,
    string? ClosedBy,
    IReadOnlyList<ChecklistItemDto> ChecklistAufbau,
    IReadOnlyList<ChecklistItemDto> ChecklistAbbau,
    IReadOnlyList<EtbEntryDto> Journal,
    IReadOnlyList<RoleAssignmentDto> Roles,
    IReadOnlyList<ForceUnitDto> Forces,
    IReadOnlyList<ScbaTruppDto> ScbaTrupps,
    IReadOnlyList<AuditEventDto> Audit,
    IReadOnlyList<TimerDto> Timers,
    IReadOnlyList<IncidentFileDto> Files,
    IReadOnlyList<TaskDto> Tasks,
    IReadOnlyList<BuildingDto> Buildings,
    IReadOnlyList<DwellingDto> Dwellings,
    IReadOnlyList<WasserfoerderungLeitungDto> Wasserfoerderung);

public sealed record TimerDto(
    string Key,
    DateTimeOffset CycleAnchor,
    int IntervalMinutes,
    int RecurringIntervalMinutes,
    bool IsRunning);

public sealed record ChecklistItemDto(Guid Id, string Text, bool IsDone, string? Note, bool IsMandatory);

public sealed record EtbEntryDto(
    Guid Id,
    DateTimeOffset Timestamp,
    EtbDirection Direction,
    string Text,
    string EnteredBy,
    string? From,
    string? To,
    IReadOnlyList<EtbEntryEditDto> Edits);

public sealed record EtbEntryEditDto(string PreviousText, string EditedBy, DateTimeOffset EditedAt);

public sealed record RoleAssignmentDto(
    Guid Id,
    string Role,
    string PersonName,
    string? CallSign,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Section,
    string? Phone);

public sealed record ForceUnitDto(
    Guid Id,
    string Brigade,
    string? CallSign,
    int PersonnelCount,
    int ScbaCount,
    string? Status,
    string? Notes,
    int OfficerCount,
    IReadOnlyList<ForceUnitStrengthEditDto> Edits);

// Mirrors Domain.ForceUnitStrengthEdit: one prior Stärke retained on correction (#76), the
// force-row sibling of EtbEntryEditDto.
public sealed record ForceUnitStrengthEditDto(
    int PreviousOfficerCount,
    int PreviousPersonnelCount,
    int PreviousScbaCount,
    string EditedBy,
    DateTimeOffset EditedAt);

public sealed record TruppMemberDto(TruppRole Role, string Name);

public sealed record PressureReadingDto(DateTimeOffset Time, int Bar);

public sealed record ScbaTruppDto(
    Guid Id,
    int TruppNumber,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? StartTime,
    DateTimeOffset? WithdrawTime,
    string Designation,
    IReadOnlyList<TruppMemberDto> Members,
    string? CallSign,
    string? Task,
    int? EntryPressure,
    int MaxDurationMinutes,
    int ReturnPressureBar,
    int PressureControlIntervalMinutes,
    DateTimeOffset? ExitTime,
    IReadOnlyList<PressureReadingDto> Readings);

public sealed record AuditEventDto(DateTimeOffset At, string Action, string By);

/// <summary>
/// Metadata only — deliberately no bytes here, so the snapshot broadcast every command triggers
/// (§5) stays small regardless of how large or numerous the attached files are. A client fetches
/// the actual bytes on demand via <c>GET /files/{id}</c> (see <c>IncidentHost</c>).
/// </summary>
public sealed record IncidentFileDto(
    Guid Id, string FileName, string DisplayName, string ContentType, long SizeBytes, DateTimeOffset AddedAt, string AddedBy);

// Mirrors Domain.IncidentTask (#88); importance/urgency ride as the domain enums directly (the
// wire serializes them as name strings), matching how EtbEntryDto carries EtbDirection.
public sealed record TaskDto(
    Guid Id,
    string Text,
    string Assignee,
    TaskImportance Importance,
    TaskUrgency Urgency,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? CompletedAt,
    string? CompletedBy);

public sealed record BuildingDto(
    Guid Id, string Name, int FloorCount, int ApartmentsPerFloor,
    Dictionary<string, string?> FloorDescriptions, int Ordinal,
    Dictionary<string, string?>? ApartmentLabels = null);

public sealed record DwellingDto(
    Guid Id, Guid BuildingId, int FloorOrdinal, int ApartmentNumber,
    string? ResidentName, DwellingStatus Status, bool? KeyAvailable, int? CoValue);

public sealed record WasserfoerderungLeitungDto(
    Guid Id,
    int Number,
    string? Uebergabestelle,
    string? Ansprechpartner,
    int FlowLMin,
    double FeedPressureBar,
    double LengthMeters,
    double ElevationRiseMeters,
    int HoseCount,
    int ReserveHoseCount,
    int PumpCount,
    int ReservePumpCount,
    IReadOnlyList<double> PumpPositionsMeters,
    IReadOnlyList<GeoPoint>? RoutePoints = null);
