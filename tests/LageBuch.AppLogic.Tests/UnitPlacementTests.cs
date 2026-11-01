using LageBuch.AppLogic.Services;
using LageBuch.Domain.Leadership;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// #827: where Organisation places a Fahrzeug decides what the Kräfte tab shows as its status.
public class UnitPlacementTests
{
    private static readonly string[] Catalogue = ["Alarmiert", "Auf Anfahrt", "Bereitstellungsraum", "Im Einsatz"];

    [Fact]
    public void Placing_a_unit_in_a_bereitstellungsraum_sets_its_status_to_bereitstellungsraum()
    {
        var session = NewSession();
        var br = AddNode(session, CommandNodeKind.Bereitstellung, "BR Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");

        UnitPlacement.Move(session, Catalogue, unit, br);

        var placed = session.Incident.Forces.Single();
        Assert.Equal(br, placed.NodeId);
        Assert.Equal("Bereitstellungsraum", placed.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Placing_a_unit_in_an_abschnitt_sets_its_status_to_im_einsatz(bool unterabschnitt)
    {
        var session = NewSession();
        var target = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        if (unterabschnitt)
        {
            target = AddNode(session, CommandNodeKind.Unterabschnitt, "Nord-West", target);
        }

        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");

        UnitPlacement.Move(session, Catalogue, unit, target);

        Assert.Equal("Im Einsatz", session.Incident.Forces.Single().Status);
    }

    [Fact]
    public void Moving_from_bereitstellungsraum_to_abschnitt_logs_the_move_and_the_status_change()
    {
        var session = NewSession();
        var br = AddNode(session, CommandNodeKind.Bereitstellung, "BR Nord");
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        UnitPlacement.Move(session, Catalogue, unit, br);
        var before = session.Incident.Journal.Count;

        UnitPlacement.Move(session, Catalogue, unit, nord);

        var texts = session.Incident.Journal.Skip(before).Select(e => e.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.EndsWith("→ Nord", texts[0], StringComparison.Ordinal);
        Assert.EndsWith("Status Bereitstellungsraum → Im Einsatz", texts[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Unplacing_a_unit_leaves_its_status_alone()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        UnitPlacement.Move(session, Catalogue, unit, nord);

        UnitPlacement.Move(session, Catalogue, unit, null);

        var unplaced = session.Incident.Forces.Single();
        Assert.Null(unplaced.NodeId);
        Assert.Equal("Im Einsatz", unplaced.Status);
    }

    [Fact]
    public void A_unit_already_im_einsatz_gets_no_second_status_entry()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        UnitPlacement.Move(session, Catalogue, unit, nord);
        var before = session.Incident.Journal.Count;

        UnitPlacement.Move(session, Catalogue, unit, sued);

        var text = Assert.Single(session.Incident.Journal.Skip(before)).Text;
        Assert.EndsWith("→ Süd", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_a_unit_to_where_it_already_is_does_not_touch_its_status()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", nord);
        session.UpdateForceUnit(unit, "Auf Anfahrt", null);
        var before = session.Incident.Journal.Count;

        UnitPlacement.Move(session, Catalogue, unit, nord);

        Assert.Equal("Auf Anfahrt", session.Incident.Forces.Single().Status);
        Assert.Equal(before, session.Incident.Journal.Count);
    }

    [Fact]
    public void The_status_takes_the_catalogue_spelling()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");

        UnitPlacement.Move(session, ["alarmiert", "im einsatz"], unit, nord);

        Assert.Equal("im einsatz", session.Incident.Forces.Single().Status);
    }

    [Fact]
    public void An_empty_catalogue_still_gets_the_fixed_status()
    {
        var session = NewSession();
        var br = AddNode(session, CommandNodeKind.Bereitstellung, "BR Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");

        UnitPlacement.Move(session, [], unit, br);

        Assert.Equal("Bereitstellungsraum", session.Incident.Forces.Single().Status);
    }

    [Fact]
    public void The_bemerkung_survives_the_status_change()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        session.UpdateForceUnit(unit, "Auf Anfahrt", "über B2");

        UnitPlacement.Move(session, Catalogue, unit, nord);

        var placed = session.Incident.Forces.Single();
        Assert.Equal("Im Einsatz", placed.Status);
        Assert.Equal("über B2", placed.Notes);
    }

    [Fact]
    public void An_unknown_unit_or_node_surfaces_the_sessions_refusal()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");

        Assert.Throws<KeyNotFoundException>(() => UnitPlacement.Move(session, Catalogue, Guid.NewGuid(), nord));
        Assert.Throws<KeyNotFoundException>(() => UnitPlacement.Move(session, Catalogue, unit, Guid.NewGuid()));
    }

    [Fact]
    public void A_refused_move_changes_no_status()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.DissolveCommandNode(nord);
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        session.UpdateForceUnit(unit, "Auf Anfahrt", null);
        var before = session.Incident.Journal.Count;

        Assert.Throws<InvalidOperationException>(() => UnitPlacement.Move(session, Catalogue, unit, nord));

        Assert.Equal("Auf Anfahrt", session.Incident.Forces.Single().Status);
        Assert.Equal(before, session.Incident.Journal.Count);
    }
}
