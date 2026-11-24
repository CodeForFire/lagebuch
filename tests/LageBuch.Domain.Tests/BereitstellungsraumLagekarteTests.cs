using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class BereitstellungsraumLagekarteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Parkplatz = new(48.1785, 11.2555);
    private static readonly GeoPoint Festwiese = new(48.17633, 11.25881);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static EtbEntry Last(Incident incident) => incident.Journal[^1];

    [Fact]
    public void A_Bereitstellungsraum_put_on_the_map_is_recorded_in_the_etb()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");

        var placed = incident.PlaceCommandNode(clock, op, node.Id, Parkplatz);

        Assert.Equal(Parkplatz, placed.Position);
        Assert.Same(placed, incident.FindCommandNode(node.Id));
        Assert.Equal("Bereitstellungsraum BR Nord auf der Lagekarte eingetragen", Last(incident).Text);
        Assert.Equal(EtbDirection.System, Last(incident).Direction);
    }

    [Fact]
    public void Moving_a_Bereitstellungsraum_on_the_map_writes_nothing_to_the_etb()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.PlaceCommandNode(clock, op, node.Id, Parkplatz);
        var count = incident.Journal.Count;

        var moved = incident.PlaceCommandNode(clock, op, node.Id, Festwiese);

        Assert.Equal(Festwiese, moved.Position);
        Assert.Equal(count, incident.Journal.Count);
    }

    [Fact]
    public void Taking_a_Bereitstellungsraum_off_the_map_is_recorded_and_forgets_its_sign_offset()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.PlaceCommandNode(clock, op, node.Id, Parkplatz);
        incident.MoveCommandNodeLabel(clock, op, node.Id, new LabelOffset(40, -30));

        var removed = incident.PlaceCommandNode(clock, op, node.Id, null);

        Assert.Null(removed.Position);
        Assert.Null(removed.LabelOffset);
        Assert.Equal("Bereitstellungsraum BR Nord von der Lagekarte genommen", Last(incident).Text);
    }

    [Fact]
    public void Taking_off_a_Bereitstellungsraum_that_is_not_on_the_map_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        var count = incident.Journal.Count;

        incident.PlaceCommandNode(clock, op, node.Id, null);

        Assert.Equal(count, incident.Journal.Count);
    }

    [Fact]
    public void Moving_the_pin_keeps_the_sign_s_offset()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.PlaceCommandNode(clock, op, node.Id, Parkplatz);
        incident.MoveCommandNodeLabel(clock, op, node.Id, new LabelOffset(40, -30));

        var moved = incident.PlaceCommandNode(clock, op, node.Id, Festwiese);

        Assert.Equal(new LabelOffset(40, -30), moved.LabelOffset);
    }

    [Fact]
    public void Moving_its_sign_keeps_the_position_and_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.PlaceCommandNode(clock, op, node.Id, Parkplatz);
        var count = incident.Journal.Count;

        var moved = incident.MoveCommandNodeLabel(clock, op, node.Id, new LabelOffset(40, -30));

        Assert.Equal(new LabelOffset(40, -30), moved.LabelOffset);
        Assert.Equal(Parkplatz, moved.Position);
        Assert.Equal(count, incident.Journal.Count);
    }

    [Theory]
    [InlineData(CommandNodeKind.Abschnitt)]
    [InlineData(CommandNodeKind.Partner)]
    public void Only_a_Bereitstellungsraum_goes_on_the_map(CommandNodeKind kind)
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, kind, "Nord");

        var refused = Assert.Throws<InvalidOperationException>(() => incident.PlaceCommandNode(clock, op, node.Id, Parkplatz));

        Assert.Equal("Nur ein Bereitstellungsraum steht auf der Lagekarte.", refused.Message);
        Assert.Null(incident.FindCommandNode(node.Id)?.Position);
    }

    [Fact]
    public void A_dissolved_Bereitstellungsraum_cannot_be_placed()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.DissolveCommandNode(clock, op, node.Id);

        var refused = Assert.Throws<InvalidOperationException>(() => incident.PlaceCommandNode(clock, op, node.Id, Parkplatz));

        Assert.Equal("Bereitstellungsraum ist aufgelöst.", refused.Message);
    }

    [Fact]
    public void Placing_an_unknown_node_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.PlaceCommandNode(clock, op, Guid.NewGuid(), Parkplatz));
        Assert.Throws<KeyNotFoundException>(() => incident.MoveCommandNodeLabel(clock, op, Guid.NewGuid(), null));
    }

    [Fact]
    public void A_closed_Einsatz_takes_no_Bereitstellungsraum()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.PlaceCommandNode(clock, op, node.Id, Parkplatz));
        Assert.Throws<IncidentClosedException>(() => incident.MoveCommandNodeLabel(clock, op, node.Id, null));
    }

    [Fact]
    public void Its_position_survives_a_rename_in_the_Organisation()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR Nord");
        incident.PlaceCommandNode(clock, op, node.Id, Parkplatz);
        incident.MoveCommandNodeLabel(clock, op, node.Id, new LabelOffset(40, -30));

        var renamed = incident.UpdateCommandNode(clock, op, node.Id, "BR Feuerwache", null, "Parkplatz Feuerwache", null, null, null);

        Assert.Equal(Parkplatz, renamed.Position);
        Assert.Equal(new LabelOffset(40, -30), renamed.LabelOffset);
    }

    [Fact]
    public void A_node_repaired_on_reading_keeps_its_place_on_the_map()
    {
        // A Bereitstellung never lies in an Abschnitt: the read repairs it to the top level.
        var node = CommandNode.Rehydrate(
            Guid.NewGuid(),
            CommandNodeKind.Bereitstellung,
            "BR Nord",
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            position: Parkplatz,
            labelOffset: new LabelOffset(12, 8));

        var incident = Incident.Rehydrate(
            Guid.NewGuid(),
            T0,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ChecklistList>(),
            Array.Empty<EtbEntry>(),
            Array.Empty<RoleAssignment>(),
            Array.Empty<ForceUnit>(),
            Array.Empty<Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<Time.IncidentTimerState>(),
            Array.Empty<Files.IncidentFile>(),
            Array.Empty<Tasks.IncidentTask>(),
            Array.Empty<CoMeasurement.Building>(),
            Array.Empty<CoMeasurement.Dwelling>(),
            Array.Empty<Involved.InvolvedParty>(),
            commandNodes: new[] { node });

        var read = Assert.Single(incident.CommandNodes);
        Assert.Null(read.ParentId);
        Assert.Equal(Parkplatz, read.Position);
        Assert.Equal(new LabelOffset(12, 8), read.LabelOffset);
    }
}
