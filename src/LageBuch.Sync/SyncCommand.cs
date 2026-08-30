using System.Text.Json.Serialization;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Sync;

/// <summary>
/// One command per domain mutation method. A joined client sends these to the host's single
/// generic <c>POST /command</c> endpoint; the host deserializes by the <c>$type</c> discriminator
/// and invokes the matching mutation on its authoritative session. Operator identity travels on
/// the commands whose domain method attributes an entry (per the design's per-device identity),
/// so an ETB line or role assignment is credited to the person at the sending device.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AddJournalEntryCommand), "addJournalEntry")]
[JsonDerivedType(typeof(EditJournalEntryCommand), "editJournalEntry")]
[JsonDerivedType(typeof(ToggleChecklistItemCommand), "toggleChecklistItem")]
[JsonDerivedType(typeof(AssignRoleCommand), "assignRole")]
[JsonDerivedType(typeof(TransferRoleCommand), "transferRole")]
[JsonDerivedType(typeof(EditRolePhoneCommand), "editRolePhone")]
[JsonDerivedType(typeof(AddForceUnitCommand), "addForceUnit")]
[JsonDerivedType(typeof(UpdateForceUnitCommand), "updateForceUnit")]
[JsonDerivedType(typeof(UpdateForceStrengthCommand), "updateForceStrength")]
[JsonDerivedType(typeof(RemoveForceUnitCommand), "removeForceUnit")]
[JsonDerivedType(typeof(AddScbaTruppCommand), "addScbaTrupp")]
[JsonDerivedType(typeof(StartScbaTruppCommand), "startScbaTrupp")]
[JsonDerivedType(typeof(RecordScbaPressureCommand), "recordScbaPressure")]
[JsonDerivedType(typeof(WithdrawScbaTruppCommand), "withdrawScbaTrupp")]
[JsonDerivedType(typeof(MarkScbaRemovedCommand), "markScbaRemoved")]
[JsonDerivedType(typeof(SetIncidentNumberCommand), "setIncidentNumber")]
[JsonDerivedType(typeof(SetKeywordCommand), "setKeyword")]
[JsonDerivedType(typeof(SetAddressCommand), "setAddress")]
[JsonDerivedType(typeof(SetStatusCommand), "setStatus")]
[JsonDerivedType(typeof(CloseIncidentCommand), "close")]
[JsonDerivedType(typeof(AddFileCommand), "addFile")]
[JsonDerivedType(typeof(RenameFileCommand), "renameFile")]
[JsonDerivedType(typeof(AddTaskCommand), "addTask")]
[JsonDerivedType(typeof(SetTaskCompletedCommand), "setTaskCompleted")]
[JsonDerivedType(typeof(AddCoBuildingCommand), "addCoBuilding")]
[JsonDerivedType(typeof(UpdateCoBuildingStructureCommand), "updateCoBuildingStructure")]
[JsonDerivedType(typeof(RemoveCoBuildingCommand), "removeCoBuilding")]
[JsonDerivedType(typeof(RecordCoValueCommand), "recordCoValue")]
[JsonDerivedType(typeof(SetDwellingStatusCommand), "setDwellingStatus")]
[JsonDerivedType(typeof(UpdateDwellingDetailsCommand), "updateDwellingDetails")]
[JsonDerivedType(typeof(SetFloorDescriptionCommand), "setFloorDescription")]
[JsonDerivedType(typeof(SetApartmentLabelCommand), "setApartmentLabel")]
[JsonDerivedType(typeof(AddWasserfoerderungLeitungCommand), "addWasserfoerderungLeitung")]
[JsonDerivedType(typeof(RemoveWasserfoerderungLeitungCommand), "removeWasserfoerderungLeitung")]
[JsonDerivedType(typeof(AddWasserfoerderungLeitungFromRouteCommand), "addWasserfoerderungLeitungFromRoute")]
public abstract record SyncCommand;

/// <summary>The operator at the sending device — carried on attributed mutations (see §6).</summary>
public sealed record OperatorDto(string Name, string? CallSign);

public sealed record AddJournalEntryCommand(
    OperatorDto Operator, EtbDirection Direction, string Text, string? From, string? To) : SyncCommand;

public sealed record EditJournalEntryCommand(OperatorDto Operator, Guid EntryId, string Text) : SyncCommand;

public sealed record ToggleChecklistItemCommand(OperatorDto Operator, Guid ItemId) : SyncCommand;

public sealed record AssignRoleCommand(
    OperatorDto Operator, string Role, string PersonName, string? CallSign,
    DateTimeOffset? From, DateTimeOffset? To, string? Section, string? Phone) : SyncCommand;

// No end-time on the wire: the host stamps it with its own (authoritative) clock, so devices with
// slightly different clocks can't disagree on when a handover happened.
public sealed record TransferRoleCommand(
    OperatorDto Operator, Guid AssignmentId, string NewPersonName, string? NewCallSign, string? NewPhone) : SyncCommand;

public sealed record EditRolePhoneCommand(OperatorDto Operator, Guid AssignmentId, string? Phone) : SyncCommand;

// OfficerCount defaults to 0 so a pre-#76 payload (no such property on the wire) deserializes
// as "keine Führungskraft erfasst" instead of failing the contract.
public sealed record AddForceUnitCommand(
    OperatorDto Operator, string Brigade, int PersonnelCount,
    string? CallSign, string? Status, string? Notes, int ScbaCount, int OfficerCount = 0) : SyncCommand;

public sealed record UpdateForceUnitCommand(
    OperatorDto Operator, Guid UnitId, string? Status, string? Notes) : SyncCommand;

public sealed record UpdateForceStrengthCommand(
    OperatorDto Operator, Guid UnitId, int OfficerCount, int PersonnelCount, int ScbaCount) : SyncCommand;

public sealed record RemoveForceUnitCommand(
    OperatorDto Operator, Guid UnitId) : SyncCommand;

// EntryPressure is required -- #78 moves pressure capture to registration time, so a client on
// this version always sends one. TruppNumber stays optional/nullable so the host's auto-assign
// path (Incident.NextFreeScbaTruppNumber) is reachable even when the caller doesn't supply one.
public sealed record AddScbaTruppCommand(
    string Designation, IReadOnlyList<TruppMemberDto> Members, string? CallSign, string? Task,
    int MaxDurationMinutes, int ReturnPressureBar, int PressureControlIntervalMinutes,
    int EntryPressure, int? TruppNumber = null) : SyncCommand;

public sealed record StartScbaTruppCommand(Guid TruppId) : SyncCommand;

public sealed record RecordScbaPressureCommand(Guid TruppId, int Bar) : SyncCommand;

/// <summary>Rückzug — the Trupp begins its withdrawal, still under air.</summary>
public sealed record WithdrawScbaTruppCommand(Guid TruppId) : SyncCommand;

/// <summary>Abgenommen — replaces the old "markScbaReturned"/Zurück command (#78).</summary>
public sealed record MarkScbaRemovedCommand(Guid TruppId) : SyncCommand;

public sealed record SetIncidentNumberCommand(string? IncidentNumber) : SyncCommand;

public sealed record SetKeywordCommand(string? Keyword) : SyncCommand;

public sealed record SetAddressCommand(string? Street, string? District) : SyncCommand;

public sealed record SetStatusCommand(string? Status) : SyncCommand;

public sealed record CloseIncidentCommand(OperatorDto Operator) : SyncCommand;

// Bytes ride this one-shot upload command (System.Text.Json base64-encodes byte[] automatically),
// but never the broadcast snapshot that follows every command — see IncidentSnapshot/IncidentFileDto.
public sealed record AddFileCommand(OperatorDto Operator, string FileName, string ContentType, byte[] Bytes) : SyncCommand;

// No operator on the wire: renaming is a silent label correction (no ETB entry), unlike every
// attributed command above.
public sealed record RenameFileCommand(Guid FileId, string? DisplayName) : SyncCommand;

// TimerMinutes travels instead of an absolute DueAt: the host stamps the anchor with its own
// authoritative clock on apply, like every timestamped command.
public sealed record AddTaskCommand(
    OperatorDto Operator, string Text, string Assignee,
    TaskImportance Importance, TaskUrgency Urgency, int TimerMinutes) : SyncCommand;

public sealed record SetTaskCompletedCommand(OperatorDto Operator, Guid TaskId, bool IsDone) : SyncCommand;

public sealed record AddCoBuildingCommand(
    OperatorDto Operator, string Name, int FloorCount, int ApartmentsPerFloor) : SyncCommand;

public sealed record UpdateCoBuildingStructureCommand(
    OperatorDto Operator, Guid BuildingId, int FloorCount, int ApartmentsPerFloor) : SyncCommand;

public sealed record RemoveCoBuildingCommand(
    OperatorDto Operator, Guid BuildingId) : SyncCommand;

public sealed record RecordCoValueCommand(
    OperatorDto Operator, Guid BuildingId, int FloorOrdinal, int ApartmentNumber, int? CoValue) : SyncCommand;

public sealed record SetDwellingStatusCommand(
    OperatorDto Operator, Guid BuildingId, int FloorOrdinal, int ApartmentNumber, DwellingStatus Status) : SyncCommand;

// No operator (silent)
public sealed record UpdateDwellingDetailsCommand(
    Guid BuildingId, int FloorOrdinal, int ApartmentNumber, string? ResidentName, bool? KeyAvailable) : SyncCommand;

// No operator (silent)
public sealed record SetFloorDescriptionCommand(
    Guid BuildingId, int FloorOrdinal, string? Description) : SyncCommand;

// No operator (silent)
public sealed record SetApartmentLabelCommand(
    Guid BuildingId, int ApartmentNumber, string? Label) : SyncCommand;

// No operator: plan lines are silent (no ETB line, no audit), so the wire carries just the plan
// inputs — the host computes Number and every derived figure with its own planner.
public sealed record AddWasserfoerderungLeitungCommand(
    string? Uebergabestelle, string? Ansprechpartner, double LengthMeters, double ElevationRiseMeters) : SyncCommand;

public sealed record RemoveWasserfoerderungLeitungCommand(Guid LeitungId) : SyncCommand;

// Plan B (#150 phase 2): carries the already-sampled profile so every replica computes the same
// pump placement without needing its own copy of the DEM file — see
// Incident.AddWasserfoerderungLeitungFromRoute.
public sealed record AddWasserfoerderungLeitungFromRouteCommand(
    string? Uebergabestelle,
    string? Ansprechpartner,
    IReadOnlyList<GeoPoint> RoutePoints,
    IReadOnlyList<ElevationProfileSample> Profile) : SyncCommand;
