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
}
