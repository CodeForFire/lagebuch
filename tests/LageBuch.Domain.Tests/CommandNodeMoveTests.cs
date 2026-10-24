using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

// Moving an Abschnitt: reordering it, hanging an Unterabschnitt under another Abschnitt, and
// promoting or demoting between the two levels.
public class CommandNodeMoveTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static string[] LiveNames(Incident incident, Guid? parentId = null) =>
        [.. incident.CommandNodes.Where(n => n.IsLive && n.ParentId == parentId && n.Kind != CommandNodeKind.Partner).Select(n => n.Name)];

    [Fact]
    public void An_abschnitt_moves_before_another_silently()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Marktplatz");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        var entries = incident.Journal.Count;

        incident.MoveCommandNode(clock, op, sued.Id, null, nord.Id);

        Assert.Equal(["Süd", "Nord", "Marktplatz"], LiveNames(incident));
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Without_a_place_a_node_goes_to_the_end()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");

        incident.MoveCommandNode(clock, op, nord.Id, null, null);

        Assert.Equal(["Süd", "Nord"], LiveNames(incident));
    }

    [Fact]
    public void A_bereitstellung_can_be_reordered()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var markt = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Marktplatz");

        incident.MoveCommandNode(clock, op, markt.Id, null, nord.Id);

        Assert.Equal(["Marktplatz", "Nord"], LiveNames(incident));
        Assert.Equal(CommandNodeKind.Bereitstellung, incident.FindCommandNode(markt.Id)!.Kind);
    }

    [Fact]
    public void An_unterabschnitt_moves_to_another_abschnitt_with_its_units_and_funktionen()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        var dach = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        incident.MoveForceUnit(clock, op, unit.Id, dach.Id);

        incident.MoveCommandNode(clock, op, dach.Id, sued.Id, null);

        var moved = incident.FindCommandNode(dach.Id)!;
        Assert.Equal((CommandNodeKind.Unterabschnitt, sued.Id), (moved.Kind, moved.ParentId));
        Assert.Equal(dach.Id, incident.Forces.Single().NodeId);
        Assert.Equal("Süd / Dach", incident.NodeLabel(dach.Id));
        Assert.Equal("Unterabschnitt Dach verschoben: Nord → Süd", incident.Journal[^1].Text);
    }

    [Fact]
    public void An_unterabschnitt_becomes_an_abschnitt()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var dach = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);

        incident.MoveCommandNode(clock, op, dach.Id, null, null);

        var moved = incident.FindCommandNode(dach.Id)!;
        Assert.Equal((CommandNodeKind.Abschnitt, (Guid?)null), (moved.Kind, moved.ParentId));
        Assert.Equal(["Nord", "Dach"], LiveNames(incident));
        Assert.Equal("Unterabschnitt Dach als Abschnitt eingeordnet", incident.Journal[^1].Text);
    }

    [Fact]
    public void An_abschnitt_becomes_an_unterabschnitt_in_place()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var west = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "West", nord.Id);
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");

        incident.MoveCommandNode(clock, op, sued.Id, nord.Id, west.Id);

        var moved = incident.FindCommandNode(sued.Id)!;
        Assert.Equal((CommandNodeKind.Unterabschnitt, nord.Id), (moved.Kind, moved.ParentId));
        Assert.Equal(["Süd", "West"], LiveNames(incident, nord.Id));
        Assert.Equal("Abschnitt Süd als Unterabschnitt in Nord eingeordnet", incident.Journal[^1].Text);
    }

    [Fact]
    public void Moving_a_node_to_where_it_already_is_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        var before = incident.CommandNodes.ToList();
        var entries = incident.Journal.Count;

        incident.MoveCommandNode(clock, op, nord.Id, null, sued.Id);
        incident.MoveCommandNode(clock, op, sued.Id, null, null);
        incident.MoveCommandNode(clock, op, sued.Id, null, sued.Id);

        Assert.Equal(before, incident.CommandNodes);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void An_abschnitt_with_unterabschnitte_cannot_become_one()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Dach", sued.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => incident.MoveCommandNode(clock, op, sued.Id, nord.Id, null));

        Assert.Equal("Erst die Unterabschnitte verschieben.", ex.Message);
        Assert.Equal(CommandNodeKind.Abschnitt, incident.FindCommandNode(sued.Id)!.Kind);
    }

    [Theory]
    [InlineData("partner", "Eine Einsatzleitung lässt sich nicht verschieben.")]
    [InlineData("bereitstellung-into-abschnitt", "Eine Bereitstellung liegt in keinem Abschnitt.")]
    [InlineData("into-itself", "Ein Abschnitt liegt nicht in sich selbst.")]
    [InlineData("into-bereitstellung", "Ein Unterabschnitt liegt in einem bestehenden Abschnitt.")]
    [InlineData("into-dissolved", "Ein Unterabschnitt liegt in einem bestehenden Abschnitt.")]
    [InlineData("dissolved-node", "Abschnitt ist aufgelöst.")]
    [InlineData("before-a-stranger", "Dort lässt sich nichts einordnen.")]
    [InlineData("name-taken", "Name ist bereits vergeben.")]
    public void A_move_the_structure_does_not_allow_is_refused_and_changes_nothing(string scenario, string message)
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        var markt = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Marktplatz");
        var rd = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Rettungsdienst");
        var dach = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);
        var gone = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Ost");
        incident.DissolveCommandNode(clock, op, gone.Id);
        incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Süd", nord.Id);
        var before = incident.CommandNodes.ToList();
        var entries = incident.Journal.Count;

        (Guid Node, Guid? Parent, Guid? Before) move = scenario switch
        {
            "partner" => (rd.Id, null, nord.Id),
            "bereitstellung-into-abschnitt" => (markt.Id, nord.Id, null),
            "into-itself" => (nord.Id, nord.Id, null),
            "into-bereitstellung" => (dach.Id, markt.Id, null),
            "into-dissolved" => (dach.Id, gone.Id, null),
            "dissolved-node" => (gone.Id, null, null),
            "before-a-stranger" => (sued.Id, null, dach.Id),
            _ => (sued.Id, nord.Id, null),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => incident.MoveCommandNode(clock, op, move.Node, move.Parent, move.Before));

        Assert.Equal(message, ex.Message);
        Assert.Equal(before, incident.CommandNodes);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Moving_an_unknown_node_throws_key_not_found()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.MoveCommandNode(clock, op, Guid.NewGuid(), null, null));
    }

    [Fact]
    public void A_closed_incident_refuses_a_move()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.MoveCommandNode(clock, op, nord.Id, null, null));
    }

    [Fact]
    public void The_refusal_check_answers_without_moving_anything()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");

        Assert.Null(CommandNodeMoves.Refusal(incident, sued.Id, nord.Id, null));
        Assert.Equal("Ein Abschnitt liegt nicht in sich selbst.", CommandNodeMoves.Refusal(incident, nord.Id, nord.Id, null));
        Assert.Equal(CommandNodeKind.Abschnitt, incident.FindCommandNode(sued.Id)!.Kind);
    }
}
