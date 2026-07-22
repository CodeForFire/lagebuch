using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Domain.Tests;

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
        var incident = NewIncident(out _, out _);
        incident.SeedChecklist(new[] { "Blaulicht aus?", "Bei ILS gemeldet?" });
        Assert.Equal(2, incident.Checklist.Count);

        var first = incident.Checklist[0];
        Assert.True(incident.ToggleChecklistItem(first.Id).IsDone);
    }

    [Fact]
    public void Toggle_unknown_item_throws()
    {
        var incident = NewIncident(out _, out _);
        Assert.Throws<KeyNotFoundException>(() => incident.ToggleChecklistItem(Guid.NewGuid()));
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
    // The Einsatztagebuch has to answer "when did which Feuerwehr arrive and change state", so
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
            "Einheit aufgenommen: FFB Wache 1 (FFB 1/40/1), Stärke 9, davon 4 AGT — Status: Alarmiert",
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
        Assert.Equal("Einheit aufgenommen: Aich, Stärke 6", entry.Text);
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
        var incident = NewIncident(out _, out _);
        incident.AssignRole("EL", "Müller", callSign: "FFB 12/1");
        Assert.Equal("EL", Assert.Single(incident.Roles).Role);
    }

    [Fact]
    public void Assign_role_records_section_and_phone()
    {
        var incident = NewIncident(out _, out _);
        incident.AssignRole("EL", "Müller", section: "  Abschnitt Nord  ", phone: " 01 71 / 1 23 45 67 ");

        var role = Assert.Single(incident.Roles);
        Assert.Equal("Abschnitt Nord", role.Section);
        Assert.Equal("01 71 / 1 23 45 67", role.Phone);
    }

    [Fact]
    public void Blank_section_and_phone_become_null_rather_than_empty()
    {
        var incident = NewIncident(out _, out _);
        incident.AssignRole("EL", "Müller", section: "   ", phone: "");

        var role = Assert.Single(incident.Roles);
        Assert.Null(role.Section);
        Assert.Null(role.Phone);
    }

    [Fact]
    public void Ending_a_role_assignment_stamps_bis_in_place()
    {
        var incident = NewIncident(out var clock, out _);
        var assigned = incident.AssignRole("EL", "Müller", from: clock.Now);

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
        var incident = NewIncident(out var clock, out _);
        var assigned = incident.AssignRole("EL", "Müller", from: clock.Now);
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
        var incident = NewIncident(out var clock, out _);
        var assigned = incident.AssignRole("EL", "Müller", from: clock.Now);

        Assert.Throws<ArgumentException>(
            () => incident.EndRoleAssignment(assigned.Id, clock.Now.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(
            () => RoleAssignment.Create("EL", "Müller", from: clock.Now, to: clock.Now.AddMinutes(-1)));
    }
}
