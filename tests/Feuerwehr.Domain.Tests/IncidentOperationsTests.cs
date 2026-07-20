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
    public void Assign_role_appends()
    {
        var incident = NewIncident(out _, out _);
        incident.AssignRole("EL", "Müller", callSign: "FFB 12/1");
        Assert.Equal("EL", Assert.Single(incident.Roles).Role);
    }
}
