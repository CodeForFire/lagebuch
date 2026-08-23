using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

// A unit can be taken back completely (#76 follow-up): the row disappears from the Kräfte
// übersicht, its Wert-Historie goes with it, and the ETB records the removal like any other
// reportable event.
public class ForceRemovalTests
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
    public void Removing_a_unit_takes_it_and_its_history_out()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 6);
        incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 1, personnelCount: 9, scbaCount: 4);

        incident.RemoveForceUnit(clock, op, unit.Id);

        Assert.Empty(incident.Forces);
        Assert.Equal((0, 0), (incident.TotalPersonnel, incident.TotalScba));
    }

    [Fact]
    public void The_removal_is_logged_with_the_unit_label()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 6, callSign: "FFB 1/40/1");
        var before = incident.Journal.Count;

        incident.RemoveForceUnit(clock, op, unit.Id);

        Assert.Equal(before + 1, incident.Journal.Count);
        var entry = incident.Journal[^1];
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal("Einheit entfernt: FFB Wache 1 (FFB 1/40/1)", entry.Text);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
        Assert.Equal(T0, entry.Timestamp);
    }

    [Fact]
    public void Totals_shrink_by_the_removed_unit_only()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.AddForceUnit(clock, op, "FFB Wache 1", 6, scbaCount: 2, officerCount: 1);
        var second = incident.AddForceUnit(clock, op, "Aich", 9, scbaCount: 3);

        incident.RemoveForceUnit(clock, op, second.Id);

        var remaining = Assert.Single(incident.Forces);
        Assert.Equal("FFB Wache 1", remaining.Brigade);
        Assert.Equal((6, 2), (incident.TotalPersonnel, incident.TotalScba));
    }

    [Fact]
    public void Removing_an_unknown_or_already_removed_unit_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        Assert.Throws<KeyNotFoundException>(() => incident.RemoveForceUnit(clock, op, Guid.NewGuid()));
        incident.RemoveForceUnit(clock, op, unit.Id);
        // Gone is gone: a replayed removal must fail loudly rather than silently no-op.
        Assert.Throws<KeyNotFoundException>(() => incident.RemoveForceUnit(clock, op, unit.Id));
    }

    [Fact]
    public void A_closed_incident_cannot_lose_units()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        clock.Now = clock.Now.AddHours(2);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.RemoveForceUnit(clock, op, unit.Id));
        Assert.Single(incident.Forces);
    }
}
