using System.Text.Json.Serialization;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;

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
[JsonDerivedType(typeof(EditRoleAssignmentCommand), "editRoleAssignment")]
[JsonDerivedType(typeof(AddCommandNodeCommand), "addCommandNode")]
[JsonDerivedType(typeof(UpdateCommandNodeCommand), "updateCommandNode")]
[JsonDerivedType(typeof(DissolveCommandNodeCommand), "dissolveCommandNode")]
[JsonDerivedType(typeof(MoveCommandNodeCommand), "moveCommandNode")]
[JsonDerivedType(typeof(MoveForceUnitCommand), "moveForceUnit")]
[JsonDerivedType(typeof(SetIncidentLocationCommand), "setIncidentLocation")]
[JsonDerivedType(typeof(PlaceForceUnitCommand), "placeForceUnit")]
[JsonDerivedType(typeof(SetForceUnitVehicleTypeCommand), "setForceUnitVehicleType")]
[JsonDerivedType(typeof(MoveForceUnitLabelCommand), "moveForceUnitLabel")]
[JsonDerivedType(typeof(PlaceRoleAssignmentCommand), "placeRoleAssignment")]
[JsonDerivedType(typeof(SetFuehrungsstufeCommand), "setFuehrungsstufe")]
[JsonDerivedType(typeof(SetNavVisibilityCommand), "setNavVisibility")]
[JsonDerivedType(typeof(AddChecklistCommand), "addChecklist")]
[JsonDerivedType(typeof(UpdateLeadershipPostCommand), "updateLeadershipPost")]
[JsonDerivedType(typeof(AddForceUnitCommand), "addForceUnit")]
[JsonDerivedType(typeof(UpdateForceUnitCommand), "updateForceUnit")]
[JsonDerivedType(typeof(UpdateForceStrengthCommand), "updateForceStrength")]
[JsonDerivedType(typeof(RemoveForceUnitCommand), "removeForceUnit")]
[JsonDerivedType(typeof(AddScbaTruppCommand), "addScbaTrupp")]
[JsonDerivedType(typeof(StartScbaTruppCommand), "startScbaTrupp")]
[JsonDerivedType(typeof(RecordScbaPressureCommand), "recordScbaPressure")]
[JsonDerivedType(typeof(WithdrawScbaTruppCommand), "withdrawScbaTrupp")]
[JsonDerivedType(typeof(MarkScbaRemovedCommand), "markScbaRemoved")]
[JsonDerivedType(typeof(SetScbaSafetyTruppCommand), "setScbaSafetyTrupp")]
[JsonDerivedType(typeof(SetIncidentNumberCommand), "setIncidentNumber")]
[JsonDerivedType(typeof(SetKeywordCommand), "setKeyword")]
[JsonDerivedType(typeof(SetAddressCommand), "setAddress")]
[JsonDerivedType(typeof(SetStatusCommand), "setStatus")]
[JsonDerivedType(typeof(CloseIncidentCommand), "close")]
[JsonDerivedType(typeof(ChangeOperatorCommand), "changeOperator")]
[JsonDerivedType(typeof(AddFileCommand), "addFile")]
[JsonDerivedType(typeof(RenameFileCommand), "renameFile")]
[JsonDerivedType(typeof(RemoveFileCommand), "removeFile")]
[JsonDerivedType(typeof(AddTaskCommand), "addTask")]
[JsonDerivedType(typeof(SetTaskCompletedCommand), "setTaskCompleted")]
[JsonDerivedType(typeof(UpdateTaskCommand), "updateTask")]
[JsonDerivedType(typeof(ExtendTaskTimerCommand), "extendTaskTimer")]
[JsonDerivedType(typeof(AddCoBuildingCommand), "addCoBuilding")]
[JsonDerivedType(typeof(UpdateCoBuildingStructureCommand), "updateCoBuildingStructure")]
[JsonDerivedType(typeof(RemoveCoBuildingCommand), "removeCoBuilding")]
[JsonDerivedType(typeof(RecordCoValueCommand), "recordCoValue")]
[JsonDerivedType(typeof(SetDwellingStatusCommand), "setDwellingStatus")]
[JsonDerivedType(typeof(UpdateDwellingDetailsCommand), "updateDwellingDetails")]
[JsonDerivedType(typeof(SetFloorDescriptionCommand), "setFloorDescription")]
[JsonDerivedType(typeof(SetApartmentLabelCommand), "setApartmentLabel")]
[JsonDerivedType(typeof(SetApartmentCountCommand), "setApartmentCount")]
[JsonDerivedType(typeof(RemoveDwellingsCommand), "removeDwellings")]
[JsonDerivedType(typeof(AddInvolvedPartyCommand), "addInvolvedParty")]
[JsonDerivedType(typeof(UpdateInvolvedPartyCommand), "updateInvolvedParty")]
[JsonDerivedType(typeof(RemoveInvolvedPartyCommand), "removeInvolvedParty")]
[JsonDerivedType(typeof(SetInvolvedPartyDwellingCommand), "setInvolvedPartyDwelling")]
[JsonDerivedType(typeof(SetInvolvedPartyWhereaboutsCommand), "setInvolvedPartyWhereabouts")]
[JsonDerivedType(typeof(AddSituationReportCommand), "addSituationReport")]
[JsonDerivedType(typeof(UpdateSituationReportCommand), "updateSituationReport")]
[JsonDerivedType(typeof(RemoveSituationReportCommand), "removeSituationReport")]
[JsonDerivedType(typeof(SendSituationReportCommand), "sendSituationReport")]
[JsonDerivedType(typeof(IssueItemCommand), "issueItem")]
[JsonDerivedType(typeof(UpdateIssuedItemCommand), "updateIssuedItem")]
[JsonDerivedType(typeof(ReturnIssuedItemCommand), "returnIssuedItem")]
[JsonDerivedType(typeof(UndoIssuedItemReturnCommand), "undoIssuedItemReturn")]
[JsonDerivedType(typeof(RemoveIssuedItemCommand), "removeIssuedItem")]
public abstract record SyncCommand;

/// <summary>The operator at the sending device — carried on attributed mutations (see §6).</summary>
public sealed record OperatorDto(string Name, string? CallSign);

// EntryId is minted by the sending device so it can link an Aufgabe to the entry at once; the
// host honours it unless it is taken. Defaulted, so a payload from an older client still reads.
public sealed record AddJournalEntryCommand(
    OperatorDto Operator, EtbDirection Direction, string Text, string? From, string? To, Guid? EntryId = null) : SyncCommand;

public sealed record EditJournalEntryCommand(OperatorDto Operator, Guid EntryId, string Text) : SyncCommand;

/// <summary>A new Lagebuchführer takes over at the sending device (#469). Only the ETB line and
/// the audit event need the host: every later command already carries the new operator.</summary>
public sealed record ChangeOperatorCommand(OperatorDto From, OperatorDto To) : SyncCommand;

public sealed record ToggleChecklistItemCommand(OperatorDto Operator, Guid ItemId) : SyncCommand;

public sealed record AssignRoleCommand(
    OperatorDto Operator, string Role, string PersonName, string? CallSign,
    DateTimeOffset? From, DateTimeOffset? To, string? Section, string? Phone,

    // Appended with defaults: a command from a client that predates the Führungsorganisation
    // reads as an unplaced Funktion.
    Guid? NodeId = null, RoleSlot Slot = RoleSlot.None) : SyncCommand;

// No end-time on the wire: the host stamps it with its own (authoritative) clock, so devices with
// slightly different clocks can't disagree on when a handover happened.
public sealed record TransferRoleCommand(
    OperatorDto Operator, Guid AssignmentId, string NewPersonName, string? NewCallSign, string? NewPhone) : SyncCommand;

public sealed record EditRolePhoneCommand(OperatorDto Operator, Guid AssignmentId, string? Phone) : SyncCommand;

// The whole corrected state rather than a diff, as updateDwellingDetails does: the host compares it
// with what it holds and logs only what changed.
public sealed record EditRoleAssignmentCommand(
    OperatorDto Operator, Guid AssignmentId, string PersonName, string? CallSign, string? Phone, string? Section) : SyncCommand;

/// <summary>Forms an Abschnitt, Unterabschnitt, Bereitstellung or partner Einsatzleitung.</summary>
public sealed record AddCommandNodeCommand(
    OperatorDto Operator,
    CommandNodeKind Kind,
    string Name,
    Guid? ParentId,
    string? Auftrag = null,
    string? Standort = null,
    string? Funkgruppe = null,
    string? Rufname = null,
    string? Telefon = null,
    IReadOnlyList<string>? FunkgruppenTmo = null,
    IReadOnlyList<string>? FunkgruppenDmo = null) : SyncCommand;

// The whole corrected state: absent Funkgruppen lists mean none, as an absent Standort does.
public sealed record UpdateCommandNodeCommand(
    OperatorDto Operator, Guid NodeId, string Name, string? Auftrag, string? Standort,
    string? Funkgruppe, string? Rufname, string? Telefon,
    IReadOnlyList<string>? FunkgruppenTmo = null, IReadOnlyList<string>? FunkgruppenDmo = null) : SyncCommand;

public sealed record DissolveCommandNodeCommand(OperatorDto Operator, Guid NodeId) : SyncCommand;

/// <summary>Moves a node under ParentId (null: the top level), before BeforeNodeId (null: at the end).</summary>
public sealed record MoveCommandNodeCommand(OperatorDto Operator, Guid NodeId, Guid? ParentId, Guid? BeforeNodeId) : SyncCommand;

/// <summary>Places a unit in a node, or back to "nicht zugeordnet" when NodeId is null.</summary>
public sealed record MoveForceUnitCommand(OperatorDto Operator, Guid UnitId, Guid? NodeId) : SyncCommand;

/// <summary>Marks the Einsatzort on the Lagekarte; both coordinates null takes the mark off.</summary>
public sealed record SetIncidentLocationCommand(OperatorDto Operator, double? Latitude, double? Longitude) : SyncCommand;

/// <summary>Puts a unit on the Lagekarte; both coordinates null takes it off.</summary>
public sealed record PlaceForceUnitCommand(OperatorDto Operator, Guid UnitId, double? Latitude, double? Longitude) : SyncCommand;

// Protocol 30: the taktisches Zeichen. A null VehicleType clears it; both offsets null put the
// sign back at its default place.
public sealed record SetForceUnitVehicleTypeCommand(OperatorDto Operator, Guid UnitId, string? VehicleType) : SyncCommand;

public sealed record MoveForceUnitLabelCommand(OperatorDto Operator, Guid UnitId, double? Dx, double? Dy) : SyncCommand;

/// <summary>Places a running Funktion in a node and board slot; a null NodeId is the Einsatzleitung.</summary>
public sealed record PlaceRoleAssignmentCommand(OperatorDto Operator, Guid AssignmentId, Guid? NodeId, RoleSlot Slot) : SyncCommand;

/// <summary>Sets the Führungsstufe; a null Stufe clears it.</summary>
public sealed record SetFuehrungsstufeCommand(OperatorDto Operator, Fuehrungsstufe? Stufe) : SyncCommand;

/// <summary>Hides or shows one rail entry for this Einsatz; see <c>Incident.SetNavVisibility</c>.</summary>
public sealed record SetNavVisibilityCommand(OperatorDto Operator, string Key, bool Visible) : SyncCommand;

/// <summary>Adds a Checkliste from a Vorlage mid-Einsatz; the Vorlage's text travels with it.</summary>
public sealed record AddChecklistCommand(
    OperatorDto Operator, Guid TemplateId, string Title, IReadOnlyList<ChecklistSeedItemDto> Items) : SyncCommand;

public sealed record ChecklistSeedItemDto(string Text, bool IsMandatory);

/// <summary>The Einsatzleitung's whole post; absent Funkgruppen lists mean none.</summary>
public sealed record UpdateLeadershipPostCommand(
    OperatorDto Operator, string? Standort, string? Rufname, string? Telefon,
    IReadOnlyList<string>? FunkgruppenTmo, IReadOnlyList<string>? FunkgruppenDmo) : SyncCommand;

// OfficerCount/ZugfuehrerCount default to 0 so a pre-#76/#216 payload (no such property on the
// wire) deserializes as "keine Führungskraft/kein ZF erfasst" instead of failing the contract.
public sealed record AddForceUnitCommand(
    OperatorDto Operator, string Brigade, int PersonnelCount,
    string? CallSign, string? Status, string? Notes, int ScbaCount, int OfficerCount = 0,
    int ZugfuehrerCount = 0, string? VehicleType = null) : SyncCommand;

public sealed record UpdateForceUnitCommand(
    OperatorDto Operator, Guid UnitId, string? Status, string? Notes) : SyncCommand;

public sealed record UpdateForceStrengthCommand(
    OperatorDto Operator, Guid UnitId, int OfficerCount, int PersonnelCount, int ScbaCount,
    int ZugfuehrerCount = 0) : SyncCommand;

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

// Location is optional on the wire (protocol 19): an older host ignores it and records the bar alone.
public sealed record RecordScbaPressureCommand(Guid TruppId, int Bar, string? Location = null) : SyncCommand;

/// <summary>Rückzug — the Trupp begins its withdrawal, still under air.</summary>
public sealed record WithdrawScbaTruppCommand(Guid TruppId) : SyncCommand;

/// <summary>Abgenommen — replaces the old "markScbaReturned"/Zurück command (#78).</summary>
public sealed record MarkScbaRemovedCommand(Guid TruppId) : SyncCommand;

/// <summary>Designates the Sicherheitstrupp standing by for a Trupp (#399); a null SafetyTruppId
/// clears it. Carries no operator: the ETB line is written by the sending device through
/// AddJournalEntry, exactly like every other Atemschutz mutation.</summary>
public sealed record SetScbaSafetyTruppCommand(Guid TruppId, Guid? SafetyTruppId) : SyncCommand;

public sealed record SetIncidentNumberCommand(string? IncidentNumber) : SyncCommand;

public sealed record SetKeywordCommand(string? Keyword) : SyncCommand;

/// <summary>The whole address at once (#401); a part left out is cleared, the way the dialog saves it.</summary>
public sealed record SetAddressCommand(
    string? Street,
    string? District,
    string? HouseNumber = null,
    string? PostalCode = null,
    string? Place = null) : SyncCommand;

public sealed record SetStatusCommand(string? Status) : SyncCommand;

public sealed record CloseIncidentCommand(OperatorDto Operator) : SyncCommand;

// Metadata only (issue #167 P1 #2) — the client generates FileId up front and PUTs the raw bytes to
// SyncProtocol.FilesPath(FileId) as a separate request, so a multi-MB attachment never rides this
// JSON command (previously base64-inflating it ~33% inside the whole-command payload). This command
// just registers the file's existence/metadata on the domain; the bytes land on disk independently.
public sealed record AddFileCommand(OperatorDto Operator, Guid FileId, string FileName, string ContentType, long SizeBytes) : SyncCommand;

// No operator on the wire: renaming is a silent label correction (no ETB entry), unlike every
// attributed command above.
public sealed record RenameFileCommand(Guid FileId, string? DisplayName) : SyncCommand;

// Attributed (unlike RenameFileCommand): removal logs an ETB entry, so the operator travels with it.
public sealed record RemoveFileCommand(OperatorDto Operator, Guid FileId) : SyncCommand;

// TimerMinutes travels instead of an absolute DueAt: the host stamps the anchor with its own
// authoritative clock on apply, like every timestamped command.
// SourceEntryId: the ETB entry the Aufgabe was given out from. Defaulted for older clients; the
// host keeps any non-empty id, since the entry's own command may still be on its way.
public sealed record AddTaskCommand(
    OperatorDto Operator, string Text, string Assignee,
    TaskImportance Importance, TaskUrgency Urgency, int TimerMinutes, Guid? SourceEntryId = null) : SyncCommand;

public sealed record SetTaskCompletedCommand(OperatorDto Operator, Guid TaskId, bool IsDone) : SyncCommand;

// The Beteiligte. Only the add carries an operator, for the entry's CreatedBy stamp; a correction
// or removal writes no ETB line and records no author, like RenameFileCommand. The domain caps
// every field, so an oversized value from a peer is refused on the host.
public sealed record AddInvolvedPartyCommand(OperatorDto Operator, string Name, string? Phone, string? Notes) : SyncCommand;

public sealed record UpdateInvolvedPartyCommand(Guid PartyId, string Name, string? Phone, string? Notes) : SyncCommand;

public sealed record RemoveInvolvedPartyCommand(Guid PartyId) : SyncCommand;

/// <summary>The Wohnung a Beteiligte/r was found in (#702); a null DwellingId clears it. Ids only:
/// the host copies the Wohnung's location itself, so no peer can put a label of its own there.</summary>
public sealed record SetInvolvedPartyDwellingCommand(Guid PartyId, Guid? DwellingId) : SyncCommand;

/// <summary>Where a Beteiligte/r is now; a null or blank Whereabouts clears it. No time on the
/// wire: the host stamps it with its own clock, as it does an ETB entry.</summary>
public sealed record SetInvolvedPartyWhereaboutsCommand(Guid PartyId, string? Whereabouts) : SyncCommand;

// The Lagemeldungen (#592). A draft's correction or discard writes no ETB line and records no
// author, like the Beteiligte; the send carries the operator because it writes the ETB entry, and
// the Leitstelle's name because that comes from the sender's Stammdaten. The domain caps every
// field, so an oversized value from a peer is refused on the host.
public sealed record AddSituationReportCommand(
    OperatorDto Operator,
    string? Reporter,
    string? Location,
    string? Situation,
    string? Measures,
    string? Units,
    string? Requests) : SyncCommand;

public sealed record UpdateSituationReportCommand(
    Guid ReportId,
    string? Reporter,
    string? Location,
    string? Situation,
    string? Measures,
    string? Units,
    string? Requests) : SyncCommand;

public sealed record RemoveSituationReportCommand(Guid ReportId) : SyncCommand;

public sealed record SendSituationReportCommand(OperatorDto Operator, Guid ReportId, string DispatchCentreName) : SyncCommand;

// The Ausgabeliste. The issue and the return carry an operator, for the IssuedBy and ReturnedBy
// stamps; a correction, an undone return or a removal records no author, like the Beteiligte. No
// time on the wire: the host stamps both with its own clock. The domain caps every field, so an
// oversized value from a peer is refused on the host.
public sealed record IssueItemCommand(
    OperatorDto Operator,
    string Device,
    string IssuedTo,
    string? IssuedToPhone,
    string? IssuedToDetails,
    string? Remark) : SyncCommand;

public sealed record UpdateIssuedItemCommand(
    Guid ItemId,
    string Device,
    string IssuedTo,
    string? IssuedToPhone,
    string? IssuedToDetails,
    string? Remark) : SyncCommand;

public sealed record ReturnIssuedItemCommand(OperatorDto Operator, Guid ItemId) : SyncCommand;

public sealed record UndoIssuedItemReturnCommand(Guid ItemId) : SyncCommand;

public sealed record RemoveIssuedItemCommand(Guid ItemId) : SyncCommand;

// No operator on the wire: a task correction is a silent edit (no ETB entry), like RenameFileCommand.
public sealed record UpdateTaskCommand(
    Guid TaskId, string Text, string Assignee, TaskImportance Importance, TaskUrgency Urgency) : SyncCommand;

// No operator (silent) -- the "+5" quick action.
public sealed record ExtendTaskTimerCommand(Guid TaskId, int Minutes) : SyncCommand;

// UndergroundFloorCount defaults to 0 so a pre-#218 payload deserializes as "keine
// Untergeschosse" instead of failing the contract (same convention as #76's OfficerCount).
// ApartmentLabels are an Objekt's Wohnungsbezeichnungen (#420), optional for the same reason; the
// host keeps only those that fit the Haus (Building.Create).
public sealed record AddCoBuildingCommand(
    OperatorDto Operator,
    string Name,
    int FloorCount,
    int ApartmentsPerFloor,
    int UndergroundFloorCount = 0,
    IReadOnlyDictionary<string, string?>? ApartmentLabels = null) : SyncCommand;

public sealed record UpdateCoBuildingStructureCommand(
    OperatorDto Operator, Guid BuildingId, int FloorCount, int ApartmentsPerFloor, int UndergroundFloorCount = 0) : SyncCommand;

public sealed record RemoveCoBuildingCommand(
    OperatorDto Operator, Guid BuildingId) : SyncCommand;

public sealed record RecordCoValueCommand(
    OperatorDto Operator, Guid BuildingId, int FloorOrdinal, int ApartmentNumber, int? CoValue) : SyncCommand;

public sealed record SetDwellingStatusCommand(
    OperatorDto Operator, Guid BuildingId, int FloorOrdinal, int ApartmentNumber, DwellingStatus Status) : SyncCommand;

// No operator (silent)
public sealed record UpdateDwellingDetailsCommand(
    Guid BuildingId, int FloorOrdinal, int ApartmentNumber, string? ResidentName, bool? KeyAvailable,

    // Protocol 24. Defaulted so the record still deserializes without it, but a peer below 24 is
    // refused: its command would read as "no Schlüsselnummer" and wipe one (see SyncProtocol).
    string? KeyNumber = null) : SyncCommand;

// No operator (silent)
public sealed record SetFloorDescriptionCommand(
    Guid BuildingId, int FloorOrdinal, string? Description) : SyncCommand;

// No operator (silent)
public sealed record SetApartmentLabelCommand(
    Guid BuildingId, int FloorOrdinal, int ApartmentNumber, string? Label) : SyncCommand;

// #265: a floor's Wohnungen count, independent of the building's default -- attributed/logged
// like UpdateCoBuildingStructure, since it changes structure rather than just a label.
public sealed record SetApartmentCountCommand(
    OperatorDto Operator, Guid BuildingId, int FloorOrdinal, int Count) : SyncCommand;

// #419: which Wohnungen go, rather than how many are left -- the count alone can only ever
// mean "drop the ones on the right". Attributed like SetApartmentCount: it writes an ETB line.
public sealed record RemoveDwellingsCommand(
    OperatorDto Operator, Guid BuildingId, int FloorOrdinal, IReadOnlyList<int> ApartmentNumbers) : SyncCommand;
