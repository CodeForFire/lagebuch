using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Domain.Tests;

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
        incident.SeedChecklist(new[] { "Blaulicht aus?" });
        var id = incident.Checklist[0].Id;
        incident.Close(clock, op);
        Assert.Throws<IncidentClosedException>(() => incident.ToggleChecklistItem(id));
    }
}
