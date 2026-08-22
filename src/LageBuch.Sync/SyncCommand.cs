using System.Text.Json.Serialization;
using LageBuch.Domain.Etb;

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
[JsonDerivedType(typeof(AddScbaTruppCommand), "addScbaTrupp")]
[JsonDerivedType(typeof(StartScbaTruppCommand), "startScbaTrupp")]
[JsonDerivedType(typeof(RecordScbaPressureCommand), "recordScbaPressure")]
[JsonDerivedType(typeof(MarkScbaReturnedCommand), "markScbaReturned")]
[JsonDerivedType(typeof(SetIncidentNumberCommand), "setIncidentNumber")]
[JsonDerivedType(typeof(SetKeywordCommand), "setKeyword")]
[JsonDerivedType(typeof(SetAddressCommand), "setAddress")]
[JsonDerivedType(typeof(SetStatusCommand), "setStatus")]
[JsonDerivedType(typeof(CloseIncidentCommand), "close")]
[JsonDerivedType(typeof(AddFileCommand), "addFile")]
[JsonDerivedType(typeof(RenameFileCommand), "renameFile")]
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

public sealed record AddScbaTruppCommand(
    string Designation, IReadOnlyList<TruppMemberDto> Members, string? CallSign, string? Task,
    int MaxDurationMinutes, int ReturnPressureBar, int PressureControlIntervalMinutes) : SyncCommand;

public sealed record StartScbaTruppCommand(Guid TruppId, int StartPressure) : SyncCommand;

public sealed record RecordScbaPressureCommand(Guid TruppId, int Bar) : SyncCommand;

public sealed record MarkScbaReturnedCommand(Guid TruppId) : SyncCommand;

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
