using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class CommandNodeAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static EtbEntry Last(Incident incident) => incident.Journal[^1];

    [Fact]
    public void Adding_a_node_with_details_records_them_and_logs_auftrag_and_funkgruppe()
    {
        var (incident, clock, op) = NewIncident();
        var before = incident.Journal.Count;

        var node = incident.AddCommandNode(
            clock,
            op,
            CommandNodeKind.Abschnitt,
            "Nord",
            null,
            auftrag: " Brandbekämpfung ",
            standort: "Hauptstraße 1",
            funkgruppe: "TG 2",
            rufname: "Florian Nord",
            telefon: "0171 1234567");

        Assert.Equal("Brandbekämpfung", node.Auftrag);
        Assert.Equal("Hauptstraße 1", node.Standort);
        Assert.Equal("TG 2", node.Funkgruppe);
        Assert.Equal("Florian Nord", node.Rufname);
        Assert.Equal("0171 1234567", node.Telefon);
        Assert.Same(node, incident.FindCommandNode(node.Id));
        var lines = incident.Journal.Skip(before).ToList();
        Assert.Equal(
            new[] { "Abschnitt gebildet: Nord", "Auftrag Nord: Brandbekämpfung", "Funkgruppe Nord: TG 2" },
            lines.Select(e => e.Text).ToArray());
        Assert.All(lines, e => Assert.Equal(EtbDirection.System, e.Direction));
    }

    [Fact]
    public void Adding_a_node_with_an_oversized_auftrag_is_refused_and_changes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var nodes = incident.CommandNodes.Count;
        var entries = incident.Journal.Count;

        Assert.Throws<ArgumentException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord", null, auftrag: new string('x', 1001)));

        Assert.Equal(nodes, incident.CommandNodes.Count);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Adding_a_node_with_blank_details_logs_only_the_gebildet_line()
    {
        var (incident, clock, op) = NewIncident();
        var before = incident.Journal.Count;

        var node = incident.AddCommandNode(
            clock,
            op,
            CommandNodeKind.Abschnitt,
            "Nord",
            null,
            auftrag: "  ",
            standort: string.Empty,
            funkgruppe: " ");

        Assert.Null(node.Auftrag);
        Assert.Null(node.Funkgruppe);
        Assert.Equal("Abschnitt gebildet: Nord", Assert.Single(incident.Journal.Skip(before)).Text);
    }

    [Fact]
    public void Adding_an_abschnitt_logs_abschnitt_gebildet()
    {
        var (incident, clock, op) = NewIncident();

        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        Assert.Same(node, Assert.Single(incident.CommandNodes));
        Assert.Same(node, incident.FindCommandNode(node.Id));
        Assert.Equal("Abschnitt gebildet: Nord", Last(incident).Text);
        Assert.Equal(EtbDirection.System, Last(incident).Direction);
    }

    [Fact]
    public void Adding_an_unterabschnitt_logs_its_parent()
    {
        var (incident, clock, op) = NewIncident();
        var parent = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var child = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", parent.Id);

        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal("Unterabschnitt gebildet: Wasser in Nord", Last(incident).Text);
    }

    [Fact]
    public void Adding_a_bereitstellung_logs_bereitstellung_eingerichtet()
    {
        var (incident, clock, op) = NewIncident();

        incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR 1");

        Assert.Equal("Bereitstellung eingerichtet: BR 1", Last(incident).Text);
    }

    [Fact]
    public void Adding_a_partner_logs_einsatzleitung_eingetragen()
    {
        var (incident, clock, op) = NewIncident();

        incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");

        Assert.Equal("Einsatzleitung eingetragen: Polizei", Last(incident).Text);
    }

    [Fact]
    public void Unterabschnitt_under_a_bereitstellung_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var bereitstellung = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "BR 1");

        Assert.Throws<ArgumentException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", bereitstellung.Id));
        Assert.Single(incident.CommandNodes);
    }

    [Fact]
    public void Unterabschnitt_under_a_dissolved_abschnitt_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var parent = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, parent.Id);

        Assert.Throws<ArgumentException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", parent.Id));
    }

    [Fact]
    public void Unterabschnitt_under_an_unknown_parent_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", Guid.NewGuid()));
    }

    [Fact]
    public void Two_live_abschnitte_cannot_share_a_name_ignoring_case()
    {
        var (incident, clock, op) = NewIncident();
        incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var ex = Assert.Throws<InvalidOperationException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, " nord "));

        Assert.Equal("Name ist bereits vergeben.", ex.Message);
    }

    [Fact]
    public void Top_level_nodes_of_different_kinds_share_one_namespace()
    {
        var (incident, clock, op) = NewIncident();
        incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        Assert.Throws<InvalidOperationException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Nord"));
    }

    [Fact]
    public void Unterabschnitte_of_different_abschnitte_may_share_a_name()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", nord.Id);

        incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", sued.Id);

        Assert.Equal(4, incident.CommandNodes.Count);
    }

    [Fact]
    public void Unterabschnitte_of_one_abschnitt_cannot_share_a_name()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", nord.Id);

        Assert.Throws<InvalidOperationException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "WASSER", nord.Id));
    }

    [Fact]
    public void A_dissolved_name_can_be_reused()
    {
        var (incident, clock, op) = NewIncident();
        var first = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, first.Id);

        var second = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Renaming_logs_the_old_and_new_name()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var updated = incident.UpdateCommandNode(clock, op, node.Id, "Nordwest", null, null, null, null, null);

        Assert.Equal("Nordwest", updated.Name);
        Assert.Same(updated, incident.CommandNodes[0]);
        Assert.Equal("Abschnitt umbenannt: Nord → Nordwest", Last(incident).Text);
    }

    [Fact]
    public void Renaming_a_partner_labels_it_einsatzleitung()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");

        incident.UpdateCommandNode(clock, op, node.Id, "Polizei Bund", null, null, null, null, null);

        Assert.Equal("Einsatzleitung umbenannt: Polizei → Polizei Bund", Last(incident).Text);
    }

    [Fact]
    public void Renaming_onto_a_live_sibling_name_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");

        Assert.Throws<InvalidOperationException>(
            () => incident.UpdateCommandNode(clock, op, sued.Id, "nord", null, null, null, null, null));
    }

    [Fact]
    public void Renaming_to_its_own_name_in_another_case_is_allowed()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var updated = incident.UpdateCommandNode(clock, op, node.Id, "NORD", null, null, null, null, null);

        Assert.Equal("NORD", updated.Name);
    }

    [Fact]
    public void Setting_and_clearing_the_auftrag_is_logged()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        incident.UpdateCommandNode(clock, op, node.Id, "Nord", "Brandbekämpfung", null, null, null, null);
        Assert.Equal("Auftrag Nord: Brandbekämpfung", Last(incident).Text);

        incident.UpdateCommandNode(clock, op, node.Id, "Nord", null, null, null, null, null);
        Assert.Equal("Auftrag Nord aufgehoben", Last(incident).Text);
    }

    [Fact]
    public void Setting_the_funkgruppe_is_logged()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        incident.UpdateCommandNode(clock, op, node.Id, "Nord", null, null, "TG 3", null, null);

        Assert.Equal("Funkgruppe Nord: TG 3", Last(incident).Text);
    }

    [Fact]
    public void Standort_rufname_and_telefon_changes_are_silent()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var before = incident.Journal.Count;

        var updated = incident.UpdateCommandNode(
            clock, op, node.Id, "Nord", null, "Hauptstr. 1", null, "Florian Nord", "0171 0000001");

        Assert.Equal(before, incident.Journal.Count);
        Assert.Equal("Hauptstr. 1", updated.Standort);
        Assert.Equal("Florian Nord", updated.Rufname);
        Assert.Equal("0171 0000001", updated.Telefon);
    }

    [Fact]
    public void An_unchanged_update_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var before = incident.Journal.Count;

        incident.UpdateCommandNode(clock, op, node.Id, "Nord", null, null, null, null, null);

        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void One_update_writes_one_line_per_changed_field_in_table_order()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var before = incident.Journal.Count;

        incident.UpdateCommandNode(clock, op, node.Id, "Nordwest", "Löschen", null, "TG 3", null, null);

        var lines = incident.Journal.Skip(before).Select(e => e.Text).ToArray();
        Assert.Equal(
            new[]
            {
                "Abschnitt umbenannt: Nord → Nordwest",
                "Auftrag Nordwest: Löschen",
                "Funkgruppe Nordwest: TG 3",
            },
            lines);
    }

    [Fact]
    public void Updating_a_dissolved_node_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, node.Id);

        var ex = Assert.Throws<InvalidOperationException>(
            () => incident.UpdateCommandNode(clock, op, node.Id, "Nord", null, null, null, null, null));

        Assert.Equal("Abschnitt ist aufgelöst.", ex.Message);
    }

    [Fact]
    public void Updating_an_unknown_node_throws_key_not_found()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(
            () => incident.UpdateCommandNode(clock, op, Guid.NewGuid(), "x", null, null, null, null, null));
    }

    [Fact]
    public void Dissolving_logs_and_keeps_the_node_as_history()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var dissolved = incident.DissolveCommandNode(clock, op, node.Id);

        Assert.False(dissolved.IsLive);
        Assert.Equal(T0, dissolved.DissolvedAt);
        Assert.Single(incident.CommandNodes);
        Assert.Equal("Abschnitt aufgelöst: Nord", Last(incident).Text);
    }

    [Fact]
    public void Dissolving_twice_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, node.Id);

        Assert.Throws<InvalidOperationException>(() => incident.DissolveCommandNode(clock, op, node.Id));
    }

    [Fact]
    public void Dissolving_a_node_with_a_unit_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(clock, op, "FFB", 6);
        incident.MoveForceUnit(clock, op, unit.Id, node.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => incident.DissolveCommandNode(clock, op, node.Id));

        Assert.Equal("Abschnitt ist nicht leer.", ex.Message);
        Assert.True(incident.FindCommandNode(node.Id)!.IsLive);
    }

    [Fact]
    public void Dissolving_an_abschnitt_with_a_live_unterabschnitt_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var child = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Wasser", node.Id);

        Assert.Throws<InvalidOperationException>(() => incident.DissolveCommandNode(clock, op, node.Id));

        incident.DissolveCommandNode(clock, op, child.Id);
        incident.DissolveCommandNode(clock, op, node.Id);
        Assert.False(incident.FindCommandNode(node.Id)!.IsLive);
    }

    [Fact]
    public void Dissolving_an_unknown_node_throws_key_not_found()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.DissolveCommandNode(clock, op, Guid.NewGuid()));
    }

    [Fact]
    public void Moving_a_unit_onto_a_node_logs_the_move_from_the_call_sign()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");

        var moved = incident.MoveForceUnit(clock, op, unit.Id, node.Id);

        Assert.Equal(node.Id, moved.NodeId);
        Assert.Same(moved, incident.Forces[0]);
        Assert.Equal("Aich (LF 10/1) → Nord", Last(incident).Text);
        Assert.Equal("LF 10/1", Last(incident).From);
        Assert.Equal(EtbDirection.System, Last(incident).Direction);
    }

    [Fact]
    public void Moving_a_unit_out_logs_abschnitt_aufgehoben()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");
        incident.MoveForceUnit(clock, op, unit.Id, node.Id);

        var moved = incident.MoveForceUnit(clock, op, unit.Id, null);

        Assert.Null(moved.NodeId);
        Assert.Equal("Aich (LF 10/1): Abschnitt aufgehoben (vorher Nord)", Last(incident).Text);
    }

    [Fact]
    public void Moving_a_unit_onto_its_own_node_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        incident.MoveForceUnit(clock, op, unit.Id, node.Id);
        var before = incident.Journal.Count;

        incident.MoveForceUnit(clock, op, unit.Id, node.Id);

        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void Moving_an_unplaced_unit_to_none_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        var before = incident.Journal.Count;

        incident.MoveForceUnit(clock, op, unit.Id, null);

        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void Moving_a_unit_onto_a_dissolved_node_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, node.Id);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        var ex = Assert.Throws<InvalidOperationException>(() => incident.MoveForceUnit(clock, op, unit.Id, node.Id));

        Assert.Equal("Abschnitt ist aufgelöst.", ex.Message);
        Assert.Null(incident.Forces[0].NodeId);
    }

    [Fact]
    public void Moving_a_unit_onto_a_partner_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var partner = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        var ex = Assert.Throws<InvalidOperationException>(() => incident.MoveForceUnit(clock, op, unit.Id, partner.Id));

        Assert.Equal("Einsatzleitungen anderer Organisationen führen keine eigenen Einheiten.", ex.Message);
    }

    [Fact]
    public void Moving_to_an_unknown_node_or_unit_throws_key_not_found()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        Assert.Throws<KeyNotFoundException>(() => incident.MoveForceUnit(clock, op, unit.Id, Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => incident.MoveForceUnit(clock, op, Guid.NewGuid(), null));
    }

    [Fact]
    public void Placement_survives_a_status_edit()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        incident.MoveForceUnit(clock, op, unit.Id, node.Id);

        incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", null);

        Assert.Equal(node.Id, incident.Forces[0].NodeId);
    }

    [Fact]
    public void Closed_incident_refuses_every_node_operation()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(
            () => incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd"));
        Assert.Throws<IncidentClosedException>(
            () => incident.UpdateCommandNode(clock, op, node.Id, "Nord", null, null, null, null, null));
        Assert.Throws<IncidentClosedException>(() => incident.DissolveCommandNode(clock, op, node.Id));
        Assert.Throws<IncidentClosedException>(() => incident.MoveForceUnit(clock, op, unit.Id, node.Id));
    }

    [Fact]
    public void Rehydrate_restores_nodes_and_unit_node_ids()
    {
        var nodeId = Guid.NewGuid();
        var node = CommandNode.Rehydrate(nodeId, CommandNodeKind.Abschnitt, "Nord", null, "Löschen", null, null, null, null, null);
        var unit = ForceUnit.Rehydrate(Guid.NewGuid(), "Aich", "LF 10/1", 6, 0, null, null, 0, nodeId: nodeId);

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
            new[] { unit },
            Array.Empty<Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<Time.IncidentTimerState>(),
            Array.Empty<Files.IncidentFile>(),
            Array.Empty<Tasks.IncidentTask>(),
            Array.Empty<CoMeasurement.Building>(),
            Array.Empty<CoMeasurement.Dwelling>(),
            Array.Empty<Involved.InvolvedParty>(),
            commandNodes: new[] { node });

        Assert.Same(node, Assert.Single(incident.CommandNodes));
        Assert.Equal(nodeId, Assert.Single(incident.Forces).NodeId);
    }

    [Fact]
    public void Force_unit_rehydrate_defaults_to_unplaced()
    {
        var unit = ForceUnit.Rehydrate(Guid.NewGuid(), "Aich", null, 6, 0, null, null, 0);

        Assert.Null(unit.NodeId);
    }

    [Fact]
    public void Adding_a_node_of_an_undefined_kind_is_refused_and_changes_nothing()
    {
        // JsonStringEnumConverter also accepts a raw integer, so a sync peer can send "kind": 99.
        var (incident, clock, op) = NewIncident();
        var entries = incident.Journal.Count;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AddCommandNode(clock, op, (CommandNodeKind)99, "Nord"));

        Assert.Empty(incident.CommandNodes);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Rehydrating_hostile_nodes_stufe_and_slots_repairs_them_and_never_throws()
    {
        var (source, _, _) = NewIncident();
        var abschnitt = Guid.NewGuid();
        var selfParented = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        var nested = Guid.NewGuid();
        var withParent = Guid.NewGuid();
        var unknownKind = Guid.NewGuid();
        var good = Guid.NewGuid();
        CommandNode Node(Guid id, CommandNodeKind kind, Guid? parent) =>
            CommandNode.Rehydrate(id, kind, id.ToString(), parent, null, null, null, null, null, null);
        var hostileRole = new RoleAssignment(Guid.NewGuid(), "AL", "Huber", null, null, null, null, null, Slot: (RoleSlot)42);

        var incident = Incident.Rehydrate(
            source.Id,
            source.StartedAt,
            source.State,
            incidentNumber: null,
            keyword: null,
            street: null,
            district: null,
            status: null,
            closedAt: null,
            closedBy: null,
            checklists: [],
            journal: [],
            roles: [hostileRole],
            forces: [],
            scbaTrupps: [],
            audit: [],
            timers: [],
            files: [],
            tasks: [],
            buildings: [],
            dwellings: [],
            involvedParties: [],
            commandNodes:
            [
                Node(abschnitt, CommandNodeKind.Abschnitt, null),
                Node(selfParented, CommandNodeKind.Unterabschnitt, selfParented),
                Node(orphan, CommandNodeKind.Unterabschnitt, Guid.NewGuid()),
                Node(nested, CommandNodeKind.Unterabschnitt, selfParented),
                Node(withParent, CommandNodeKind.Partner, abschnitt),
                Node(unknownKind, (CommandNodeKind)99, abschnitt),
                Node(good, CommandNodeKind.Unterabschnitt, abschnitt),
            ],
            fuehrungsstufe: (Fuehrungsstufe)7);

        Assert.Null(incident.Fuehrungsstufe);
        Assert.Equal(RoleSlot.None, incident.Roles.Single().Slot);
        foreach (var id in new[] { selfParented, orphan, nested, unknownKind })
        {
            Assert.Equal(CommandNodeKind.Abschnitt, incident.FindCommandNode(id)?.Kind);
            Assert.Null(incident.FindCommandNode(id)?.ParentId);
        }

        Assert.Equal(CommandNodeKind.Partner, incident.FindCommandNode(withParent)?.Kind);
        Assert.Null(incident.FindCommandNode(withParent)?.ParentId);
        Assert.Equal(abschnitt, incident.FindCommandNode(good)?.ParentId);
        Assert.Equal(CommandNodeKind.Unterabschnitt, incident.FindCommandNode(good)?.Kind);
    }

    [Fact]
    public void Same_named_unterabschnitte_under_different_abschnitte_do_not_share_a_role_holder()
    {
        var (incident, clock, op) = NewIncident();
        var one = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var two = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Abschnitt 2");
        var nordOne = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Nord", one.Id);
        var nordTwo = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Nord", two.Id);
        var holder = incident.AssignRole(clock, op, "UAL", "Huber", section: "Nord", nodeId: nordOne.Id);

        Assert.Same(holder, incident.FindRunningRoleHolder("UAL", "Nord", nordOne.Id));
        Assert.Null(incident.FindRunningRoleHolder("UAL", "Nord", nordTwo.Id));
    }

    [Fact]
    public void A_node_keyed_lookup_ignores_the_section_text_of_an_assignment_in_another_node_or_none()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.AssignRole(clock, op, "EL", "Typed", section: "Nord");

        Assert.Null(incident.FindRunningRoleHolder("EL", "Nord", nord.Id));
        Assert.NotNull(incident.FindRunningRoleHolder("EL", "Nord", null));
    }

    [Fact]
    public void Rehydrating_two_nodes_with_one_id_keeps_the_first()
    {
        var id = Guid.NewGuid();
        var first = CommandNode.Rehydrate(id, CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, null);
        var second = CommandNode.Rehydrate(id, CommandNodeKind.Abschnitt, "Süd", null, null, null, null, null, null, null);

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
            commandNodes: new[] { first, second });

        Assert.Equal("Nord", Assert.Single(incident.CommandNodes).Name);
    }

    [Fact]
    public void Placing_a_funktion_in_a_node_sets_node_section_slot_and_logs_it()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var role = incident.AssignRole(clock, op, "Einsatzleiter", "Huber");

        var placed = incident.PlaceRoleAssignment(clock, op, role.Id, node.Id, RoleSlot.Leiter);

        Assert.Equal(node.Id, placed.NodeId);
        Assert.Equal("Nord", placed.Section);
        Assert.Equal(RoleSlot.Leiter, placed.Slot);
        Assert.Equal(placed, Assert.Single(incident.Roles));
        Assert.Equal("Funktion Einsatzleiter (Huber) → Nord", Last(incident).Text);
        Assert.Equal(EtbDirection.System, Last(incident).Direction);
    }

    [Fact]
    public void Placing_a_funktion_at_the_root_clears_node_and_section()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var role = incident.AssignRole(clock, op, "Abschnittsleiter", "Huber", nodeId: node.Id, slot: RoleSlot.Leiter);

        var placed = incident.PlaceRoleAssignment(clock, op, role.Id, null, RoleSlot.Leiter);

        Assert.Null(placed.NodeId);
        Assert.Null(placed.Section);
        Assert.Equal(RoleSlot.Leiter, placed.Slot);
        Assert.Equal("Funktion Abschnittsleiter (Huber) → Einsatzleitung", Last(incident).Text);
    }

    [Fact]
    public void Placing_a_funktion_where_it_already_is_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var role = incident.AssignRole(clock, op, "Abschnittsleiter", "Huber", nodeId: node.Id, slot: RoleSlot.Leiter);
        var entries = incident.Journal.Count;

        var placed = incident.PlaceRoleAssignment(clock, op, role.Id, node.Id, RoleSlot.Leiter);

        Assert.Equal(role, placed);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Placing_a_funktion_that_is_ended_unknown_or_into_a_bad_target_is_refused_and_changes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var gone = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        incident.DissolveCommandNode(clock, op, gone.Id);
        var role = incident.AssignRole(clock, op, "Einsatzleiter", "Huber");
        var ended = incident.AssignRole(clock, op, "Atemschutzüberwacher", "Meier");
        incident.EndRoleAssignment(ended.Id, clock.Now);
        var before = incident.Roles.ToList();
        var entries = incident.Journal.Count;

        Assert.Throws<KeyNotFoundException>(() => incident.PlaceRoleAssignment(clock, op, Guid.NewGuid(), node.Id, RoleSlot.Leiter));
        Assert.Throws<InvalidOperationException>(() => incident.PlaceRoleAssignment(clock, op, ended.Id, node.Id, RoleSlot.Leiter));
        Assert.Throws<InvalidOperationException>(() => incident.PlaceRoleAssignment(clock, op, role.Id, gone.Id, RoleSlot.Leiter));
        Assert.Throws<KeyNotFoundException>(() => incident.PlaceRoleAssignment(clock, op, role.Id, Guid.NewGuid(), RoleSlot.Leiter));
        Assert.Throws<ArgumentOutOfRangeException>(() => incident.PlaceRoleAssignment(clock, op, role.Id, node.Id, (RoleSlot)42));

        Assert.Equal(before, incident.Roles.ToList());
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void Placing_a_funktion_in_a_closed_incident_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var role = incident.AssignRole(clock, op, "Einsatzleiter", "Huber");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.PlaceRoleAssignment(clock, op, role.Id, null, RoleSlot.Leiter));
    }

    [Fact]
    public void A_placed_funktion_blocks_dissolving_its_node_and_follows_its_rename()
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var role = incident.AssignRole(clock, op, "Abschnittsleiter", "Huber");
        incident.PlaceRoleAssignment(clock, op, role.Id, node.Id, RoleSlot.Leiter);

        Assert.Throws<InvalidOperationException>(() => incident.DissolveCommandNode(clock, op, node.Id));

        incident.UpdateCommandNode(clock, op, node.Id, "Nordost", null, null, null, null, null);
        Assert.Equal("Nordost", Assert.Single(incident.Roles).Section);
    }
}
