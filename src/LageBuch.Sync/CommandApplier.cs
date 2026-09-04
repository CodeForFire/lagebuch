using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
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
    /// The newly recorded <see cref="IncidentFile"/> for an <see cref="AddFileCommand"/> — informational
    /// only, since the attachment's bytes never travel through this command (issue #167 P1 #2: they
    /// PUT to <see cref="SyncProtocol.FilesPath"/> as a separate request, keyed by the id the command
    /// already carries). Null for every other command.
    /// </returns>
    public static IncidentFile? Apply(SyncCommand command, Incident incident, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(clock);

        switch (command)
        {
            case AddJournalEntryCommand c:
                incident.AddJournalEntry(clock, Operator(c.Operator), c.Direction, c.Text, c.From, c.To);
                break;
            case EditJournalEntryCommand c:
                incident.EditJournalEntry(clock, Operator(c.Operator), c.EntryId, c.Text);
                break;
            case ToggleChecklistItemCommand c:
                incident.ToggleChecklistItem(clock, Operator(c.Operator), c.ItemId);
                break;
            case AssignRoleCommand c:
                incident.AssignRole(clock, Operator(c.Operator), c.Role, c.PersonName, c.CallSign, c.From, c.To, c.Section, c.Phone);
                break;
            case TransferRoleCommand c:
                incident.TransferRole(clock, Operator(c.Operator), c.AssignmentId, c.NewPersonName, c.NewCallSign, c.NewPhone);
                break;
            case EditRolePhoneCommand c:
                incident.EditRolePhone(clock, Operator(c.Operator), c.AssignmentId, c.Phone);
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
                    c.OfficerCount);
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
                    c.ScbaCount);
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
                incident.RecordScbaPressure(clock, c.TruppId, c.Bar);
                break;
            case WithdrawScbaTruppCommand c:
                incident.WithdrawScbaTrupp(clock, c.TruppId);
                break;
            case MarkScbaRemovedCommand c:
                incident.MarkScbaRemoved(clock, c.TruppId);
                break;
            case SetIncidentNumberCommand c:
                incident.SetIncidentNumber(c.IncidentNumber is { } n ? new IncidentNumber(n) : null);
                break;
            case SetKeywordCommand c:
                incident.SetKeyword(c.Keyword);
                break;
            case SetAddressCommand c:
                incident.SetAddress(c.Street, c.District);
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
            case AddTaskCommand c:
                incident.AddTask(
                    clock,
                    Operator(c.Operator),
                    c.Text,
                    c.Assignee,
                    c.Importance,
                    c.Urgency,
                    c.TimerMinutes);
                break;
            case SetTaskCompletedCommand c:
                incident.SetTaskCompleted(c.TaskId, c.IsDone, clock, Operator(c.Operator));
                break;
            case AddCoBuildingCommand c:
                incident.AddCoBuilding(clock, Operator(c.Operator), c.Name, c.FloorCount, c.ApartmentsPerFloor);
                break;
            case UpdateCoBuildingStructureCommand c:
                incident.UpdateCoBuildingStructure(clock, Operator(c.Operator), c.BuildingId, c.FloorCount, c.ApartmentsPerFloor);
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
                incident.SetDwellingDetails(c.BuildingId, c.FloorOrdinal, c.ApartmentNumber, c.ResidentName, c.KeyAvailable);
                break;
            case SetFloorDescriptionCommand c:
                incident.SetFloorDescription(c.BuildingId, c.FloorOrdinal, c.Description);
                break;
            case SetApartmentLabelCommand c:
                incident.SetApartmentLabel(c.BuildingId, c.ApartmentNumber, c.Label);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    $"Unbekannter Befehl: {command.GetType().Name}");
        }

        return null;
    }

    private static SessionOperator Operator(OperatorDto dto) => new(dto.Name, dto.CallSign);
}
