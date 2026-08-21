using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;

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
    IReadOnlyList<IncidentFileDto> Files);

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
    string? Notes);

public sealed record TruppMemberDto(TruppRole Role, string Name);

public sealed record PressureReadingDto(DateTimeOffset Time, int Bar);

public sealed record ScbaTruppDto(
    Guid Id,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? StartTime,
    string Designation,
    IReadOnlyList<TruppMemberDto> Members,
    string? CallSign,
    string? Task,
    int? StartPressure,
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
