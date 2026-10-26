using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class NodeLabelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(T0);
    private static readonly SessionOperator Op = new("Muster", "FFB 12/1");

    [Fact]
    public void A_null_or_unknown_node_id_has_no_label()
    {
        var incident = Incident.Start(Clock, Op);

        Assert.Null(incident.NodeLabel(null));
        Assert.Null(incident.NodeLabel(Guid.NewGuid()));
    }

    [Fact]
    public void An_Abschnitt_is_labelled_with_its_name()
    {
        var incident = Incident.Start(Clock, Op);
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");

        Assert.Equal("Nord", incident.NodeLabel(nord.Id));
    }

    [Fact]
    public void An_Unterabschnitt_is_labelled_with_its_Abschnitt()
    {
        var incident = Incident.Start(Clock, Op);
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        var sub = incident.AddCommandNode(Clock, Op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);

        Assert.Equal("Nord / Dach", incident.NodeLabel(sub.Id));
    }

    [Fact]
    public void A_dissolved_parent_Abschnitt_still_prefixes_its_Unterabschnitt()
    {
        var parent = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, T0);
        var sub = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Unterabschnitt, "Dach", parent.Id, null, null, null, null, null, null);

        Assert.Equal("Nord / Dach", Rehydrated(parent, sub).NodeLabel(sub.Id));
    }

    [Fact]
    public void A_repaired_orphan_Unterabschnitt_is_labelled_as_an_Abschnitt()
    {
        var sub = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Unterabschnitt, "Dach", Guid.NewGuid(), null, null, null, null, null, null);

        Assert.Equal("Dach", Rehydrated(sub).NodeLabel(sub.Id));
    }

    private static Incident Rehydrated(params CommandNode[] nodes)
        => Incident.Rehydrate(
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
            commandNodes: nodes);

    [Fact]
    public void A_dissolved_node_is_still_labelled()
    {
        var incident = Incident.Start(Clock, Op);
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(Clock, Op, nord.Id);

        Assert.Equal("Nord", incident.NodeLabel(nord.Id));
    }
}
