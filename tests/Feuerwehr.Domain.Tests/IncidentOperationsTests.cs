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
        Assert.Equal(EtbDirection.Internal, entry.Direction);
        Assert.Equal(T0.AddHours(1), entry.Timestamp);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
    }

    [Fact]
    public void Total_personnel_sums_force_units()
    {
        var incident = NewIncident(out _, out _);
        incident.AddForceUnit("FFB", 12);
        incident.AddForceUnit("Emmering", 9);
        Assert.Equal(21, incident.TotalPersonnel);
    }

    [Fact]
    public void Total_scba_sums_the_agt_of_every_unit()
    {
        var incident = NewIncident(out _, out _);
        incident.AddForceUnit("FFB Wache 1", 12, scbaCount: 6);
        incident.AddForceUnit("Emmering", 9, scbaCount: 4);
        incident.AddForceUnit("Aich", 5);

        Assert.Equal(26, incident.TotalPersonnel);
        Assert.Equal(10, incident.TotalScba);
    }

    [Fact]
    public void A_force_unit_records_status_and_notes()
    {
        var incident = NewIncident(out _, out _);
        incident.AddForceUnit("FFB Wache 1", 9, status: " Im Einsatz ", notes: " über DLK ");

        var unit = Assert.Single(incident.Forces);
        Assert.Equal("Im Einsatz", unit.Status);
        Assert.Equal("über DLK", unit.Notes);
    }

    [Fact]
    public void Agt_cannot_outnumber_the_crew()
    {
        var incident = NewIncident(out _, out _);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AddForceUnit("FFB Wache 1", 4, scbaCount: 5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AddForceUnit("FFB Wache 1", 4, scbaCount: -1));
        Assert.Empty(incident.Forces);
    }

    [Fact]
    public void Every_crew_member_may_be_an_agt()
    {
        var incident = NewIncident(out _, out _);
        incident.AddForceUnit("FFB Wache 1", 4, scbaCount: 4);
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
