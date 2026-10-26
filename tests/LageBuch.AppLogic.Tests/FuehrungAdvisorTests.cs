using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.Tests;

public class FuehrungAdvisorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(T0);
    private static readonly SessionOperator Op = new("Muster", "FFB 12/1");

    private static Incident WithShape(int abschnitte = 0, int partner = 0, int units = 0, int bereitstellung = 0)
    {
        var incident = Incident.Start(Clock, Op);
        AddNodes(incident, CommandNodeKind.Abschnitt, abschnitte, "Abschnitt");
        AddNodes(incident, CommandNodeKind.Partner, partner, "Partner");
        AddNodes(incident, CommandNodeKind.Bereitstellung, bereitstellung, "BR");
        for (var i = 1; i <= units; i++)
        {
            incident.AddForceUnit(Clock, Op, $"FF {i}", 6);
        }

        return incident;
    }

    private static void AddNodes(Incident incident, CommandNodeKind kind, int count, string prefix)
    {
        for (var i = 1; i <= count; i++)
        {
            incident.AddCommandNode(Clock, Op, kind, $"{prefix} {i}");
        }
    }

    [Fact]
    public void Two_units_and_no_Abschnitt_is_Stufe_A()
    {
        var suggestion = FuehrungsstufeAdvisor.Suggest(WithShape(units: 2));

        Assert.Equal(Fuehrungsstufe.A, suggestion.Stufe);
        Assert.Equal("keine Abschnitte, 2 Einheiten", suggestion.Reason);
    }

    [Fact]
    public void Three_units_is_Stufe_B()
    {
        var suggestion = FuehrungsstufeAdvisor.Suggest(WithShape(units: 3));

        Assert.Equal(Fuehrungsstufe.B, suggestion.Stufe);
        Assert.Equal("keine Abschnitte, 3 Einheiten", suggestion.Reason);
    }

    [Fact]
    public void One_Abschnitt_is_Stufe_B_with_singular_wording()
    {
        var suggestion = FuehrungsstufeAdvisor.Suggest(WithShape(abschnitte: 1, units: 1));

        Assert.Equal(Fuehrungsstufe.B, suggestion.Stufe);
        Assert.Equal("1 Abschnitt, 1 Einheit", suggestion.Reason);
    }

    [Fact]
    public void An_empty_Einsatz_reads_keine_Einheiten()
    {
        var suggestion = FuehrungsstufeAdvisor.Suggest(WithShape());

        Assert.Equal(Fuehrungsstufe.A, suggestion.Stufe);
        Assert.Equal("keine Abschnitte, keine Einheiten", suggestion.Reason);
    }

    [Fact]
    public void Three_Abschnitte_is_Stufe_C()
    {
        var suggestion = FuehrungsstufeAdvisor.Suggest(WithShape(abschnitte: 3, units: 14));

        Assert.Equal(Fuehrungsstufe.C, suggestion.Stufe);
        Assert.Equal("3 Abschnitte, 14 Einheiten", suggestion.Reason);
    }

    [Fact]
    public void Two_Abschnitte_stay_Stufe_B()
    {
        Assert.Equal(Fuehrungsstufe.B, FuehrungsstufeAdvisor.Suggest(WithShape(abschnitte: 2)).Stufe);
    }

    [Fact]
    public void A_running_Fuehrungsassistent_is_Stufe_C()
    {
        var incident = WithShape(units: 1);
        incident.AssignRole(Clock, Op, "FA", "Mayer", slot: RoleSlot.Fuehrungsassistent);

        var suggestion = FuehrungsstufeAdvisor.Suggest(incident);

        Assert.Equal(Fuehrungsstufe.C, suggestion.Stufe);
        Assert.Equal("FüAss besetzt", suggestion.Reason);
    }

    [Fact]
    public void An_ended_Fuehrungsassistent_does_not_count()
    {
        var incident = WithShape(units: 1);
        var fa = incident.AssignRole(Clock, Op, "FA", "Mayer", slot: RoleSlot.Fuehrungsassistent);
        incident.EndRoleAssignment(fa.Id, T0);

        Assert.Equal(Fuehrungsstufe.A, FuehrungsstufeAdvisor.Suggest(incident).Stufe);
    }

    [Fact]
    public void An_Unterabschnitt_is_Stufe_D()
    {
        var incident = WithShape(abschnitte: 1);
        var parent = incident.CommandNodes[0];
        incident.AddCommandNode(Clock, Op, CommandNodeKind.Unterabschnitt, "Dach", parent.Id);

        var suggestion = FuehrungsstufeAdvisor.Suggest(incident);

        Assert.Equal(Fuehrungsstufe.D, suggestion.Stufe);
        Assert.Equal("Unterabschnitte gebildet", suggestion.Reason);
    }

    [Fact]
    public void Two_Partner_and_six_Abschnitte_is_Stufe_D()
    {
        var suggestion = FuehrungsstufeAdvisor.Suggest(WithShape(abschnitte: 6, partner: 2));

        Assert.Equal(Fuehrungsstufe.D, suggestion.Stufe);
        Assert.Equal("6 Abschnitte, 2 Partner-Einsatzleitungen", suggestion.Reason);
    }

    [Fact]
    public void Two_Partner_and_five_Abschnitte_stay_Stufe_C()
    {
        Assert.Equal(Fuehrungsstufe.C, FuehrungsstufeAdvisor.Suggest(WithShape(abschnitte: 5, partner: 2)).Stufe);
    }

    [Fact]
    public void A_dissolved_Abschnitt_is_not_counted()
    {
        var incident = WithShape(abschnitte: 1);
        incident.DissolveCommandNode(Clock, Op, incident.CommandNodes[0].Id);

        Assert.Equal(Fuehrungsstufe.A, FuehrungsstufeAdvisor.Suggest(incident).Stufe);
    }

    [Fact]
    public void Five_Abschnitte_raise_no_span_hint()
    {
        Assert.Empty(FuehrungsspanneHints.For(WithShape(abschnitte: 5)));
    }

    [Fact]
    public void Six_Abschnitte_raise_the_Einsatzleitung_hint()
    {
        var hint = Assert.Single(FuehrungsspanneHints.For(WithShape(abschnitte: 6)));

        Assert.Null(hint.NodeId);
        Assert.Equal("FwDV 100: höchstens 5 Einsatzabschnitte", hint.Text);
    }

    [Fact]
    public void Bereitstellung_and_Partner_are_not_counted_as_Abschnitte()
    {
        Assert.Empty(FuehrungsspanneHints.For(WithShape(abschnitte: 5, partner: 2, bereitstellung: 2)));
    }

    [Fact]
    public void Six_units_in_one_node_raise_a_node_hint()
    {
        var incident = WithShape(abschnitte: 1, units: 6);
        var node = incident.CommandNodes[0];
        foreach (var unit in incident.Forces.ToList())
        {
            incident.MoveForceUnit(Clock, Op, unit.Id, node.Id);
        }

        var hint = Assert.Single(FuehrungsspanneHints.For(incident));

        Assert.Equal(node.Id, hint.NodeId);
        Assert.Equal("Mehr als 5 Einheiten – Unterabschnitt bilden?", hint.Text);
    }

    [Fact]
    public void Five_units_in_one_node_raise_no_hint()
    {
        var incident = WithShape(abschnitte: 1, units: 5);
        var node = incident.CommandNodes[0];
        foreach (var unit in incident.Forces.ToList())
        {
            incident.MoveForceUnit(Clock, Op, unit.Id, node.Id);
        }

        Assert.Empty(FuehrungsspanneHints.For(incident));
    }

    [Fact]
    public void Partner_and_Unterabschnitt_nodes_raise_no_unit_hint()
    {
        var incident = WithShape(abschnitte: 1, partner: 1, units: 6);
        var abschnitt = incident.CommandNodes[0];
        var sub = incident.AddCommandNode(Clock, Op, CommandNodeKind.Unterabschnitt, "Dach", abschnitt.Id);
        foreach (var unit in incident.Forces.ToList())
        {
            incident.MoveForceUnit(Clock, Op, unit.Id, sub.Id);
        }

        Assert.Empty(FuehrungsspanneHints.For(incident));

        // A Partner cannot hold units through the aggregate; a foreign file can.
        var partnerNode = incident.CommandNodes.Single(n => n.Kind == CommandNodeKind.Partner);
        var foreign = Incident.Rehydrate(
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
            Array.Empty<LageBuch.Domain.ChecklistList>(),
            Array.Empty<LageBuch.Domain.Etb.EtbEntry>(),
            Array.Empty<RoleAssignment>(),
            Enumerable.Range(0, 6).Select(i => ForceUnit.Rehydrate(Guid.NewGuid(), $"FF {i}", null, 3, 0, null, null, 0, nodeId: partnerNode.Id)).ToList(),
            Array.Empty<LageBuch.Domain.Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<LageBuch.Domain.Time.IncidentTimerState>(),
            Array.Empty<LageBuch.Domain.Files.IncidentFile>(),
            Array.Empty<LageBuch.Domain.Tasks.IncidentTask>(),
            Array.Empty<LageBuch.Domain.CoMeasurement.Building>(),
            Array.Empty<LageBuch.Domain.CoMeasurement.Dwelling>(),
            Array.Empty<LageBuch.Domain.Involved.InvolvedParty>(),
            commandNodes: [partnerNode]);

        Assert.Empty(FuehrungsspanneHints.For(foreign));
    }

    [Fact]
    public void Fachberater_suggestions_follow_FwDV_100_Anlage_3()
    {
        Assert.Equal(15, FachberaterSuggestions.Roles.Count);
        Assert.Equal("Fachberater THW", FachberaterSuggestions.Roles[0]);
        Assert.Equal("Fachberater Arzt", FachberaterSuggestions.Roles[^1]);
    }
}
