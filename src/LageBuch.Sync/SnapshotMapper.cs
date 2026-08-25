using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Sync;

/// <summary>
/// Maps between the <see cref="Incident"/> aggregate and its JSON <see cref="IncidentSnapshot"/>.
/// The reconstruction path reuses the same domain factories (<see cref="Incident.Rehydrate"/> and
/// each child's <c>Rehydrate</c>/constructor) that <c>LageBuch.Persistence</c> uses to reload from
/// SQLite, so no new domain surface is introduced — only DTOs and this mapping.
/// </summary>
public static class SnapshotMapper
{
    public static IncidentSnapshot ToSnapshot(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return new IncidentSnapshot(
            incident.Id,
            incident.StartedAt,
            incident.State,
            incident.IncidentNumber?.Value,
            incident.Keyword,
            incident.Street,
            incident.District,
            incident.Status,
            incident.ClosedAt,
            incident.ClosedBy,
            incident.ChecklistAufbau.Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.Note, c.IsMandatory)).ToList(),
            incident.ChecklistAbbau.Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.Note, c.IsMandatory)).ToList(),
            incident.Journal.Select(e => new EtbEntryDto(e.Id, e.Timestamp, e.Direction, e.Text, e.EnteredBy, e.From, e.To,
                e.Edits.Select(x => new EtbEntryEditDto(x.PreviousText, x.EditedBy, x.EditedAt)).ToList())).ToList(),
            incident.Roles.Select(r => new RoleAssignmentDto(r.Id, r.Role, r.PersonName, r.CallSign, r.From, r.To, r.Section, r.Phone)).ToList(),
            incident.Forces.Select(f => new ForceUnitDto(f.Id, f.Brigade, f.CallSign, f.PersonnelCount, f.ScbaCount, f.Status, f.Notes,
                f.OfficerCount,
                f.Edits.Select(x => new ForceUnitStrengthEditDto(x.PreviousOfficerCount, x.PreviousPersonnelCount, x.PreviousScbaCount, x.EditedBy, x.EditedAt)).ToList())).ToList(),
            incident.ScbaTrupps.Select(ToDto).ToList(),
            incident.Audit.Select(a => new AuditEventDto(a.At, a.Action, a.By)).ToList(),
            incident.Timers.Select(t => new TimerDto(t.Key, t.CycleAnchor, t.IntervalMinutes, t.RecurringIntervalMinutes, t.IsRunning)).ToList(),
            incident.Files.Select(f => new IncidentFileDto(f.Id, f.FileName, f.DisplayName, f.ContentType, f.SizeBytes, f.AddedAt, f.AddedBy)).ToList(),
            incident.Tasks.Select(t => new TaskDto(t.Id, t.Text, t.Assignee, t.Importance, t.Urgency,
                t.CreatedBy, t.CreatedAt, t.DueAt, t.CompletedAt, t.CompletedBy)).ToList());
    }

    public static Incident FromSnapshot(IncidentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Incident.Rehydrate(
            snapshot.Id,
            snapshot.StartedAt,
            snapshot.State,
            snapshot.IncidentNumber is { } n ? new IncidentNumber(n) : null,
            snapshot.Keyword,
            snapshot.Street,
            snapshot.District,
            snapshot.Status,
            snapshot.ClosedAt,
            snapshot.ClosedBy,
            snapshot.ChecklistAufbau.Select(c => ChecklistItem.Rehydrate(c.Id, c.Text, c.IsDone, c.Note, c.IsMandatory)),
            snapshot.ChecklistAbbau.Select(c => ChecklistItem.Rehydrate(c.Id, c.Text, c.IsDone, c.Note, c.IsMandatory)),
            snapshot.Journal.Select(e => EtbEntry.Rehydrate(e.Id, e.Timestamp, e.Direction, e.Text, e.EnteredBy, e.From, e.To,
                e.Edits.Select(x => new EtbEntryEdit(x.PreviousText, x.EditedBy, x.EditedAt)))),
            snapshot.Roles.Select(r => new RoleAssignment(r.Id, r.Role, r.PersonName, r.CallSign, r.From, r.To, r.Section, r.Phone)),
            snapshot.Forces.Select(f => ForceUnit.Rehydrate(f.Id, f.Brigade, f.CallSign, f.PersonnelCount, f.ScbaCount, f.Status, f.Notes,
                f.OfficerCount,
                f.Edits.Select(x => new ForceUnitStrengthEdit(x.PreviousOfficerCount, x.PreviousPersonnelCount, x.PreviousScbaCount, x.EditedBy, x.EditedAt)))),
            snapshot.ScbaTrupps.Select(FromDto),
            snapshot.Audit.Select(a => new AuditEvent(a.At, a.Action, a.By)),
            snapshot.Timers.Select(t => new IncidentTimerState(t.Key, t.CycleAnchor, t.IntervalMinutes, t.RecurringIntervalMinutes, t.IsRunning)),
            snapshot.Files.Select(f => IncidentFile.Rehydrate(f.Id, f.FileName, f.DisplayName, f.ContentType, f.SizeBytes, f.AddedAt, f.AddedBy)),
            snapshot.Tasks.Select(t => IncidentTask.Rehydrate(t.Id, t.CreatedAt, t.Text, t.Assignee,
                t.Importance, t.Urgency, t.CreatedBy, t.DueAt, t.CompletedAt, t.CompletedBy)),
            Enumerable.Empty<Domain.CoMeasurement.Building>(),
            Enumerable.Empty<Domain.CoMeasurement.Dwelling>());
    }

    private static ScbaTruppDto ToDto(AtemschutzTrupp t) => new(
        t.Id,
        t.RegisteredAt,
        t.StartTime,
        t.Designation,
        t.Members.Select(m => new TruppMemberDto(m.Role, m.Name)).ToList(),
        t.CallSign,
        t.Task,
        t.StartPressure,
        t.MaxDurationMinutes,
        t.ReturnPressureBar,
        t.PressureControlIntervalMinutes,
        t.ExitTime,
        t.PressureReadings.Select(p => new PressureReadingDto(p.Time, p.Bar)).ToList());

    private static AtemschutzTrupp FromDto(ScbaTruppDto d) => AtemschutzTrupp.Rehydrate(
        d.Id,
        d.RegisteredAt,
        d.StartTime,
        d.Designation,
        d.Members.Select(m => new TruppMember(m.Role, m.Name)),
        d.CallSign,
        d.Task,
        d.StartPressure,
        d.MaxDurationMinutes,
        d.ReturnPressureBar,
        d.PressureControlIntervalMinutes,
        d.ExitTime,
        d.Readings.Select(p => new PressureReading(p.Time, p.Bar)));
}
