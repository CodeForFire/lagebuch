using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Sync;

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
    public static void Apply(SyncCommand command, Incident incident, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(clock);

        switch (command)
        {
            case AddJournalEntryCommand c:
                incident.AddJournalEntry(clock, Operator(c.Operator), c.Direction, c.Text, c.From, c.To);
                break;
            case ToggleChecklistItemCommand c:
                incident.ToggleChecklistItem(c.ItemId);
                break;
            case AssignRoleCommand c:
                incident.AssignRole(c.Role, c.PersonName, c.CallSign, c.From, c.To, c.Section, c.Phone);
                break;
            case EndRoleAssignmentCommand c:
                incident.EndRoleAssignment(c.AssignmentId, clock.Now);
                break;
            case AddForceUnitCommand c:
                incident.AddForceUnit(clock, Operator(c.Operator), c.Brigade, c.PersonnelCount,
                    c.CallSign, c.Status, c.Notes, c.ScbaCount);
                break;
            case UpdateForceUnitCommand c:
                incident.UpdateForceUnit(clock, Operator(c.Operator), c.UnitId, c.Status, c.Notes);
                break;
            case AddScbaTruppCommand c:
                incident.AddScbaTrupp(clock, c.Designation,
                    c.Members.Select(m => new TruppMember(m.Role, m.Name)),
                    c.CallSign, c.Task, c.MaxDurationMinutes, c.ReturnPressureBar, c.PressureControlIntervalMinutes);
                break;
            case StartScbaTruppCommand c:
                incident.StartScbaTrupp(clock, c.TruppId, c.StartPressure);
                break;
            case RecordScbaPressureCommand c:
                incident.RecordScbaPressure(clock, c.TruppId, c.Bar);
                break;
            case MarkScbaReturnedCommand c:
                incident.MarkScbaReturned(clock, c.TruppId);
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
            default:
                throw new ArgumentOutOfRangeException(nameof(command),
                    $"Unbekannter Befehl: {command.GetType().Name}");
        }
    }

    private static SessionOperator Operator(OperatorDto dto) => new(dto.Name, dto.CallSign);
}
