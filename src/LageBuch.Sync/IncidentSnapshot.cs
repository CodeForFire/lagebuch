using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;

namespace LageBuch.Sync;

/// <summary>
/// A JSON-serializable snapshot of the whole <see cref="Incident"/> aggregate — the wire form the
/// host broadcasts and a joining client reconstructs from. The fields mirror
/// <see cref="Incident.Rehydrate"/> exactly, so <see cref="SnapshotMapper"/> is a JSON-shaped
/// sibling of the SQL mapping in <c>LageBuch.Persistence.IncidentRepository</c>.
/// </summary>
/// <remarks>
/// <c>Epoch</c> and <c>Revision</c> are the two members a client compares rather than displays
/// (#295). <c>Revision</c> is the host's change counter; <c>Epoch</c> names the counter it belongs to,
/// a fresh random value every time the host starts sharing. See <see cref="SyncPosition"/> for how a
/// client orders two snapshots by them.
/// <para>
/// Their defaults mean nobody attached them. <see cref="SnapshotMapper.ToSnapshot"/> leaves both
/// alone and only <c>IncidentHost</c> fills them in, so the persistence path (<c>IncidentStore</c>,
/// which round-trips this type in memory and never reads them) is untouched. Appending defaulted
/// trailing parameters is this type's documented compat convention.
/// </para>
/// </remarks>
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
    IReadOnlyList<ChecklistListDto> Checklists,
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

    // Defaulted like DwellingDto.Readings: a snapshot without the key reads as "no Beteiligte".
    IReadOnlyList<InvolvedPartyDto>? InvolvedParties = null,
    long Revision = 0,
    Guid Epoch = default,
    string? HouseNumber = null,
    string? PostalCode = null,
    string? Place = null,

    // Defaulted so every existing positional construction keeps compiling; a snapshot
    // without the key reads as "no Lagemeldungen" (#592).
    IReadOnlyList<SituationReportDto>? SituationReports = null,

    // Defaulted the same way; a snapshot without the key reads as "nothing handed out".
    IReadOnlyList<IssuedItemDto>? IssuedItems = null,

    // Defaulted like InvolvedParties: a snapshot without the keys reads as no Abschnitte, no
    // Führungsstufe and an empty Einsatzleitung post.
    IReadOnlyList<CommandNodeDto>? CommandNodes = null,
    Fuehrungsstufe? Fuehrungsstufe = null,
    string? ElStandort = null,
    string? ElRufname = null,
    string? ElTelefon = null,
    IReadOnlyList<string>? ElFunkgruppenTmo = null,
    IReadOnlyList<string>? ElFunkgruppenDmo = null,

    // The Einsatzort on the Lagekarte, as plain numbers: a peer's values are checked on arrival
    // (GeoPoint.TryCreate), never trusted into a constructor that would throw mid-sync.
    double? LocationLatitude = null,
    double? LocationLongitude = null,

    // The Einsatz's rail overrides, keyed by nav entry; a snapshot without the key reads as none.
    IReadOnlyDictionary<string, bool>? NavOverrides = null);

public sealed record TimerDto(
    string Key,
    DateTimeOffset CycleAnchor,
    int IntervalMinutes,
    int RecurringIntervalMinutes,
    bool IsRunning);

/// <summary>One of the Einsatz's Checklisten on the wire: its id, its name and its items.</summary>
public sealed record ChecklistListDto(
    Guid Id,
    string Title,
    IReadOnlyList<ChecklistItemDto> Items);

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
    string? Phone,

    // Where the Funktion sits and the post it holds; absent reads as unplaced.
    Guid? NodeId = null,
    RoleSlot Slot = RoleSlot.None);

public sealed record ForceUnitDto(
    Guid Id,
    string Brigade,
    string? CallSign,
    int PersonnelCount,
    int ScbaCount,
    string? Status,
    string? Notes,
    int OfficerCount,
    IReadOnlyList<ForceUnitStrengthEditDto> Edits,
    int ZugfuehrerCount = 0,
    Guid? NodeId = null,
    double? PositionLatitude = null,
    double? PositionLongitude = null);

// Mirrors Domain.ForceUnitStrengthEdit: one prior Stärke retained on correction (#76), the
// force-row sibling of EtbEntryEditDto.
public sealed record ForceUnitStrengthEditDto(
    int PreviousOfficerCount,
    int PreviousPersonnelCount,
    int PreviousScbaCount,
    string EditedBy,
    DateTimeOffset EditedAt,
    int PreviousZugfuehrerCount = 0);

public sealed record TruppMemberDto(TruppRole Role, string Name);

public sealed record PressureReadingDto(DateTimeOffset Time, int Bar, string? Location = null);

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
    IReadOnlyList<PressureReadingDto> Readings,
    Guid? SafetyTruppId = null);

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
    string? CompletedBy,

    // Last and defaulted, so a snapshot from a host that predates the link still reads: unlinked.
    Guid? SourceEntryId = null);

/// <summary>One Lagemeldung on the wire; mirrors <see cref="LageBuch.Domain.SituationReports.SituationReport"/>.</summary>
public sealed record SituationReportDto(
    Guid Id,
    int? Number,
    string? Reporter,
    string? Location,
    string? Situation,
    string? Measures,
    string? Units,
    string? Requests,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    string? SentBy,
    Guid? EtbEntryId = null);

/// <summary>One line of the Ausgabeliste on the wire; mirrors <see cref="LageBuch.Domain.Equipment.IssuedItem"/>.</summary>
public sealed record IssuedItemDto(
    Guid Id,
    string Device,
    string IssuedTo,
    string? IssuedToPhone,
    string? IssuedToDetails,
    string? Remark,
    DateTimeOffset IssuedAt,
    string IssuedBy,
    DateTimeOffset? ReturnedAt,
    string? ReturnedBy);

/// <summary>One Beteiligte/r on the wire; mirrors <see cref="LageBuch.Domain.Involved.InvolvedParty"/>.</summary>
public sealed record InvolvedPartyDto(
    Guid Id, string Name, string? Phone, string? Notes, string CreatedBy, DateTimeOffset CreatedAt,
    Guid? DwellingId = null, string? DwellingLabel = null,
    string? Whereabouts = null, DateTimeOffset? WhereaboutsAt = null);

/// <summary>One Abschnitt (or Bereitstellung, partner Einsatzleitung) on the wire; mirrors <see cref="CommandNode"/>.</summary>
public sealed record CommandNodeDto(
    Guid Id, CommandNodeKind Kind, string Name, Guid? ParentId, string? Auftrag, string? Standort,
    string? Funkgruppe, string? Rufname, string? Telefon, DateTimeOffset? DissolvedAt,
    IReadOnlyList<string>? FunkgruppenTmo = null, IReadOnlyList<string>? FunkgruppenDmo = null);

public sealed record BuildingDto(
    Guid Id, string Name, int FloorCount, int ApartmentsPerFloor,
    Dictionary<string, string?> FloorDescriptions, int Ordinal,
    Dictionary<string, string?>? ApartmentLabels = null,
    int UndergroundFloorCount = 0,
    Dictionary<string, int>? ApartmentCounts = null);

public sealed record DwellingDto(
    Guid Id, Guid BuildingId, int FloorOrdinal, int ApartmentNumber,
    string? ResidentName, DwellingStatus Status, bool? KeyAvailable, int? CoValue,

    // Appended with a default, the BuildingDto convention: a snapshot written before #424 has no
    // readings key, deserializes to null, and maps to an empty series. That default is this
    // layer's equivalent of a migration -- the wire has no schema version of its own.
    IReadOnlyList<CoReadingDto>? Readings = null,

    // Protocol 24, appended the same way: a snapshot without it reads as no Schlüsselnummer.
    string? KeyNumber = null);

/// <summary>One CO reading on the wire; the JSON-shaped sibling of
/// <see cref="LageBuch.Domain.CoMeasurement.CoReading"/>.</summary>
public sealed record CoReadingDto(DateTimeOffset MeasuredAt, int? Value, string RecordedBy);
