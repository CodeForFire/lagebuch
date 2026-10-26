using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

// What an Abschnitt adds up to: its own units and those of its live Unterabschnitte.
public class NodeStrengthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static void Place(Incident incident, FixedClock clock, SessionOperator op, Guid nodeId, int personnel, int scba, int officers, int zugfuehrer = 0)
    {
        var unit = incident.AddForceUnit(clock, op, "FF Muster", personnel, scbaCount: scba, officerCount: officers, zugfuehrerCount: zugfuehrer);
        incident.MoveForceUnit(clock, op, unit.Id, nodeId);
    }

    [Fact]
    public void An_abschnitt_adds_up_its_own_units_and_its_unterabschnitte()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var dach = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);
        Place(incident, clock, op, nord.Id, 9, 4, 1, zugfuehrer: 1);
        Place(incident, clock, op, dach.Id, 6, 2, 1);
        incident.AddForceUnit(clock, op, "FF Elsewhere", 9, scbaCount: 4);

        var sum = NodeStrength.Of(incident, nord.Id);

        Assert.Equal((1, 2, 15, 6, 2), (sum.Zugfuehrer, sum.Officer, sum.Personnel, sum.Scba, sum.UnitCount));
        Assert.Equal("1/2/12/15", sum.Text);
        Assert.Equal("0/1/5/6", NodeStrength.Of(incident, dach.Id).Text);
    }

    [Fact]
    public void A_dissolved_unterabschnitts_units_do_not_count()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var dach = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);
        Place(incident, clock, op, nord.Id, 9, 4, 1);

        // Dissolve needs an empty node; a hostile file can still carry units on a dissolved one.
        incident.DissolveCommandNode(clock, op, dach.Id);
        var stranded = ForceUnit.Rehydrate(Guid.NewGuid(), "FF Stranded", null, 6, 2, null, null, 0, nodeId: dach.Id);
        var rehydrated = Incident.Rehydrate(
            incident.Id,
            T0,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [.. incident.Forces, stranded],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            commandNodes: incident.CommandNodes);

        Assert.Equal("0/1/8/9", NodeStrength.Of(rehydrated, nord.Id).Text);
    }

    [Fact]
    public void A_node_without_units_is_empty()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var sum = NodeStrength.Of(incident, nord.Id);

        Assert.Equal(0, sum.UnitCount);
        Assert.Equal("0/0/0/0", sum.Text);
    }
}
