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

    [Fact]
    public void Assigning_an_undefined_slot_is_refused_and_changes_nothing()
    {
        // JsonStringEnumConverter also accepts a raw integer, so a sync peer can send "slot": 42.
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AssignRole(clock, op, "Abschnittsleiter", "Huber", slot: (RoleSlot)42));

        Assert.Empty(incident.Roles);
    }

    // --- #824: a Freitext-Abschnitt taken into the Führung structure in one step. ---
    [Fact]
    public void Adopting_a_free_text_abschnitt_forms_it_and_places_its_people_keeping_their_slots()
    {
        var (incident, clock, op) = NewIncident();
        var eal = incident.AssignRole(clock, op, "EAL", "Huber", section: "Nord", slot: RoleSlot.Leiter);
        var melder = incident.AssignRole(clock, op, "Melder", "Maier", section: "nord ", slot: RoleSlot.Melder);
        var zf = incident.AssignRole(clock, op, "ZF", "Schulz", section: "Nord");
        var elsewhere = incident.AssignRole(clock, op, "ZF", "Weber", section: "Süd");

        var node = incident.AdoptFreeTextSection(clock, op, "Nord");

        Assert.Equal(CommandNodeKind.Abschnitt, node.Kind);
        Assert.Equal("Nord", node.Name);
        Assert.Null(node.ParentId);
        RoleAssignment Now(RoleAssignment r) => incident.Roles.Single(x => x.Id == r.Id);
        Assert.Equal(node.Id, Now(eal).NodeId);
        Assert.Equal(RoleSlot.Leiter, Now(eal).Slot);
        Assert.Equal(node.Id, Now(melder).NodeId);
        Assert.Equal(RoleSlot.Melder, Now(melder).Slot);
        Assert.Equal(node.Id, Now(zf).NodeId);
        Assert.Equal(RoleSlot.None, Now(zf).Slot);
        Assert.All(incident.Roles.Where(r => r.NodeId == node.Id), r => Assert.Equal("Nord", r.Section));
        Assert.Null(incident.Roles.Single(r => r.Id == elsewhere.Id).NodeId);
        Assert.DoesNotContain(ReportingLine.FreeTextSections(incident), s => s.Section == "Nord");
        Assert.Equal("Abschnitt gebildet: Nord", incident.Journal[^2].Text);
        Assert.Equal("Abschnitt (Freitext) Nord übernommen: 3 Funktionen", incident.Journal[^1].Text);
    }

    [Fact]
    public void Adopting_into_an_abschnitt_of_that_name_reuses_it()
    {
        var (incident, clock, op) = NewIncident();
        var existing = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var eal = incident.AssignRole(clock, op, "EAL", "Huber", section: "NORD", slot: RoleSlot.Leiter);

        var node = incident.AdoptFreeTextSection(clock, op, "NORD");

        Assert.Equal(existing.Id, node.Id);
        Assert.Single(incident.CommandNodes);
        Assert.Equal(existing.Id, incident.Roles.Single(r => r.Id == eal.Id).NodeId);
        Assert.Equal("Abschnitt (Freitext) NORD übernommen: 1 Funktion", incident.Journal[^1].Text);
    }

    [Fact]
    public void Adopting_keeps_a_second_leiter_a_leiter_beside_the_one_already_there()
    {
        // ReportingLine already shows a second Leiter of a node among its members, so nothing is lost
        // and nobody's slot is rewritten behind their back.
        var (incident, clock, op) = NewIncident();
        var existing = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var held = incident.AssignRole(clock, op, "EAL", "Huber", nodeId: existing.Id, slot: RoleSlot.Leiter);
        var typed = incident.AssignRole(clock, op, "EAL", "Maier", section: "Nord", slot: RoleSlot.Leiter);

        incident.AdoptFreeTextSection(clock, op, "Nord");

        var entry = ReportingLine.Of(incident).Single(e => e.Node?.Id == existing.Id);
        Assert.Equal(held.Id, entry.Leiter?.Id);
        Assert.Contains(entry.Others, r => r.Id == typed.Id);
    }

    [Fact]
    public void Adopting_leaves_ended_assignments_where_they_were()
    {
        var (incident, clock, op) = NewIncident();
        var ended = incident.AssignRole(clock, op, "EAL", "Huber", from: clock.Now, section: "Nord", slot: RoleSlot.Leiter);
        incident.EndRoleAssignment(ended.Id, clock.Now);
        incident.AssignRole(clock, op, "EAL", "Maier", section: "Nord", slot: RoleSlot.Leiter);

        incident.AdoptFreeTextSection(clock, op, "Nord");

        var record = incident.Roles.Single(r => r.Id == ended.Id);
        Assert.Null(record.NodeId);
        Assert.Equal("Nord", record.Section);
    }

    [Fact]
    public void Adopting_a_free_text_abschnitt_nobody_holds_is_refused_and_forms_nothing()
    {
        var (incident, clock, op) = NewIncident();
        incident.AssignRole(clock, op, "EAL", "Huber", section: "Nord", slot: RoleSlot.Leiter);
        var entries = incident.Journal.Count;

        Assert.Throws<InvalidOperationException>(() => incident.AdoptFreeTextSection(clock, op, "Süd"));
        Assert.Throws<ArgumentException>(() => incident.AdoptFreeTextSection(clock, op, " "));

        Assert.Empty(incident.CommandNodes);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Adopting_onto_a_bereitstellung_of_that_name_is_refused_and_moves_nobody()
    {
        var (incident, clock, op) = NewIncident();
        incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Nord");
        var eal = incident.AssignRole(clock, op, "EAL", "Huber", section: "Nord", slot: RoleSlot.Leiter);

        Assert.Throws<InvalidOperationException>(() => incident.AdoptFreeTextSection(clock, op, "Nord"));

        Assert.Null(incident.Roles.Single(r => r.Id == eal.Id).NodeId);
        Assert.Single(incident.CommandNodes);
    }

    [Fact]
    public void Adopting_in_a_closed_incident_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        incident.AssignRole(clock, op, "EAL", "Huber", section: "Nord", slot: RoleSlot.Leiter);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AdoptFreeTextSection(clock, op, "Nord"));
    }
}
