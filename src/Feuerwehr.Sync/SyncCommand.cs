using System.Text.Json.Serialization;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Sync;

/// <summary>
/// One command per domain mutation method. A joined client sends these to the host's single
/// generic <c>POST /command</c> endpoint; the host deserializes by the <c>$type</c> discriminator
/// and invokes the matching mutation on its authoritative session. Operator identity travels on
/// the commands whose domain method attributes an entry (per the design's per-device identity),
/// so an ETB line or role assignment is credited to the person at the sending device.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AddJournalEntryCommand), "addJournalEntry")]
[JsonDerivedType(typeof(ToggleChecklistItemCommand), "toggleChecklistItem")]
[JsonDerivedType(typeof(AssignRoleCommand), "assignRole")]
[JsonDerivedType(typeof(EndRoleAssignmentCommand), "endRoleAssignment")]
[JsonDerivedType(typeof(AddForceUnitCommand), "addForceUnit")]
[JsonDerivedType(typeof(UpdateForceUnitCommand), "updateForceUnit")]
[JsonDerivedType(typeof(AddScbaTruppCommand), "addScbaTrupp")]
[JsonDerivedType(typeof(StartScbaTruppCommand), "startScbaTrupp")]
[JsonDerivedType(typeof(RecordScbaPressureCommand), "recordScbaPressure")]
[JsonDerivedType(typeof(MarkScbaReturnedCommand), "markScbaReturned")]
[JsonDerivedType(typeof(SetIncidentNumberCommand), "setIncidentNumber")]
[JsonDerivedType(typeof(SetKeywordCommand), "setKeyword")]
[JsonDerivedType(typeof(SetAddressCommand), "setAddress")]
[JsonDerivedType(typeof(SetStatusCommand), "setStatus")]
[JsonDerivedType(typeof(CloseIncidentCommand), "close")]
public abstract record SyncCommand;

/// <summary>The operator at the sending device — carried on attributed mutations (see §6).</summary>
public sealed record OperatorDto(string Name, string? CallSign);

public sealed record AddJournalEntryCommand(
    OperatorDto Operator, EtbDirection Direction, string Text, string? From, string? To) : SyncCommand;

public sealed record ToggleChecklistItemCommand(Guid ItemId) : SyncCommand;

public sealed record AssignRoleCommand(
    string Role, string PersonName, string? CallSign,
    DateTimeOffset? From, DateTimeOffset? To, string? Section, string? Phone) : SyncCommand;

// No end-time on the wire: the host stamps it with its own (authoritative) clock, so devices with
// slightly different clocks can't disagree on when a role ended.
public sealed record EndRoleAssignmentCommand(Guid AssignmentId) : SyncCommand;

public sealed record AddForceUnitCommand(
    OperatorDto Operator, string Brigade, int PersonnelCount,
    string? CallSign, string? Status, string? Notes, int ScbaCount) : SyncCommand;

public sealed record UpdateForceUnitCommand(
    OperatorDto Operator, Guid UnitId, string? Status, string? Notes) : SyncCommand;

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
