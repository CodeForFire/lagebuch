using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Domain;

public sealed record AuditEvent(DateTimeOffset At, string Action, string By);

public sealed class Incident
{
    private readonly List<ChecklistItem> _checklist = new();
    private readonly List<EtbEntry> _journal = new();
    private readonly List<RoleAssignment> _roles = new();
    private readonly List<ForceUnit> _forces = new();
    private readonly List<AtemschutzTrupp> _scbaTrupps = new();
    private readonly List<AuditEvent> _audit = new();

    private Incident() { }

    public Guid Id { get; private init; }
    public DateTimeOffset StartedAt { get; private init; }
    public IncidentState State { get; private set; }

    public IncidentNumber? IncidentNumber { get; private set; }
    public IlsNumber? IlsNumber { get; private set; }
    public string? Keyword { get; private set; }
    public string? Street { get; private set; }
    public string? District { get; private set; }
    public string? Status { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosedBy { get; private set; }

    public IReadOnlyList<ChecklistItem> Checklist => _checklist;
    public IReadOnlyList<EtbEntry> Journal => _journal;
    public IReadOnlyList<RoleAssignment> Roles => _roles;
    public IReadOnlyList<ForceUnit> Forces => _forces;
    public IReadOnlyList<AtemschutzTrupp> ScbaTrupps => _scbaTrupps;
    public IReadOnlyList<AuditEvent> Audit => _audit;

    public static Incident Start(
        IClock clock,
        SessionOperator openedBy,
        string? keyword = null,
        IlsNumber? ilsNumber = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(openedBy);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            StartedAt = clock.Now,
            State = IncidentState.Open,
            Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
            IlsNumber = ilsNumber
        };
        incident._audit.Add(new AuditEvent(clock.Now, "opened", openedBy.Display));
        incident.LogLifecycle(clock, openedBy, ilsNumber is null
            ? "Einsatz begonnen"
            : $"Einsatz begonnen (ILS {ilsNumber.Value})");
        return incident;
    }

    public static Incident Rehydrate(
        Guid id,
        DateTimeOffset startedAt,
        IncidentState state,
        IncidentNumber? incidentNumber,
        IlsNumber? ilsNumber,
        string? keyword,
        string? street,
        string? district,
        string? status,
        DateTimeOffset? closedAt,
        string? closedBy,
        IEnumerable<ChecklistItem> checklist,
        IEnumerable<EtbEntry> journal,
        IEnumerable<RoleAssignment> roles,
        IEnumerable<ForceUnit> forces,
        IEnumerable<AtemschutzTrupp> scbaTrupps,
        IEnumerable<AuditEvent> audit)
    {
        var incident = new Incident
        {
            Id = id,
            StartedAt = startedAt,
            State = state,
            IncidentNumber = incidentNumber,
            IlsNumber = ilsNumber,
            Keyword = keyword,
            Street = street,
            District = district,
            Status = status,
            ClosedAt = closedAt,
            ClosedBy = closedBy
        };
        incident._checklist.AddRange(checklist);
        incident._journal.AddRange(journal);
        incident._roles.AddRange(roles);
        incident._forces.AddRange(forces);
        incident._scbaTrupps.AddRange(scbaTrupps);
        incident._audit.AddRange(audit);
        return incident;
    }

    private void EnsureOpen()
    {
        if (State == IncidentState.Closed)
            throw new IncidentClosedException();
    }

    // Appends a lifecycle entry straight to the journal, deliberately bypassing
    // AddJournalEntry's EnsureOpen guard: Close has to log its own entry, and it is only
    // ever called from the transition methods, which guard themselves.
    private void LogLifecycle(IClock clock, SessionOperator op, string text) =>
        _journal.Add(EtbEntry.Create(clock.Now, EtbDirection.Internal, text, op));

    public void ResumeEditing(IClock clock, SessionOperator resumedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(resumedBy);
        EnsureOpen();
        _audit.Add(new AuditEvent(clock.Now, "resumed", resumedBy.Display));
        LogLifecycle(clock, resumedBy, "Bearbeitung fortgesetzt");
    }

    public void Close(IClock clock, SessionOperator closedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(closedBy);
        EnsureOpen();
        // Must precede the state flip — a closed incident rejects journal writes.
        LogLifecycle(clock, closedBy, "Einsatz abgeschlossen");
        State = IncidentState.Closed;
        ClosedAt = clock.Now;
        ClosedBy = closedBy.Display;
        _audit.Add(new AuditEvent(clock.Now, "closed", closedBy.Display));
    }

    public void SetIncidentNumber(IncidentNumber? number)
    {
        EnsureOpen();
        IncidentNumber = number;
    }

    public void SetIlsNumber(IlsNumber? number)
    {
        EnsureOpen();
        IlsNumber = number;
    }

    public void SetKeyword(string? keyword)
    {
        EnsureOpen();
        Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    public void SetAddress(string? street, string? district)
    {
        EnsureOpen();
        Street = string.IsNullOrWhiteSpace(street) ? null : street.Trim();
        District = string.IsNullOrWhiteSpace(district) ? null : district.Trim();
    }

    public void SetStatus(string? status)
    {
        EnsureOpen();
        Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
    }

    public int TotalPersonnel => _forces.Sum(f => f.PersonnelCount);

    /// <summary>Total Atemschutzgeräteträger across all units — how many Trupps can be formed.</summary>
    public int TotalScba => _forces.Sum(f => f.ScbaCount);

    public void SeedChecklist(IEnumerable<string> itemTexts)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(itemTexts);
        foreach (var text in itemTexts)
            _checklist.Add(new ChecklistItem(text));
    }

    public ChecklistItem ToggleChecklistItem(Guid itemId)
    {
        EnsureOpen();
        var item = _checklist.FirstOrDefault(c => c.Id == itemId)
            ?? throw new KeyNotFoundException($"Checklist item {itemId} not found.");
        item.Toggle();
        return item;
    }

    public EtbEntry AddJournalEntry(
        IClock clock,
        SessionOperator op,
        EtbDirection direction,
        string text,
        string? from = null,
        string? to = null)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);
        var entry = EtbEntry.Create(clock.Now, direction, text, op, from, to);
        _journal.Add(entry);
        return entry;
    }

    public RoleAssignment AssignRole(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null)
    {
        EnsureOpen();
        var assignment = RoleAssignment.Create(role, personName, callSign, from, to, section, phone);
        _roles.Add(assignment);
        return assignment;
    }

    /// <summary>
    /// Stamps the end of an assignment. Ending an already-ended assignment is rejected: the Bis
    /// time is a record of when someone actually handed over, not a field to be corrected by
    /// pressing the button twice.
    /// </summary>
    public RoleAssignment EndRoleAssignment(Guid assignmentId, DateTimeOffset to)
    {
        EnsureOpen();
        var index = _roles.FindIndex(r => r.Id == assignmentId);
        if (index < 0)
            throw new ArgumentException("Funktionszuweisung nicht gefunden.", nameof(assignmentId));
        if (_roles[index].To is not null)
            throw new InvalidOperationException("Funktionszuweisung ist bereits beendet.");

        var ended = _roles[index].EndedAt(to);
        _roles[index] = ended;
        return ended;
    }

    public ForceUnit AddForceUnit(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0)
    {
        EnsureOpen();
        var unit = ForceUnit.Create(brigade, personnelCount, callSign, status, notes, scbaCount);
        _forces.Add(unit);
        return unit;
    }

    /// <summary>
    /// Updates a unit's Status and Bemerkung in place, keeping its identity and position. Mirrors
    /// <see cref="EndRoleAssignment"/>: ForceUnit is a record, so "changing" it means replacing the
    /// entry rather than mutating it.
    /// </summary>
    public ForceUnit UpdateForceUnit(Guid unitId, string? status, string? notes)
    {
        EnsureOpen();
        var index = _forces.FindIndex(f => f.Id == unitId);
        if (index < 0)
            throw new ArgumentException("Einheit nicht gefunden.", nameof(unitId));

        var updated = _forces[index].WithStatusAndNotes(status, notes);
        _forces[index] = updated;
        return updated;
    }

    public AtemschutzTrupp AddScbaTrupp(
        IClock clock,
        string designation,
        string members,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        var trupp = AtemschutzTrupp.Register(
            clock.Now, designation, members, callSign, task,
            maxDurationMinutes, returnPressureBar, pressureControlIntervalMinutes);
        _scbaTrupps.Add(trupp);
        return trupp;
    }

    public AtemschutzTrupp StartScbaTrupp(IClock clock, Guid truppId, int startPressure)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        var trupp = FindScbaTrupp(truppId);
        trupp.Start(clock.Now, startPressure);
        return trupp;
    }

    public AtemschutzTrupp RecordScbaPressure(IClock clock, Guid truppId, int bar)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        var trupp = FindScbaTrupp(truppId);
        trupp.RecordPressure(clock.Now, bar);
        return trupp;
    }

    public AtemschutzTrupp MarkScbaReturned(IClock clock, Guid truppId)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        var trupp = FindScbaTrupp(truppId);
        trupp.MarkReturned(clock.Now);
        return trupp;
    }

    private AtemschutzTrupp FindScbaTrupp(Guid truppId) =>
        _scbaTrupps.FirstOrDefault(t => t.Id == truppId)
            ?? throw new KeyNotFoundException($"Atemschutz-Trupp {truppId} not found.");
}
