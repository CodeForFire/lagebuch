using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain;

public sealed record AuditEvent(DateTimeOffset At, string Action, string By);

public sealed class Incident
{
    private readonly List<ChecklistItem> _checklistAufbau = new();
    private readonly List<ChecklistItem> _checklistAbbau = new();
    private readonly List<EtbEntry> _journal = new();
    private readonly List<RoleAssignment> _roles = new();
    private readonly List<ForceUnit> _forces = new();
    private readonly List<AtemschutzTrupp> _scbaTrupps = new();
    private readonly List<AuditEvent> _audit = new();
    private readonly List<IncidentTimerState> _timers = new();
    private readonly List<IncidentFile> _files = new();
    private readonly List<IncidentTask> _tasks = new();
    private readonly List<Building> _buildings = new();
    private readonly List<Dwelling> _dwellings = new();

    private Incident() { }

    public Guid Id { get; private init; }
    public DateTimeOffset StartedAt { get; private init; }
    public IncidentState State { get; private set; }

    public IncidentNumber? IncidentNumber { get; private set; }
    public string? Keyword { get; private set; }
    public string? Street { get; private set; }
    public string? District { get; private set; }
    public string? Status { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosedBy { get; private set; }

    public IReadOnlyList<ChecklistItem> ChecklistAufbau => _checklistAufbau;
    public IReadOnlyList<ChecklistItem> ChecklistAbbau => _checklistAbbau;
    public IReadOnlyList<EtbEntry> Journal => _journal;
    public IReadOnlyList<RoleAssignment> Roles => _roles;
    public IReadOnlyList<ForceUnit> Forces => _forces;
    public IReadOnlyList<AtemschutzTrupp> ScbaTrupps => _scbaTrupps;
    public IReadOnlyList<AuditEvent> Audit => _audit;

    /// <summary>Persisted incident-level timers, keyed by <see cref="IncidentTimerState.Key"/>.</summary>
    public IReadOnlyList<IncidentTimerState> Timers => _timers;

    public IReadOnlyList<IncidentFile> Files => _files;

    /// <summary>Operational to-dos (#88), in creation order. Sorting for display lives in the
    /// view layer — the aggregate keeps insertion order, like every other list here.</summary>
    public IReadOnlyList<IncidentTask> Tasks => _tasks;

    public IReadOnlyList<Building> Buildings => _buildings;
    public IReadOnlyList<Dwelling> Dwellings => _dwellings;

    /// <summary>The persisted state of the timer with this key, or null if none has been recorded.</summary>
    public IncidentTimerState? FindTimer(string key) => _timers.Find(t => t.Key == key);

    private static string FloorLabel(int ordinal) =>
        ordinal == 0 ? "EG" : $"{ordinal}. OG";

    private Building FindBuilding(Guid buildingId) =>
        _buildings.FirstOrDefault(b => b.Id == buildingId)
            ?? throw new KeyNotFoundException($"Haus {buildingId} nicht gefunden.");

    private Dwelling FindDwelling(Guid buildingId, int floorOrdinal, int apartmentNumber) =>
        _dwellings.FirstOrDefault(d =>
            d.BuildingId == buildingId &&
            d.FloorOrdinal == floorOrdinal &&
            d.ApartmentNumber == apartmentNumber)
            ?? throw new KeyNotFoundException(
                $"Wohnung nicht gefunden: {buildingId}, {FloorLabel(floorOrdinal)}, Whg. {apartmentNumber}");

    public static Incident Start(
        IClock clock,
        SessionOperator openedBy,
        string? keyword = null,
        IncidentNumber? incidentNumber = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(openedBy);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            StartedAt = clock.Now,
            State = IncidentState.Open,
            Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
            IncidentNumber = incidentNumber
        };
        incident._audit.Add(new AuditEvent(clock.Now, "opened", openedBy.Display));
        incident.AppendSystemEntry(clock, openedBy, incidentNumber is null
            ? "Einsatz begonnen"
            : $"Einsatz begonnen (Einsatznummer {incidentNumber.Value})");
        return incident;
    }

    public static Incident Rehydrate(
        Guid id,
        DateTimeOffset startedAt,
        IncidentState state,
        IncidentNumber? incidentNumber,
        string? keyword,
        string? street,
        string? district,
        string? status,
        DateTimeOffset? closedAt,
        string? closedBy,
        IEnumerable<ChecklistItem> checklistAufbau,
        IEnumerable<ChecklistItem> checklistAbbau,
        IEnumerable<EtbEntry> journal,
        IEnumerable<RoleAssignment> roles,
        IEnumerable<ForceUnit> forces,
        IEnumerable<AtemschutzTrupp> scbaTrupps,
        IEnumerable<AuditEvent> audit,
        IEnumerable<IncidentTimerState> timers,
        IEnumerable<IncidentFile> files,
        IEnumerable<IncidentTask> tasks,
        IEnumerable<Building> buildings,
        IEnumerable<Dwelling> dwellings)
    {
        var incident = new Incident
        {
            Id = id,
            StartedAt = startedAt,
            State = state,
            IncidentNumber = incidentNumber,
            Keyword = keyword,
            Street = street,
            District = district,
            Status = status,
            ClosedAt = closedAt,
            ClosedBy = closedBy
        };
        incident._checklistAufbau.AddRange(checklistAufbau);
        incident._checklistAbbau.AddRange(checklistAbbau);
        incident._journal.AddRange(journal);
        incident._roles.AddRange(roles);
        incident._forces.AddRange(forces);
        incident._scbaTrupps.AddRange(scbaTrupps);
        incident._audit.AddRange(audit);
        incident._timers.AddRange(timers);
        incident._files.AddRange(files);
        incident._tasks.AddRange(tasks);
        incident._buildings.AddRange(buildings);
        incident._dwellings.AddRange(dwellings);
        return incident;
    }

    /// <summary>
    /// Records (or replaces) the state of an incident-level timer keyed by <paramref name="key"/>.
    /// Persisted so the timer survives a reopen/crash; live due/remaining values are recomputed from
    /// the anchor. Guarded like every other edit — a closed incident takes no mutations.
    /// </summary>
    public void UpsertTimer(
        string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Timer key must not be blank.", nameof(key));
        _timers.RemoveAll(t => t.Key == key);
        _timers.Add(new IncidentTimerState(key.Trim(), cycleAnchor, intervalMinutes, recurringIntervalMinutes, isRunning));
    }

    private void EnsureOpen()
    {
        if (State == IncidentState.Closed)
            throw new IncidentClosedException();
    }

    // Appends an automatic (system-generated) entry straight to the journal, deliberately
    // bypassing AddJournalEntry's EnsureOpen guard: Close has to log its own entry, and this is
    // only ever called from methods that guard themselves. Uses EtbDirection.System so these
    // machine-written lines are distinguishable from -- and filterable apart from -- human
    // "Intern" notes.
    private void AppendSystemEntry(
        IClock clock, SessionOperator op, string text, string? from = null, string? to = null) =>
        _journal.Add(EtbEntry.Create(clock.Now, EtbDirection.System, text, op, from, to));

    public void ResumeEditing(IClock clock, SessionOperator resumedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(resumedBy);
        EnsureOpen();
        _audit.Add(new AuditEvent(clock.Now, "resumed", resumedBy.Display));
        AppendSystemEntry(clock, resumedBy, "Bearbeitung fortgesetzt");
    }

    public void Close(IClock clock, SessionOperator closedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(closedBy);
        EnsureOpen();
        // Must precede the state flip — a closed incident rejects journal writes.
        AppendSystemEntry(clock, closedBy, "Einsatz abgeschlossen");
        // A closed incident is a historical record: nobody hands over a role after the fact, so any
        // still-running assignment is stamped closed right along with it, silently — same as a plain
        // AssignRole/EndRoleAssignment, which don't log to the ETB either.
        for (var i = 0; i < _roles.Count; i++)
            if (_roles[i].To is null)
                _roles[i] = _roles[i].EndedAt(clock.Now);
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

    /// <summary>Total Führungskräfte across all units (#76).</summary>
    public int TotalOfficer => _forces.Sum(f => f.OfficerCount);

    /// <summary>Total Atemschutzgeräteträger across all units — how many Trupps can be formed.</summary>
    public int TotalScba => _forces.Sum(f => f.ScbaCount);

    public void SeedChecklist(
        IEnumerable<(string Text, bool IsMandatory)> aufbauItems,
        IEnumerable<(string Text, bool IsMandatory)> abbauItems)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(aufbauItems);
        ArgumentNullException.ThrowIfNull(abbauItems);
        foreach (var (text, isMandatory) in aufbauItems)
            _checklistAufbau.Add(new ChecklistItem(text, isMandatory));
        foreach (var (text, isMandatory) in abbauItems)
            _checklistAbbau.Add(new ChecklistItem(text, isMandatory));
    }

    /// <summary>
    /// Toggles the item and, on a false→true "all mandatory items done" transition for its list,
    /// logs a one-off ETB system entry — "Systemmeldung" per the issue means a journal line, not a
    /// UI notification. Silent on the reverse transition (unchecking after completion) and on every
    /// other toggle, mirroring how <see cref="UpdateForceUnit"/> only logs a real status change.
    /// </summary>
    public ChecklistItem ToggleChecklistItem(IClock clock, SessionOperator op, Guid itemId)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var (list, kind) = FindChecklistOwning(itemId);
        var item = list.First(c => c.Id == itemId);

        var wasComplete = AllMandatoryDone(list);
        item.Toggle();
        var isComplete = AllMandatoryDone(list);

        if (!wasComplete && isComplete)
            AppendSystemEntry(clock, op, $"Checkliste {kind} abgeschlossen: alle Pflichtpunkte erledigt");

        return item;
    }

    private (List<ChecklistItem> List, ChecklistKind Kind) FindChecklistOwning(Guid itemId)
    {
        if (_checklistAufbau.Any(c => c.Id == itemId))
            return (_checklistAufbau, ChecklistKind.Aufbau);
        if (_checklistAbbau.Any(c => c.Id == itemId))
            return (_checklistAbbau, ChecklistKind.Abbau);
        throw new KeyNotFoundException($"Checklist item {itemId} not found.");
    }

    private static bool AllMandatoryDone(IReadOnlyList<ChecklistItem> items) =>
        items.Where(i => i.IsMandatory).All(i => i.IsDone);

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
        // Direction rides the wire as a plain integer (AddJournalEntryCommand.Direction), so a
        // malformed value (e.g. a forged "direction": 99) must not reach EtbEntry.Create and get
        // persisted by an out-of-range ordinal. Note this intentionally still allows System: several
        // local call sites (ScbaViewModel's trupp-lifecycle logging) legitimately go through this
        // same method with EtbDirection.System rather than the private AppendSystemEntry helper, so
        // rejecting it here would break them, not just a synced command's ability to forge one.
        if (!Enum.IsDefined(direction))
            throw new ArgumentException("Ungültige Richtung für einen ETB-Eintrag.", nameof(direction));
        var entry = EtbEntry.Create(clock.Now, direction, text, op, from, to);
        _journal.Add(entry);
        return entry;
    }

    /// <summary>
    /// Corrects a manually-typed ETB entry's text, preserving the prior wording under
    /// <see cref="EtbEntry.Edits"/>. System-direction entries (Kräfte, Atemschutz,
    /// Einsatz-Lebenszyklus) are never editable — they are the app's own record of what happened,
    /// not something an operator wrote and could have mistyped. Any operator may edit any manual
    /// entry, matching <see cref="UpdateForceUnit"/>'s "no per-field author restriction" precedent.
    ///
    /// Unlike a silent label correction (<see cref="RenameFile"/>), a correction to the journal
    /// itself is a reportable event: it appends its own System line, the same trace every other
    /// record-changing mutation on this aggregate leaves, so a rewrite is visible in the grid and
    /// the PDF export even though the row itself now shows the corrected text.
    /// </summary>
    public EtbEntry EditJournalEntry(IClock clock, SessionOperator op, Guid entryId, string text)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _journal.FindIndex(e => e.Id == entryId);
        if (index < 0)
            throw new KeyNotFoundException($"ETB-Eintrag {entryId} nicht gefunden.");

        var existing = _journal[index];
        if (existing.Direction == EtbDirection.System)
            throw new InvalidOperationException("Systemeinträge können nicht bearbeitet werden.");

        var edited = existing.WithEditedText(text, op, clock.Now);
        _journal[index] = edited;
        // WithEditedText is a no-op (returns the same instance) when the text didn't actually
        // change -- nothing to trace in that case.
        if (edited.Edits.Count > existing.Edits.Count)
            AppendSystemEntry(clock, op, $"ETB-Eintrag {existing.Timestamp:HH:mm} bearbeitet");
        return edited;
    }

    /// <summary>
    /// Records a new assignment and logs it to the ETB. Mirrors <see cref="AddForceUnit"/> and
    /// <see cref="TransferRole"/>: creating a new record is always a reportable event, so this logs
    /// unconditionally — unlike <see cref="EditRolePhone"/>, there is no prior state to compare
    /// against for a "did anything actually change" gate.
    /// </summary>
    public RoleAssignment AssignRole(
        IClock clock,
        SessionOperator op,
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var assignment = RoleAssignment.Create(role, personName, callSign, from, to, section, phone);
        _roles.Add(assignment);
        AppendSystemEntry(clock, op, $"Funktion {assignment.Role} zugewiesen: {assignment.PersonName}");
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

    /// <summary>
    /// Ends a running assignment and starts a new one for the same role and section in one step —
    /// a handover, not two independent edits. Unlike <see cref="AssignRole"/>/
    /// <see cref="EndRoleAssignment"/> (which stay silent), a transfer always appends a System
    /// entry: "wer hat wann welche Funktion übernommen" is exactly what the journal exists to answer.
    /// </summary>
    public RoleAssignment TransferRole(
        IClock clock, SessionOperator op, Guid assignmentId,
        string newPersonName, string? newCallSign, string? newPhone)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _roles.FindIndex(r => r.Id == assignmentId);
        if (index < 0)
            throw new ArgumentException("Funktionszuweisung nicht gefunden.", nameof(assignmentId));
        if (_roles[index].To is not null)
            throw new InvalidOperationException("Funktionszuweisung ist bereits beendet.");

        var previous = _roles[index];
        var ended = previous.EndedAt(clock.Now);
        _roles[index] = ended;

        var next = RoleAssignment.Create(
            ended.Role, newPersonName, newCallSign, from: clock.Now, to: null,
            section: ended.Section, phone: newPhone);
        _roles.Add(next);

        AppendSystemEntry(clock, op, $"Funktion {ended.Role} übergeben: {ended.PersonName} → {next.PersonName}");
        return next;
    }

    /// <summary>
    /// Corrects a role assignment's phone number. Mirrors <see cref="UpdateForceUnit"/>: only a real
    /// change reaches the ETB, so re-saving the same (normalised) number is not a reportable event.
    /// </summary>
    public RoleAssignment EditRolePhone(IClock clock, SessionOperator op, Guid assignmentId, string? phone)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _roles.FindIndex(r => r.Id == assignmentId);
        if (index < 0)
            throw new ArgumentException("Funktionszuweisung nicht gefunden.", nameof(assignmentId));

        var previous = _roles[index];
        var updated = previous.WithPhone(phone);
        _roles[index] = updated;

        if (!string.Equals(previous.Phone, updated.Phone, StringComparison.Ordinal))
            AppendSystemEntry(clock, op,
                $"Handynummer für {updated.Role} ({updated.PersonName}) geändert: {previous.Phone ?? "—"} → {updated.Phone ?? "—"}");

        return updated;
    }

    /// <summary>
    /// Records a unit and logs it to the ETB. The clock and operator are required rather than
    /// optional because the entry is the point: the Einsatztagebuch has to answer when which
    /// LageBuch was alarmed, so no caller may record a unit without leaving that trace.
    /// </summary>
    public ForceUnit AddForceUnit(
        IClock clock,
        SessionOperator op,
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var unit = ForceUnit.Create(brigade, personnelCount, callSign, status, notes, scbaCount, officerCount);
        _forces.Add(unit);

        // Optional clauses are omitted rather than printed empty, so a bare unit reads as
        // "Einheit aufgenommen: Aich, Stärke 0/6/6" instead of trailing "davon 0 AGT — Status: ".
        var text = $"Einheit aufgenommen: {Label(unit)}, Stärke {unit.StrengthText}";
        if (unit.ScbaCount > 0)
            text += $", davon {unit.ScbaCount} AGT";
        if (unit.Status is not null)
            text += $" — Status: {unit.Status}";

        AppendSystemEntry(clock, op, text, to: unit.CallSign);
        return unit;
    }

    /// <summary>
    /// Updates a unit's Status and Bemerkung in place, keeping its identity and position. Mirrors
    /// <see cref="EndRoleAssignment"/>: ForceUnit is a record, so "changing" it means replacing the
    /// entry rather than mutating it.
    ///
    /// Only a real status transition reaches the ETB. The Bemerkung is a working note rather than
    /// a reportable event, and the grid writes it through on every keystroke, so logging it would
    /// bury the journal it is supposed to inform.
    /// </summary>
    public ForceUnit UpdateForceUnit(
        IClock clock, SessionOperator op, Guid unitId, string? status, string? notes)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _forces.FindIndex(f => f.Id == unitId);
        if (index < 0)
            throw new ArgumentException("Einheit nicht gefunden.", nameof(unitId));

        var previous = _forces[index];
        var updated = previous.WithStatusAndNotes(status, notes);
        _forces[index] = updated;

        // Compare the normalised values, so re-selecting the same status -- or the same status
        // with stray whitespace -- is not a transition.
        if (!string.Equals(previous.Status, updated.Status, StringComparison.Ordinal))
            AppendSystemEntry(clock, op, StatusChangeText(previous, updated), from: updated.CallSign);

        return updated;
    }

    private static string Label(ForceUnit unit) =>
        unit.CallSign is null ? unit.Brigade : $"{unit.Brigade} ({unit.CallSign})";

    /// <summary>
    /// Corrects a unit's Stärke (Führungskraft / Gesamt / davon AGT) in place, keeping its identity
    /// and position (#76). A real change is protokolliert twice, mirroring the ETB's own edit rule:
    /// as a Systemmeldung in the journal (like a status transition, credited via
    /// <see cref="UpdateForceUnit"/>'s from-call-sign convention) and as a retained prior value on
    /// the unit itself (<see cref="ForceUnit.WithStrength"/>). An unchanged resubmission is neither.
    /// </summary>
    public ForceUnit UpdateForceStrength(
        IClock clock, SessionOperator op, Guid unitId,
        int officerCount, int personnelCount, int scbaCount)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _forces.FindIndex(f => f.Id == unitId);
        if (index < 0)
            throw new KeyNotFoundException($"Einheit {unitId} nicht gefunden.");

        var previous = _forces[index];
        var updated = previous.WithStrength(officerCount, personnelCount, scbaCount, op, clock.Now);
        if (ReferenceEquals(updated, previous))
            return previous;

        _forces[index] = updated;

        // The AGT clause appears only when the AGT count itself moved — same economy as
        // AddForceUnit's optional clauses.
        var text = $"{Label(updated)}: Stärke {previous.StrengthText} → {updated.StrengthText}";
        if (previous.ScbaCount != updated.ScbaCount)
            text += $", davon AGT {previous.ScbaCount} → {updated.ScbaCount}";
        AppendSystemEntry(clock, op, text, from: updated.CallSign);

        return updated;
    }

    private static string StatusChangeText(ForceUnit previous, ForceUnit updated) => updated.Status switch
    {
        null => $"{Label(updated)}: Status aufgehoben (vorher {previous.Status})",
        _ when previous.Status is null => $"{Label(updated)}: Status {updated.Status}",
        _ => $"{Label(updated)}: Status {previous.Status} → {updated.Status}",
    };

    /// <summary>
    /// Takes a unit back completely (#76 follow-up): row, Wert-Historie and totals go with it, and
    /// the ETB records the removal like any other reportable event. A closed Einsatz is a
    /// historical record — EnsureOpen guards it; an unknown or already-removed id throws so a
    /// replayed removal fails loudly instead of silently no-oping.
    /// </summary>
    public void RemoveForceUnit(IClock clock, SessionOperator op, Guid unitId)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _forces.FindIndex(f => f.Id == unitId);
        if (index < 0)
            throw new KeyNotFoundException($"Einheit {unitId} nicht gefunden.");

        var unit = _forces[index];
        _forces.RemoveAt(index);
        AppendSystemEntry(clock, op, $"Einheit entfernt: {Label(unit)}", from: unit.CallSign);
    }

    public AtemschutzTrupp AddScbaTrupp(
        IClock clock,
        string designation,
        IEnumerable<TruppMember> members,
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

    /// <summary>
    /// Records an attached file's metadata and logs it to the ETB. Bytes never pass through the
    /// domain — the caller writes them to storage separately, keyed by
    /// <see cref="IncidentFile.StorageFileName"/> on the returned <see cref="IncidentFile"/>.
    /// </summary>
    public IncidentFile AddFile(
        IClock clock, SessionOperator op, string fileName, string contentType, long sizeBytes)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var file = IncidentFile.Create(fileName, contentType, sizeBytes, clock.Now, op.Display);
        _files.Add(file);
        AppendSystemEntry(clock, op, $"Datei hinzugefügt: {file.FileName}");
        return file;
    }

    /// <summary>
    /// Corrects a file's display label. Silent — no ETB entry — matching
    /// <see cref="UpdateForceUnit"/>'s Bemerkung field: a label correction isn't a reportable event.
    /// </summary>
    public IncidentFile RenameFile(Guid fileId, string? displayName)
    {
        EnsureOpen();
        var index = _files.FindIndex(f => f.Id == fileId);
        if (index < 0)
            throw new KeyNotFoundException($"Datei {fileId} nicht gefunden.");
        var renamed = _files[index].WithDisplayName(displayName);
        _files[index] = renamed;
        return renamed;
    }

    /// <summary>
    /// Records a task (#88). Deliberately silent — unlike <see cref="AddForceUnit"/>, no ETB
    /// system line: tasks are work management, not the operational log, and a task spawned from
    /// an ETB entry would just duplicate that entry. The PDF export reports tasks instead.
    /// Importance/Urgency ride the wire as integers, so out-of-range values are rejected here the
    /// same way <see cref="AddJournalEntry"/> rejects malformed directions.
    /// </summary>
    public IncidentTask AddTask(
        IClock clock,
        SessionOperator op,
        string text,
        string? assignee,
        TaskImportance importance,
        TaskUrgency urgency,
        int timerMinutes)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);
        if (!Enum.IsDefined(importance))
            throw new ArgumentException("Unbekannte Wichtigkeit.", nameof(importance));
        if (!Enum.IsDefined(urgency))
            throw new ArgumentException("Unbekannte Dringlichkeit.", nameof(urgency));

        var task = IncidentTask.Create(clock.Now, text, assignee, importance, urgency, timerMinutes, op);
        _tasks.Add(task);
        return task;
    }

    /// <summary>
    /// Stamps or clears a task's completion. Un-checking restores the open state but never touches
    /// <see cref="IncidentTask.DueAt"/> — the original timer stands, so a stale task shows as
    /// overdue again immediately. Unknown ids throw so a replayed command fails loudly.
    /// </summary>
    public IncidentTask SetTaskCompleted(Guid taskId, bool isDone, IClock clock, SessionOperator op)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var index = _tasks.FindIndex(t => t.Id == taskId);
        if (index < 0)
            throw new KeyNotFoundException($"Aufgabe {taskId} nicht gefunden.");

        var updated = _tasks[index].WithCompletion(isDone, op, clock.Now);
        _tasks[index] = updated;
        return updated;
    }

    private AtemschutzTrupp FindScbaTrupp(Guid truppId) =>
        _scbaTrupps.FirstOrDefault(t => t.Id == truppId)
            ?? throw new KeyNotFoundException($"Atemschutz-Trupp {truppId} not found.");

    public void AddCoBuilding(IClock clock, SessionOperator op, string name, int floorCount, int apartmentsPerFloor)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var ordinal = _buildings.Count;
        var building = Building.Create(name, floorCount, apartmentsPerFloor, ordinal);
        _buildings.Add(building);

        for (var floor = 0; floor <= floorCount; floor++)
            for (var apt = 1; apt <= apartmentsPerFloor; apt++)
                _dwellings.Add(Dwelling.Create(building.Id, floor, apt));

        AppendSystemEntry(clock, op,
            $"CO-Messprotokoll eröffnet: {building.Name} (EG–{FloorLabel(floorCount)}, {apartmentsPerFloor} Wohnungen je Geschoss)");
    }

    public void UpdateCoBuildingStructure(IClock clock, SessionOperator op, Guid buildingId, int floorCount, int apartmentsPerFloor)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var building = FindBuilding(buildingId);
        var oldFloorCount = building.FloorCount;
        var oldApts = building.ApartmentsPerFloor;

        var updated = building.WithStructure(floorCount, apartmentsPerFloor);
        var index = _buildings.IndexOf(building);
        _buildings[index] = updated;

        // Remove dwellings outside the new structure
        var removed = _dwellings.RemoveAll(d =>
            d.BuildingId == buildingId &&
            (d.FloorOrdinal > floorCount || d.ApartmentNumber > apartmentsPerFloor));

        var text = $"CO-Struktur geändert: {building.Name} jetzt EG–{FloorLabel(floorCount)}, {apartmentsPerFloor} Wohnungen je Geschoss";
        if (removed > 0)
            text += $", {removed} Wohnungen entfernt";

        AppendSystemEntry(clock, op, text);
    }

    public void RemoveCoBuilding(IClock clock, SessionOperator op, Guid buildingId)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var building = FindBuilding(buildingId);
        _buildings.Remove(building);
        _dwellings.RemoveAll(d => d.BuildingId == buildingId);

        AppendSystemEntry(clock, op, $"CO-Messprotokoll entfernt: {building.Name}");
    }

    public void RecordCoValue(IClock clock, SessionOperator op, Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        if (coValue is < 0)
            throw new ArgumentOutOfRangeException(nameof(coValue), "CO-Messwert darf nicht negativ sein.");

        var building = FindBuilding(buildingId);
        var dwelling = FindDwelling(buildingId, floorOrdinal, apartmentNumber);

        if (dwelling.CoValue == coValue)
            return;

        var index = _dwellings.IndexOf(dwelling);
        _dwellings[index] = dwelling.WithCoValue(coValue);

        var location = CoMeasurementLabels.DwellingLocation(building, floorOrdinal, apartmentNumber);
        var text = coValue is { } v
            ? $"CO-Messung {location}: {v} ppm"
            : $"CO-Messung {location}: Messwert gelöscht";
        AppendSystemEntry(clock, op, text);
    }

    public void SetDwellingStatus(IClock clock, SessionOperator op, Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);

        var building = FindBuilding(buildingId);
        var dwelling = FindDwelling(buildingId, floorOrdinal, apartmentNumber);

        if (dwelling.Status == status)
            return;

        var index = _dwellings.IndexOf(dwelling);
        _dwellings[index] = dwelling.WithStatus(status);

        var location = CoMeasurementLabels.DwellingLocation(building, floorOrdinal, apartmentNumber);
        AppendSystemEntry(clock, op, $"Whg.-Status {location}: {CoMeasurementLabels.StatusText(status)}");
    }

    public void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, bool? keyAvailable)
    {
        EnsureOpen();
        var dwelling = FindDwelling(buildingId, floorOrdinal, apartmentNumber);
        var index = _dwellings.IndexOf(dwelling);
        _dwellings[index] = dwelling.WithDetails(residentName, keyAvailable);
    }

    public void SetFloorDescription(Guid buildingId, int ordinal, string? description)
    {
        EnsureOpen();
        var building = FindBuilding(buildingId);
        var updated = building.WithFloorDescription(ordinal, description);
        var index = _buildings.IndexOf(building);
        _buildings[index] = updated;
    }

    public void SetApartmentLabel(Guid buildingId, int apartmentNumber, string? label)
    {
        EnsureOpen();
        var building = FindBuilding(buildingId);
        var updated = building.WithApartmentLabel(apartmentNumber, label);
        var index = _buildings.IndexOf(building);
        _buildings[index] = updated;
    }
}
