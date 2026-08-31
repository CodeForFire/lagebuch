using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

public class IncidentOperationsTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock, out SessionOperator op)
    {
        clock = new FixedClock(T0);
        op = new SessionOperator("Müller", "FFB 12/1");
        return Incident.Start(clock, op);
    }

    [Fact]
    public void Seed_and_toggle_checklist()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(
            new[] { ("Blaulicht aus?", false), ("Bei ILS gemeldet?", false) },
            Array.Empty<(string, bool)>());
        Assert.Equal(2, incident.ChecklistAufbau.Count);

        var first = incident.ChecklistAufbau[0];
        Assert.True(incident.ToggleChecklistItem(clock, op, first.Id).IsDone);
    }

    [Fact]
    public void Toggle_unknown_item_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        Assert.Throws<KeyNotFoundException>(() => incident.ToggleChecklistItem(clock, op, Guid.NewGuid()));
    }

    [Fact]
    public void Aufbau_and_abbau_seed_independently()
    {
        var incident = NewIncident(out _, out _);
        incident.SeedChecklist(
            new[] { ("Fahrzeug prüfen", true) },
            new[] { ("Standort räumen", true), ("Material zählen", false) });

        Assert.Equal("Fahrzeug prüfen", Assert.Single(incident.ChecklistAufbau).Text);
        Assert.Equal(2, incident.ChecklistAbbau.Count);
    }

    // --- ETB logging for mandatory checklist completion -------------------------------------
    // "Systemmeldung" means an ETB entry (not a UI notification): the moment every mandatory
    // item in a list becomes checked, that transition is logged automatically, exactly once.
    [Fact]
    public void Completing_all_mandatory_aufbau_items_logs_a_system_entry_once()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(
            new[] { ("Fahrzeug prüfen", true), ("Kaffee kochen", false) },
            Array.Empty<(string, bool)>());
        var before = incident.Journal.Count;

        incident.ToggleChecklistItem(clock, op, incident.ChecklistAufbau[0].Id);

        Assert.Equal(before + 1, incident.Journal.Count);
        var entry = incident.Journal[^1];
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal("Checkliste Aufbau abgeschlossen: alle Pflichtpunkte erledigt", entry.Text);

        // The optional item still open afterward does not re-trigger logging.
        incident.ToggleChecklistItem(clock, op, incident.ChecklistAufbau[1].Id);
        Assert.Equal(before + 1, incident.Journal.Count);
    }

    [Fact]
    public void Unchecking_a_mandatory_item_after_completion_logs_nothing()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(new[] { ("Fahrzeug prüfen", true) }, Array.Empty<(string, bool)>());
        var id = incident.ChecklistAufbau[0].Id;
        incident.ToggleChecklistItem(clock, op, id); // completes -> logs
        var before = incident.Journal.Count;

        incident.ToggleChecklistItem(clock, op, id); // reopens -> silent

        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void A_checklist_with_no_mandatory_items_is_vacuously_complete_and_never_logs()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(new[] { ("Optional", false) }, Array.Empty<(string, bool)>());
        var before = incident.Journal.Count;

        incident.ToggleChecklistItem(clock, op, incident.ChecklistAufbau[0].Id);

        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void Aufbau_and_abbau_completion_are_tracked_independently()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(
            new[] { ("Aufbau Pflicht", true) },
            new[] { ("Abbau Pflicht", true) });

        incident.ToggleChecklistItem(clock, op, incident.ChecklistAufbau[0].Id);
        var afterAufbau = incident.Journal.Count;
        Assert.Equal("Checkliste Aufbau abgeschlossen: alle Pflichtpunkte erledigt", incident.Journal[^1].Text);

        incident.ToggleChecklistItem(clock, op, incident.ChecklistAbbau[0].Id);
        Assert.Equal(afterAufbau + 1, incident.Journal.Count);
        Assert.Equal("Checkliste Abbau abgeschlossen: alle Pflichtpunkte erledigt", incident.Journal[^1].Text);
    }

    [Fact]
    public void Update_force_unit_sets_status_and_notes()
    {
        // A unit's Status and Bemerkung change constantly during an Einsatz -- "Auf Anfahrt"
        // becomes "Im Einsatz" -- so they have to be correctable after the row was added.
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9, "FFB 1/40/1", "Alarmiert", null, 4);

        var updated = incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", "über DLK angefordert");

        Assert.Equal("Im Einsatz", updated.Status);
        Assert.Equal("über DLK angefordert", updated.Notes);

        // Replaced in place: same identity, same position, everything else untouched.
        Assert.Equal(unit.Id, updated.Id);
        Assert.Same(updated, Assert.Single(incident.Forces));
        Assert.Equal("FFB Wache 1", updated.Brigade);
        Assert.Equal(9, updated.PersonnelCount);
        Assert.Equal(4, updated.ScbaCount);
        Assert.Equal("FFB 1/40/1", updated.CallSign);
    }

    [Fact]
    public void Update_force_unit_trims_and_nulls_blank_values()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9, null, "Alarmiert", "Notiz");

        var updated = incident.UpdateForceUnit(clock, op, unit.Id, "  Im Einsatz  ", "   ");

        // Matches ForceUnit.Create: trimmed, and blank means "nothing recorded" rather than "".
        Assert.Equal("Im Einsatz", updated.Status);
        Assert.Null(updated.Notes);
    }

    [Fact]
    public void Update_unknown_force_unit_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        Assert.Throws<ArgumentException>(
            () => incident.UpdateForceUnit(clock, op, Guid.NewGuid(), "Im Einsatz", null));
    }

    [Fact]
    public void Update_force_unit_is_rejected_on_a_closed_incident()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(
            () => incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", null));
    }

    // --- ETB logging for Kräfte -------------------------------------------------------------
    // The Einsatztagebuch has to answer "when did which LageBuch arrive and change state", so
    // these entries are generated in the domain: no caller can record a unit without one.
    private static Etb.EtbEntry LastEntry(Incident incident) => incident.Journal[^1];

    [Fact]
    public void Adding_a_unit_logs_a_descriptive_etb_entry()
    {
        var incident = NewIncident(out var clock, out var op);
        var before = incident.Journal.Count;

        incident.AddForceUnit(clock, op, "FFB Wache 1", 9, "FFB 1/40/1", "Alarmiert", null, 4);

        Assert.Equal(before + 1, incident.Journal.Count);
        var entry = LastEntry(incident);
        Assert.Equal(Etb.EtbDirection.System, entry.Direction);
        Assert.Equal(
            "Einheit aufgenommen: FFB Wache 1 (FFB 1/40/1), Stärke 0/9/9, davon 4 AGT — Status: Alarmiert",
            entry.Text);

        // The Einsatzleitung alarms the unit, so the call sign is the recipient -- same split
        // ScbaViewModel uses for "bereitgestellt" versus "Druckkontrolle".
        Assert.Equal("FFB 1/40/1", entry.To);
        Assert.Null(entry.From);
    }

    [Fact]
    public void Adding_a_bare_unit_omits_the_optional_clauses()
    {
        var incident = NewIncident(out var clock, out var op);

        incident.AddForceUnit(clock, op, "Aich", 6);

        var entry = LastEntry(incident);

        // No call sign, no AGT, no status: none of them appear as empty decoration.
        Assert.Equal("Einheit aufgenommen: Aich, Stärke 0/6/6", entry.Text);
        Assert.Null(entry.To);
    }

    [Fact]
    public void Changing_the_status_logs_the_transition()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9, "FFB 1/40/1", "Alarmiert");
        var before = incident.Journal.Count;

        incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", null);

        Assert.Equal(before + 1, incident.Journal.Count);
        var entry = LastEntry(incident);
        Assert.Equal("FFB Wache 1 (FFB 1/40/1): Status Alarmiert → Im Einsatz", entry.Text);

        // The unit reports its own status, so here the call sign is the source.
        Assert.Equal("FFB 1/40/1", entry.From);
        Assert.Null(entry.To);
    }

    [Fact]
    public void Setting_a_status_for_the_first_time_reads_without_an_arrow()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        incident.UpdateForceUnit(clock, op, unit.Id, "Auf Anfahrt", null);

        Assert.Equal("Aich: Status Auf Anfahrt", LastEntry(incident).Text);
    }

    [Fact]
    public void Clearing_a_status_records_what_it_was()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, status: "Im Einsatz");

        incident.UpdateForceUnit(clock, op, unit.Id, null, null);

        Assert.Equal("Aich: Status aufgehoben (vorher Im Einsatz)", LastEntry(incident).Text);
    }

    [Fact]
    public void Editing_only_the_bemerkung_logs_nothing()
    {
        // The Bemerkung is a working note, not a reportable event -- and the grid writes it
        // through on every keystroke, so logging it would bury the ETB.
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9, status: "Alarmiert");
        var before = incident.Journal.Count;

        incident.UpdateForceUnit(clock, op, unit.Id, "Alarmiert", "über DLK angefordert");

        Assert.Equal(before, incident.Journal.Count);
        Assert.Equal("über DLK angefordert", incident.Forces[0].Notes);
    }

    [Fact]
    public void Re_selecting_the_same_status_logs_nothing()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9, status: "Alarmiert");
        var before = incident.Journal.Count;

        incident.UpdateForceUnit(clock, op, unit.Id, "  Alarmiert  ", null);

        // Trimming happens before the comparison, so whitespace alone is not a transition.
        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void A_closed_incident_logs_nothing_because_it_throws_first()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9);
        incident.Close(clock, op);
        var after = incident.Journal.Count;

        Assert.Throws<IncidentClosedException>(
            () => incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", null));
        Assert.Equal(after, incident.Journal.Count);
    }

    [Fact]
    public void Add_journal_entry_appends_with_clock_timestamp()
    {
        var incident = NewIncident(out var clock, out var op);
        clock.Now = T0.AddMinutes(5);

        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung", from: "ILS");

        // Journal[0] is the automatic "Einsatz begonnen" entry from Incident.Start.
        Assert.Equal(entry, incident.Journal[^1]);
        Assert.Equal(T0.AddMinutes(5), entry.Timestamp);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
    }

    [Fact]
    public void Add_journal_entry_rejects_an_undefined_direction()
    {
        // Direction rides the wire as a plain integer (a synced AddJournalEntryCommand), so a
        // forged out-of-range value must not reach EtbEntry.Create (security review, #73).
        var incident = NewIncident(out var clock, out var op);
        Assert.Throws<ArgumentException>(
            () => incident.AddJournalEntry(clock, op, (EtbDirection)99, "Gefälscht"));
    }

    [Fact]
    public void Add_journal_entry_still_accepts_the_System_direction()
    {
        // ScbaViewModel's trupp-lifecycle logging legitimately calls this same method with
        // EtbDirection.System (not just the private AppendSystemEntry helper) -- the undefined-value
        // guard above must not reject a value this codebase already produces through it.
        var incident = NewIncident(out var clock, out var op);
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.System, "Atemschutz-Trupp registriert");
        Assert.Equal(EtbDirection.System, entry.Direction);
    }

    [Fact]
    public void Edit_journal_entry_appends_a_system_trace_of_the_correction()
    {
        // A correction to the journal is itself a reportable event: without this trace, an edit
        // would leave no sign in the grid or the PDF export that the original wording ever existed
        // (security review, #73).
        var incident = NewIncident(out var clock, out var op);
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung");
        clock.Now = T0.AddMinutes(5);

        incident.EditJournalEntry(clock, op, entry.Id, "Lagemeldung korrigiert");

        var trace = Assert.Single(incident.Journal, e => e.Text.Contains("bearbeitet", StringComparison.Ordinal));
        Assert.Equal(EtbDirection.System, trace.Direction);
        Assert.Equal(T0.AddMinutes(5), trace.Timestamp);
    }

    [Fact]
    public void Editing_an_entry_with_its_own_unchanged_text_appends_no_system_trace()
    {
        var incident = NewIncident(out var clock, out var op);
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung");
        var before = incident.Journal.Count;

        incident.EditJournalEntry(clock, op, entry.Id, "Lagemeldung");

        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void Edit_journal_entry_replaces_text_and_records_the_prior_version()
    {
        var incident = NewIncident(out var clock, out var op);
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung", from: "ILS");
        clock.Now = T0.AddMinutes(5);
        var editor = new SessionOperator("Schmidt");

        var edited = incident.EditJournalEntry(clock, editor, entry.Id, "Lagemeldung korrigiert");

        Assert.Equal("Lagemeldung korrigiert", edited.Text);
        Assert.Equal(edited, incident.Journal.Single(e => e.Id == entry.Id));
        var historyEntry = Assert.Single(edited.Edits);
        Assert.Equal("Lagemeldung", historyEntry.PreviousText);
        Assert.Equal("Schmidt", historyEntry.EditedBy);
        Assert.Equal(T0.AddMinutes(5), historyEntry.EditedAt);
    }

    [Fact]
    public void Edit_journal_entry_throws_on_a_System_entry()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.ResumeEditing(clock, op);
        var systemEntry = incident.Journal.Single(e => e.Text == "Bearbeitung fortgesetzt");

        Assert.Throws<InvalidOperationException>(
            () => incident.EditJournalEntry(clock, op, systemEntry.Id, "Manipuliert"));
    }

    [Fact]
    public void Edit_journal_entry_throws_when_entry_not_found()
    {
        var incident = NewIncident(out var clock, out var op);
        Assert.Throws<KeyNotFoundException>(
            () => incident.EditJournalEntry(clock, op, Guid.NewGuid(), "Text"));
    }

    [Fact]
    public void Edit_journal_entry_throws_when_incident_closed()
    {
        var incident = NewIncident(out var clock, out var op);
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(
            () => incident.EditJournalEntry(clock, op, entry.Id, "Korrigiert"));
    }

    [Fact]
    public void Resume_editing_logs_etb_entry()
    {
        var incident = NewIncident(out var clock, out var op);
        clock.Now = T0.AddHours(1);

        incident.ResumeEditing(clock, op);

        var entry = Assert.Single(incident.Journal, e => e.Text == "Bearbeitung fortgesetzt");
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal(T0.AddHours(1), entry.Timestamp);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
    }

    [Fact]
    public void Total_personnel_sums_force_units()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AddForceUnit(clock, op, "FFB", 12);
        incident.AddForceUnit(clock, op, "Emmering", 9);
        Assert.Equal(21, incident.TotalPersonnel);
    }

    [Fact]
    public void Total_scba_sums_the_agt_of_every_unit()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AddForceUnit(clock, op, "FFB Wache 1", 12, scbaCount: 6);
        incident.AddForceUnit(clock, op, "Emmering", 9, scbaCount: 4);
        incident.AddForceUnit(clock, op, "Aich", 5);

        Assert.Equal(26, incident.TotalPersonnel);
        Assert.Equal(10, incident.TotalScba);
    }

    [Fact]
    public void A_force_unit_records_status_and_notes()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AddForceUnit(clock, op, "FFB Wache 1", 9, status: " Im Einsatz ", notes: " über DLK ");

        var unit = Assert.Single(incident.Forces);
        Assert.Equal("Im Einsatz", unit.Status);
        Assert.Equal("über DLK", unit.Notes);
    }

    [Fact]
    public void Agt_cannot_outnumber_the_crew()
    {
        var incident = NewIncident(out var clock, out var op);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AddForceUnit(clock, op, "FFB Wache 1", 4, scbaCount: 5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AddForceUnit(clock, op, "FFB Wache 1", 4, scbaCount: -1));
        Assert.Empty(incident.Forces);
    }

    [Fact]
    public void Every_crew_member_may_be_an_agt()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AddForceUnit(clock, op, "FFB Wache 1", 4, scbaCount: 4);
        Assert.Equal(4, Assert.Single(incident.Forces).ScbaCount);
    }

    [Fact]
    public void Assign_role_appends()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AssignRole(clock, op, "EL", "Müller", callSign: "FFB 12/1");
        Assert.Equal("EL", Assert.Single(incident.Roles).Role);
    }

    [Fact]
    public void Assign_role_records_section_and_phone()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AssignRole(clock, op, "EL", "Müller", section: "  Abschnitt Nord  ", phone: " 01 71 / 1 23 45 67 ");

        var role = Assert.Single(incident.Roles);
        Assert.Equal("Abschnitt Nord", role.Section);
        Assert.Equal("01 71 / 1 23 45 67", role.Phone);
    }

    [Fact]
    public void Blank_section_and_phone_become_null_rather_than_empty()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AssignRole(clock, op, "EL", "Müller", section: "   ", phone: string.Empty);

        var role = Assert.Single(incident.Roles);
        Assert.Null(role.Section);
        Assert.Null(role.Phone);
    }

    // Mirrors AddForceUnit/TransferRole: creating a new role assignment is always a reportable
    // event, so -- unlike EditRolePhone's "only on a real change" rule -- this logs unconditionally.
    [Fact]
    public void Assigning_a_role_logs_the_new_assignment()
    {
        var incident = NewIncident(out var clock, out var op);
        var before = incident.Journal.Count;

        incident.AssignRole(clock, op, "EL", "Müller");

        Assert.Equal(before + 1, incident.Journal.Count);
        Assert.Equal("Funktion EL zugewiesen: Müller", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void Ending_a_role_assignment_stamps_bis_in_place()
    {
        var incident = NewIncident(out var clock, out var op);
        var assigned = incident.AssignRole(clock, op, "EL", "Müller", from: clock.Now);

        var ended = incident.EndRoleAssignment(assigned.Id, clock.Now.AddMinutes(30));

        Assert.Equal(clock.Now.AddMinutes(30), ended.To);

        // Assignments are immutable records, so ending one replaces it -- the aggregate must not
        // grow a second entry, and the surviving one must be the ended copy.
        Assert.Equal(ended, Assert.Single(incident.Roles));
        Assert.Equal(assigned.Id, ended.Id);
    }

    [Fact]
    public void Ending_an_unknown_role_assignment_is_rejected()
    {
        var incident = NewIncident(out var clock, out _);
        Assert.Throws<ArgumentException>(() => incident.EndRoleAssignment(Guid.NewGuid(), clock.Now));
    }

    [Fact]
    public void Ending_an_already_ended_role_assignment_is_rejected()
    {
        var incident = NewIncident(out var clock, out var op);
        var assigned = incident.AssignRole(clock, op, "EL", "Müller", from: clock.Now);
        incident.EndRoleAssignment(assigned.Id, clock.Now.AddMinutes(30));

        // The Bis time records when a handover actually happened; pressing the button again must
        // not quietly rewrite it.
        Assert.Throws<InvalidOperationException>(
            () => incident.EndRoleAssignment(assigned.Id, clock.Now.AddMinutes(45)));
        Assert.Equal(clock.Now.AddMinutes(30), Assert.Single(incident.Roles).To);
    }

    [Fact]
    public void A_role_assignment_cannot_end_before_it_began()
    {
        var incident = NewIncident(out var clock, out var op);
        var assigned = incident.AssignRole(clock, op, "EL", "Müller", from: clock.Now);

        Assert.Throws<ArgumentException>(
            () => incident.EndRoleAssignment(assigned.Id, clock.Now.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(
            () => RoleAssignment.Create("EL", "Müller", from: clock.Now, to: clock.Now.AddMinutes(-1)));
    }

    [Fact]
    public void Closing_an_incident_ends_every_running_role_assignment()
    {
        var incident = NewIncident(out var clock, out var op);
        var running = incident.AssignRole(clock, op, "EL", "Müller", from: clock.Now);
        var alreadyEnded = incident.AssignRole(clock, op, "ZF", "Huber", from: clock.Now);
        incident.EndRoleAssignment(alreadyEnded.Id, clock.Now.AddMinutes(10));

        clock.Now = clock.Now.AddMinutes(30);
        incident.Close(clock, op);

        var closedRunning = incident.Roles.Single(r => r.Id == running.Id);
        Assert.Equal(clock.Now, closedRunning.To);

        // An assignment that was already ended before the close keeps its original Bis time.
        var closedAlreadyEnded = incident.Roles.Single(r => r.Id == alreadyEnded.Id);
        Assert.Equal(clock.Now.AddMinutes(-20), closedAlreadyEnded.To);
    }

    [Fact]
    public void Transferring_a_role_ends_the_old_assignment_and_starts_a_new_one()
    {
        var incident = NewIncident(out var clock, out var op);
        var original = incident.AssignRole(clock, op, "EL", "Müller", callSign: "FFB 12/1", from: clock.Now, section: "Abschnitt Nord");

        clock.Now = clock.Now.AddMinutes(15);
        var next = incident.TransferRole(clock, op, original.Id, "Schmidt", "FFB 12/2", "0171");

        var ended = incident.Roles.Single(r => r.Id == original.Id);
        Assert.Equal(clock.Now, ended.To);

        Assert.Equal("EL", next.Role);
        Assert.Equal("Schmidt", next.PersonName);
        Assert.Equal("FFB 12/2", next.CallSign);
        Assert.Equal("0171", next.Phone);
        Assert.Equal("Abschnitt Nord", next.Section);
        Assert.Equal(clock.Now, next.From);
        Assert.Null(next.To);
        Assert.Equal(2, incident.Roles.Count);

        Assert.Equal("Funktion EL übergeben: Müller → Schmidt", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void Transferring_an_unknown_role_assignment_is_rejected()
    {
        var incident = NewIncident(out var clock, out var op);
        Assert.Throws<ArgumentException>(
            () => incident.TransferRole(clock, op, Guid.NewGuid(), "Schmidt", null, null));
    }

    [Fact]
    public void Transferring_an_already_ended_role_assignment_is_rejected()
    {
        var incident = NewIncident(out var clock, out var op);
        var original = incident.AssignRole(clock, op, "EL", "Müller", from: clock.Now);
        incident.EndRoleAssignment(original.Id, clock.Now.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(
            () => incident.TransferRole(clock, op, original.Id, "Schmidt", null, null));
    }

    [Fact]
    public void Editing_a_role_phone_number_logs_the_change()
    {
        var incident = NewIncident(out var clock, out var op);
        var assigned = incident.AssignRole(clock, op, "EL", "Müller", phone: "0171");
        var before = incident.Journal.Count;

        var updated = incident.EditRolePhone(clock, op, assigned.Id, "0172");

        Assert.Equal("0172", updated.Phone);
        Assert.Equal(before + 1, incident.Journal.Count);
        Assert.Equal("Handynummer für EL (Müller) geändert: 0171 → 0172", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void Resaving_the_same_role_phone_number_does_not_log_anything()
    {
        var incident = NewIncident(out var clock, out var op);
        var assigned = incident.AssignRole(clock, op, "EL", "Müller", phone: "0171");
        var before = incident.Journal.Count;

        incident.EditRolePhone(clock, op, assigned.Id, " 0171 ");

        Assert.Equal(before, incident.Journal.Count);
    }
}
