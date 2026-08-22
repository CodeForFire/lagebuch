using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

public class IncidentCloseTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident OpenIncident(out FixedClock clock, out SessionOperator op)
    {
        clock = new FixedClock(T0);
        op = new SessionOperator("Müller");
        return Incident.Start(clock, op);
    }

    [Fact]
    public void Close_sets_state_and_audit()
    {
        var incident = OpenIncident(out var clock, out var op);
        clock.Now = T0.AddHours(2);

        incident.Close(clock, op);

        Assert.Equal(IncidentState.Closed, incident.State);
        Assert.Equal(T0.AddHours(2), incident.ClosedAt);
        Assert.Equal("Müller", incident.ClosedBy);
        Assert.Contains(incident.Audit, e => e.Action == "closed");
    }

    [Fact]
    public void Closing_twice_throws()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(() => incident.Close(clock, op));
    }

    // Close flips State to Closed, and AddJournalEntry rejects a closed incident — so the
    // closing entry has to be appended before the flip. These two assertions together pin
    // that ordering; either one alone would pass with the calls in the wrong order.
    [Fact]
    public void Close_logs_closing_etb_entry_before_sealing_the_incident()
    {
        var incident = OpenIncident(out var clock, out var op);
        clock.Now = T0.AddHours(2);

        incident.Close(clock, op);

        Assert.Equal(IncidentState.Closed, incident.State);
        var entry = Assert.Single(incident.Journal, e => e.Text == "Einsatz abgeschlossen");
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal(T0.AddHours(2), entry.Timestamp);
        Assert.Equal("Müller", entry.EnteredBy);
        Assert.Equal(incident.Journal[^1], entry);
    }

    [Fact]
    public void Failed_second_close_does_not_append_a_duplicate_entry()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.Close(clock, op);
        var journalCount = incident.Journal.Count;

        Assert.Throws<IncidentClosedException>(() => incident.Close(clock, op));

        Assert.Equal(journalCount, incident.Journal.Count);
    }

    [Fact]
    public void Adding_journal_entry_after_close_throws()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(
            () => incident.AddJournalEntry(clock, op, EtbDirection.Internal, "zu spät"));
    }

    [Fact]
    public void Metadata_change_after_close_throws()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(() => incident.SetStatus("geändert"));
    }

    [Fact]
    public void Toggling_checklist_after_close_throws()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.SeedChecklist(new[] { ("Blaulicht aus?", false) }, Array.Empty<(string, bool)>());
        var id = incident.ChecklistAufbau[0].Id;
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(() => incident.ToggleChecklistItem(clock, op, id));
    }

    [Fact]
    public void Assigning_role_after_close_throws()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(() => incident.AssignRole(clock, op, "EL", "Müller"));
    }

    [Fact]
    public void Adding_force_unit_after_close_throws()
    {
        var incident = OpenIncident(out var clock, out var op);
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(() => incident.AddForceUnit(clock, op, "FFB", 12));
    }
}
