using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Files;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Sync;

/// <summary>
/// Applies a received <see cref="SyncCommand"/> to the host's authoritative <see cref="Incident"/>.
/// The inverse of the ViewModel → command mapping: each command invokes the matching domain
/// mutation, stamped with the <b>host's</b> clock (authoritative) and the <b>command's</b> operator
/// (per-device attribution, §6) where the domain method takes one. The same domain guards that
/// protect a local edit protect a remote one — e.g. a command against a closed incident throws
/// <see cref="IncidentClosedException"/>, exactly as a local mutation would.
/// </summary>
public static class CommandApplier
{
    /// <param name="command">The received command to apply.</param>
    /// <param name="incident">The host's authoritative aggregate to mutate.</param>
    /// <param name="clock">The host's clock — authoritative timestamps for every applied command.</param>
    /// <returns>
    /// The newly recorded <see cref="IncidentFile"/> for an <see cref="AddFileCommand"/>, or the
    /// just-removed one for a <see cref="RemoveFileCommand"/> — both informational, since the
    /// attachment's bytes never travel through either command (issue #167 P1 #2: they ride
    /// <see cref="SyncProtocol.FilesPath"/> as a separate request, keyed by the id the command
    /// already carries). Callers use the returned <see cref="IncidentFile.FileName"/> to add or
    /// delete the bytes on disk. Null for every other command.
    /// </returns>
    public static IncidentFile? Apply(SyncCommand command, Incident incident, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(clock);

        switch (command)
        {
            case AddJournalEntryCommand c:
                incident.AddJournalEntry(clock, Operator(c.Operator), c.Direction, c.Text, c.From, c.To, c.EntryId);
                break;
            case EditJournalEntryCommand c:
                incident.EditJournalEntry(clock, Operator(c.Operator), c.EntryId, c.Text);
                break;
            case ChangeOperatorCommand c:
                incident.ChangeOperator(clock, Operator(c.From), Operator(c.To));
                break;
            case ToggleChecklistItemCommand c:
                incident.ToggleChecklistItem(clock, Operator(c.Operator), c.ItemId);
                break;
            case AssignRoleCommand c:
                incident.AssignRole(clock, Operator(c.Operator), c.Role, c.PersonName, c.CallSign, c.From, c.To, c.Section, c.Phone, c.NodeId, c.Slot);
                break;
            case TransferRoleCommand c:
                incident.TransferRole(clock, Operator(c.Operator), c.AssignmentId, c.NewPersonName, c.NewCallSign, c.NewPhone);
                break;
            case EditRolePhoneCommand c:
                incident.EditRolePhone(clock, Operator(c.Operator), c.AssignmentId, c.Phone);
                break;
            case EditRoleAssignmentCommand c:
                incident.EditRoleAssignment(clock, Operator(c.Operator), c.AssignmentId, c.PersonName, c.CallSign, c.Phone, c.Section);
                break;
            case AddCommandNodeCommand c:
                incident.AddCommandNode(
                    clock,
                    Operator(c.Operator),
                    c.Kind,
                    c.Name,
                    c.ParentId,
                    c.Auftrag,
                    c.Standort,
                    c.Funkgruppe,
                    c.Rufname,
                    c.Telefon);
                break;
            case UpdateCommandNodeCommand c:
                incident.UpdateCommandNode(clock, Operator(c.Operator), c.NodeId, c.Name, c.Auftrag, c.Standort, c.Funkgruppe, c.Rufname, c.Telefon);
                break;
            case DissolveCommandNodeCommand c:
                incident.DissolveCommandNode(clock, Operator(c.Operator), c.NodeId);
                break;
            case PlaceRoleAssignmentCommand c:
                incident.PlaceRoleAssignment(clock, Operator(c.Operator), c.AssignmentId, c.NodeId, c.Slot);
                break;
            case MoveCommandNodeCommand c:
                incident.MoveCommandNode(clock, Operator(c.Operator), c.NodeId, c.ParentId, c.BeforeNodeId);
                break;
            case MoveForceUnitCommand c:
                incident.MoveForceUnit(clock, Operator(c.Operator), c.UnitId, c.NodeId);
                break;
            case SetIncidentLocationCommand c:
                incident.SetLocation(clock, Operator(c.Operator), Position(c.Latitude, c.Longitude));
                break;
            case PlaceForceUnitCommand c:
                incident.PlaceForceUnit(clock, Operator(c.Operator), c.UnitId, Position(c.Latitude, c.Longitude));
                break;
            case SetFuehrungsstufeCommand c:
                incident.SetFuehrungsstufe(clock, Operator(c.Operator), c.Stufe);
                break;
            case SetNavVisibilityCommand c:
                incident.SetNavVisibility(c.Key, c.Visible);
                break;
            case AddChecklistCommand c:
                incident.AddChecklist(
                    clock,
                    Operator(c.Operator),
                    new ChecklistSeed(c.TemplateId, c.Title, ToSeedItems(c.Items)));
                break;
            case UpdateLeadershipPostCommand c:
                incident.UpdateLeadershipPost(clock, Operator(c.Operator), c.Standort, c.Rufname, c.Telefon, c.Funkgruppe);
                break;
            case AddForceUnitCommand c:
                incident.AddForceUnit(
                    clock,
                    Operator(c.Operator),
                    c.Brigade,
                    c.PersonnelCount,
                    c.CallSign,
                    c.Status,
                    c.Notes,
                    c.ScbaCount,
                    c.OfficerCount,
                    c.ZugfuehrerCount);
                break;
            case UpdateForceUnitCommand c:
                incident.UpdateForceUnit(clock, Operator(c.Operator), c.UnitId, c.Status, c.Notes);
                break;
            case UpdateForceStrengthCommand c:
                incident.UpdateForceStrength(
                    clock,
                    Operator(c.Operator),
                    c.UnitId,
                    c.OfficerCount,
                    c.PersonnelCount,
                    c.ScbaCount,
                    c.ZugfuehrerCount);
                break;
            case RemoveForceUnitCommand c:
                incident.RemoveForceUnit(clock, Operator(c.Operator), c.UnitId);
                break;
            case AddScbaTruppCommand c:
                incident.AddScbaTrupp(
                    clock,
                    c.Designation,
                    c.Members.Select(m => new TruppMember(m.Role, m.Name)),
                    c.EntryPressure,
                    c.TruppNumber,
                    c.CallSign,
                    c.Task,
                    c.MaxDurationMinutes,
                    c.ReturnPressureBar,
                    c.PressureControlIntervalMinutes);
                break;
            case StartScbaTruppCommand c:
                incident.StartScbaTrupp(clock, c.TruppId);
                break;
            case RecordScbaPressureCommand c:
                incident.RecordScbaPressure(clock, c.TruppId, c.Bar, c.Location);
                break;
            case WithdrawScbaTruppCommand c:
                incident.WithdrawScbaTrupp(clock, c.TruppId);
                break;
            case MarkScbaRemovedCommand c:
                incident.MarkScbaRemoved(clock, c.TruppId);
                break;
            case SetScbaSafetyTruppCommand c:
                incident.SetScbaSafetyTrupp(c.TruppId, c.SafetyTruppId);
                break;
            case SetIncidentNumberCommand c:
                incident.SetIncidentNumber(c.IncidentNumber is { } n ? new IncidentNumber(n) : null);
                break;
            case SetKeywordCommand c:
                incident.SetKeyword(c.Keyword);
                break;
            case SetAddressCommand c:
                incident.SetAddress(c.Street, c.District, c.HouseNumber, c.PostalCode, c.Place);
                break;
            case SetStatusCommand c:
                incident.SetStatus(c.Status);
                break;
            case CloseIncidentCommand c:
                incident.Close(clock, Operator(c.Operator));
                break;
            case AddFileCommand c:
                return incident.AddFile(clock, Operator(c.Operator), c.FileId, c.FileName, c.ContentType, c.SizeBytes);
            case RenameFileCommand c:
                incident.RenameFile(c.FileId, c.DisplayName);
                break;
            case RemoveFileCommand c:
                return incident.RemoveFile(clock, Operator(c.Operator), c.FileId);
            case AddTaskCommand c:
                incident.AddTask(
                    clock,
                    Operator(c.Operator),
                    c.Text,
                    c.Assignee,
                    c.Importance,
                    c.Urgency,
                    c.TimerMinutes,
                    c.SourceEntryId);
                break;
            case SetTaskCompletedCommand c:
                incident.SetTaskCompleted(c.TaskId, c.IsDone, clock, Operator(c.Operator));
                break;
            case AddInvolvedPartyCommand c:
                incident.AddInvolvedParty(clock, Operator(c.Operator), c.Name, c.Phone, c.Notes);
                break;
            case UpdateInvolvedPartyCommand c:
                incident.UpdateInvolvedParty(c.PartyId, c.Name, c.Phone, c.Notes);
                break;
            case RemoveInvolvedPartyCommand c:
                incident.RemoveInvolvedParty(c.PartyId);
                break;
            case SetInvolvedPartyDwellingCommand c:
                incident.SetInvolvedPartyDwelling(c.PartyId, c.DwellingId);
                break;
            case SetInvolvedPartyWhereaboutsCommand c:
                incident.SetInvolvedPartyWhereabouts(clock, c.PartyId, c.Whereabouts);
                break;
            case AddSituationReportCommand c:
                incident.AddSituationReport(
                    clock, Operator(c.Operator), c.Reporter, c.Location, c.Situation, c.Measures, c.Units, c.Requests);
                break;
            case UpdateSituationReportCommand c:
                incident.UpdateSituationReport(c.ReportId, c.Reporter, c.Location, c.Situation, c.Measures, c.Units, c.Requests);
                break;
            case RemoveSituationReportCommand c:
                incident.RemoveSituationReport(c.ReportId);
                break;
            case SendSituationReportCommand c:
                incident.SendSituationReport(clock, Operator(c.Operator), c.ReportId, c.DispatchCentreName);
                break;
            case IssueItemCommand c:
                incident.IssueItem(clock, Operator(c.Operator), c.Device, c.IssuedTo, c.IssuedToPhone, c.IssuedToDetails, c.Remark);
                break;
            case UpdateIssuedItemCommand c:
                incident.UpdateIssuedItem(c.ItemId, c.Device, c.IssuedTo, c.IssuedToPhone, c.IssuedToDetails, c.Remark);
                break;
            case ReturnIssuedItemCommand c:
                incident.ReturnIssuedItem(clock, Operator(c.Operator), c.ItemId);
                break;
            case UndoIssuedItemReturnCommand c:
                incident.UndoIssuedItemReturn(c.ItemId);
                break;
            case RemoveIssuedItemCommand c:
                incident.RemoveIssuedItem(c.ItemId);
                break;
            case UpdateTaskCommand c:
                incident.UpdateTask(c.TaskId, c.Text, c.Assignee, c.Importance, c.Urgency);
                break;
            case ExtendTaskTimerCommand c:
                incident.ExtendTaskTimer(c.TaskId, c.Minutes, clock);
                break;
            case AddCoBuildingCommand c:
                incident.AddCoBuilding(clock, Operator(c.Operator), c.Name, c.FloorCount, c.ApartmentsPerFloor, c.UndergroundFloorCount, c.ApartmentLabels);
                break;
            case UpdateCoBuildingStructureCommand c:
                incident.UpdateCoBuildingStructure(clock, Operator(c.Operator), c.BuildingId, c.FloorCount, c.ApartmentsPerFloor, c.UndergroundFloorCount);
                break;
            case RemoveCoBuildingCommand c:
                incident.RemoveCoBuilding(clock, Operator(c.Operator), c.BuildingId);
                break;
            case RecordCoValueCommand c:
                incident.RecordCoValue(clock, Operator(c.Operator), c.BuildingId, c.FloorOrdinal, c.ApartmentNumber, c.CoValue);
                break;
            case SetDwellingStatusCommand c:
                incident.SetDwellingStatus(clock, Operator(c.Operator), c.BuildingId, c.FloorOrdinal, c.ApartmentNumber, c.Status);
                break;
            case UpdateDwellingDetailsCommand c:
                incident.SetDwellingDetails(c.BuildingId, c.FloorOrdinal, c.ApartmentNumber, c.ResidentName, KeyAvailabilityEncoding.FromNullableBool(c.KeyAvailable), c.KeyNumber);
                break;
            case SetFloorDescriptionCommand c:
                incident.SetFloorDescription(c.BuildingId, c.FloorOrdinal, c.Description);
                break;
            case SetApartmentLabelCommand c:
                incident.SetApartmentLabel(c.BuildingId, c.FloorOrdinal, c.ApartmentNumber, c.Label);
                break;
            case SetApartmentCountCommand c:
                incident.SetApartmentCount(clock, Operator(c.Operator), c.BuildingId, c.FloorOrdinal, c.Count);
                break;
            case RemoveDwellingsCommand c:
                incident.RemoveDwellings(clock, Operator(c.Operator), c.BuildingId, c.FloorOrdinal, c.ApartmentNumbers);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    $"Unbekannter Befehl: {command.GetType().Name}");
        }

        return null;
    }

    // The body is a peer's JSON, which is not nullable-checked: a null list or element becomes an
    // ArgumentException (a clean 400) instead of a NullReferenceException (a 500).
    private static List<(string Text, bool IsMandatory)> ToSeedItems(IReadOnlyList<ChecklistSeedItemDto>? items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Select(i => i is null
                ? throw new ArgumentException("Ein Checklistenpunkt fehlt.", nameof(items))
                : (i.Text, i.IsMandatory))
            .ToList();
    }

    private static SessionOperator Operator(OperatorDto dto) => new(dto.Name, dto.CallSign);

    // Both null takes a mark off the Lagekarte. Anything else has to be a position on the globe: a
    // half-given or impossible coordinate is refused like any other bad command, never read as "off".
    private static GeoPoint? Position(double? latitude, double? longitude) =>
        latitude is null && longitude is null
            ? null
            : GeoPoint.TryCreate(latitude, longitude)
                ?? throw new ArgumentException("Ungültige Koordinaten.", nameof(latitude));
}
