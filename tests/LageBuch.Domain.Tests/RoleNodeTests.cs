using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class RoleNodeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    [Fact]
    public void Assigning_into_a_node_sets_section_to_the_node_name()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var role = incident.AssignRole(
            clock, op, "Abschnittsleiter", "Huber", section: "Süd", nodeId: node.Id, slot: RoleSlot.Leiter);

        Assert.Equal(node.Id, role.NodeId);
        Assert.Equal(RoleSlot.Leiter, role.Slot);
        Assert.Equal("Nord", role.Section);
    }

    [Fact]
    public void Assigning_into_a_dissolved_node_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, node.Id);

        var ex = Assert.Throws<InvalidOperationException>(
            () => incident.AssignRole(clock, op, "Abschnittsleiter", "Huber", nodeId: node.Id));

        Assert.Equal("Abschnitt ist aufgelöst.", ex.Message);
    }

    [Fact]
    public void Assigning_into_an_unknown_node_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(
            () => incident.AssignRole(clock, op, "Abschnittsleiter", "Huber", nodeId: Guid.NewGuid()));
    }

    [Fact]
    public void Transfer_keeps_node_and_slot()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var first = incident.AssignRole(
            clock, op, "Abschnittsleiter", "Huber", nodeId: node.Id, slot: RoleSlot.Leiter);

        var next = incident.TransferRole(clock, op, first.Id, "Maier", null, null);

        Assert.Equal(node.Id, next.NodeId);
        Assert.Equal(RoleSlot.Leiter, next.Slot);
        Assert.Equal("Nord", next.Section);
    }

    [Fact]
    public void Renaming_a_node_renames_running_assignments_only()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var first = incident.AssignRole(clock, op, "Abschnittsleiter", "Huber", nodeId: node.Id);
        var running = incident.TransferRole(clock, op, first.Id, "Maier", null, null);

        incident.UpdateCommandNode(clock, op, node.Id, "Nordost", null, null, null, null, null);

        Assert.Equal("Nordost", incident.Roles.Single(r => r.Id == running.Id).Section);
        Assert.Equal("Nord", incident.Roles.Single(r => r.Id == first.Id).Section);
    }

    [Fact]
    public void Slot_defaults_to_none()
    {
        var (incident, clock, op) = NewIncident();

        var role = incident.AssignRole(clock, op, "EL", "Huber");

        Assert.Equal(RoleSlot.None, role.Slot);
        Assert.Null(role.NodeId);
    }

    [Fact]
    public void Dissolving_a_node_with_a_running_leiter_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var role = incident.AssignRole(
            clock, op, "Abschnittsleiter", "Huber", nodeId: node.Id, slot: RoleSlot.Leiter);

        var ex = Assert.Throws<InvalidOperationException>(
            () => incident.DissolveCommandNode(clock, op, node.Id));
        Assert.Equal("Abschnitt ist nicht leer.", ex.Message);

        incident.EndRoleAssignment(role.Id, clock.Now);
        Assert.False(incident.DissolveCommandNode(clock, op, node.Id).IsLive);
    }
}
