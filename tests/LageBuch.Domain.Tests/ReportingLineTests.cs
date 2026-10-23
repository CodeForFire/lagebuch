using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class ReportingLineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(T0);
    private static readonly SessionOperator Op = new("Muster", "FFB 12/1");

    private static Incident NewIncident() => Incident.Start(Clock, Op);

    private static CommandNode Node(CommandNodeKind kind, string name, DateTimeOffset? dissolvedAt = null)
        => CommandNode.Rehydrate(Guid.NewGuid(), kind, name, null, null, null, null, null, null, dissolvedAt);

    private static Incident Rehydrated(
        IEnumerable<CommandNode> nodes, IEnumerable<RoleAssignment>? roles = null, IEnumerable<ForceUnit>? forces = null)
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
            roles ?? Array.Empty<RoleAssignment>(),
            forces ?? Array.Empty<ForceUnit>(),
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
    public void An_empty_incident_has_only_the_Einsatzleitung()
    {
        var entry = Assert.Single(ReportingLine.Of(NewIncident()));

        Assert.Equal(ReportingEntryKind.Einsatzleitung, entry.Kind);
        Assert.Equal(0, entry.Depth);
        Assert.Null(entry.Node);
        Assert.Null(entry.Leiter);
        Assert.Empty(entry.Others);
        Assert.Empty(entry.Units);
    }

    [Fact]
    public void Entries_follow_the_Fuehrungsorganisation_order()
    {
        var incident = NewIncident();
        var bereit = incident.AddCommandNode(Clock, Op, CommandNodeKind.Bereitstellung, "BR Emmering");
        var eins = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        var partner = incident.AddCommandNode(Clock, Op, CommandNodeKind.Partner, "Polizei");
        var zwei = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Süd");
        var sub = incident.AddCommandNode(Clock, Op, CommandNodeKind.Unterabschnitt, "Dach", eins.Id);
        incident.AssignRole(Clock, Op, "Fachberater THW", "Gruber", slot: RoleSlot.Fachberater);

        var entries = ReportingLine.Of(incident);

        Assert.Equal(
            [
                (ReportingEntryKind.Einsatzleitung, 0),
                (ReportingEntryKind.Bereitstellung, 1),
                (ReportingEntryKind.Abschnitt, 1),
                (ReportingEntryKind.Unterabschnitt, 2),
                (ReportingEntryKind.Abschnitt, 1),
                (ReportingEntryKind.Partner, 0),
                (ReportingEntryKind.Fachberater, 0),
            ],
            entries.Select(e => (e.Kind, e.Depth)).ToArray());
        Assert.Equal(
            [bereit.Id, eins.Id, sub.Id, zwei.Id, partner.Id],
            entries.Where(e => e.Node is not null).Select(e => e.Node!.Id).ToArray());
    }

    [Fact]
    public void The_Einsatzleitung_takes_the_unplaced_Leiter_and_the_Fuehrungsassistenten()
    {
        var incident = NewIncident();
        var leiter = incident.AssignRole(Clock, Op, "EL", "Huber", slot: RoleSlot.Leiter);
        var assistent = incident.AssignRole(Clock, Op, "FA", "Mayer", slot: RoleSlot.Fuehrungsassistent);

        var el = ReportingLine.Of(incident)[0];

        Assert.Equal(leiter, el.Leiter);
        Assert.Equal(assistent, Assert.Single(el.Others));
    }

    [Fact]
    public void A_Leiter_with_a_free_text_Section_is_not_the_Einsatzleitung()
    {
        var incident = NewIncident();
        var nord = incident.AssignRole(Clock, Op, "AL", "Huber", section: "Nord", slot: RoleSlot.Leiter);

        var el = ReportingLine.Of(incident)[0];

        Assert.Null(el.Leiter);
        var section = Assert.Single(ReportingLine.FreeTextSections(incident));
        Assert.Equal("Nord", section.Section);
        Assert.Equal(nord, Assert.Single(section.Assignments));
    }

    [Fact]
    public void A_node_entry_takes_Leiter_Melder_and_the_others_with_its_node_id()
    {
        var incident = NewIncident();
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Süd");
        var leiter = incident.AssignRole(Clock, Op, "AL", "Huber", nodeId: nord.Id, slot: RoleSlot.Leiter);
        var melder = incident.AssignRole(Clock, Op, "Melder", "Mayer", nodeId: nord.Id, slot: RoleSlot.Melder);
        var other = incident.AssignRole(Clock, Op, "Sicherheit", "Bauer", nodeId: nord.Id);
        incident.AssignRole(Clock, Op, "AL", "Fischer", nodeId: sued.Id, slot: RoleSlot.Leiter);

        var entry = ReportingLine.Of(incident).Single(e => e.Node?.Id == nord.Id);

        Assert.Equal(leiter, entry.Leiter);
        Assert.Equal(melder, entry.Melder);
        Assert.Equal(other, Assert.Single(entry.Others));
    }

    [Fact]
    public void A_node_entry_lists_the_units_placed_in_it()
    {
        var incident = NewIncident();
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        var unit = incident.AddForceUnit(Clock, Op, "Emmering", 6);
        incident.AddForceUnit(Clock, Op, "Aich", 4);
        incident.MoveForceUnit(Clock, Op, unit.Id, nord.Id);

        var entry = ReportingLine.Of(incident).Single(e => e.Node?.Id == nord.Id);

        Assert.Equal("Emmering", Assert.Single(entry.Units).Brigade);
    }

    [Fact]
    public void Ended_assignments_are_ignored()
    {
        var incident = NewIncident();
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        var el = incident.AssignRole(Clock, Op, "EL", "Huber", slot: RoleSlot.Leiter);
        var al = incident.AssignRole(Clock, Op, "AL", "Mayer", nodeId: nord.Id, slot: RoleSlot.Leiter);
        var fb = incident.AssignRole(Clock, Op, "Fachberater Polizei", "Gruber", slot: RoleSlot.Fachberater);
        incident.EndRoleAssignment(el.Id, T0);
        incident.EndRoleAssignment(al.Id, T0);
        incident.EndRoleAssignment(fb.Id, T0);

        var entries = ReportingLine.Of(incident);

        Assert.Null(entries[0].Leiter);
        Assert.Null(entries.Single(e => e.Node?.Id == nord.Id).Leiter);
        Assert.DoesNotContain(entries, e => e.Kind == ReportingEntryKind.Fachberater);
    }

    [Fact]
    public void Dissolved_nodes_are_absent()
    {
        var incident = NewIncident();
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(Clock, Op, nord.Id);

        Assert.Single(ReportingLine.Of(incident));
    }

    [Fact]
    public void The_Fachberater_entry_collects_every_running_Fachberater()
    {
        var incident = NewIncident();
        var nord = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Nord");
        incident.AssignRole(Clock, Op, "Fachberater THW", "Gruber", slot: RoleSlot.Fachberater);
        incident.AssignRole(Clock, Op, "Fachberater Polizei", "Lang", nodeId: nord.Id, slot: RoleSlot.Fachberater);

        var entry = ReportingLine.Of(incident)[^1];

        Assert.Equal(ReportingEntryKind.Fachberater, entry.Kind);
        Assert.Null(entry.Node);
        Assert.Equal(2, entry.Others.Count);
    }

    [Fact]
    public void Partner_nodes_carry_no_units()
    {
        var partner = Node(CommandNodeKind.Partner, "Polizei");
        var unit = ForceUnit.Rehydrate(Guid.NewGuid(), "Aich", null, 6, 0, null, null, 0, nodeId: partner.Id);
        var incident = Rehydrated([partner], forces: [unit]);

        var entry = ReportingLine.Of(incident).Single(e => e.Node?.Id == partner.Id);

        Assert.Empty(entry.Units);
        Assert.Equal(unit, Assert.Single(ReportingLine.UnplacedUnits(incident)));
    }

    [Fact]
    public void Unplaced_units_cover_null_missing_dissolved_and_Partner_node_ids()
    {
        var live = Node(CommandNodeKind.Abschnitt, "Nord");
        var gone = Node(CommandNodeKind.Abschnitt, "Süd", T0);
        var partner = Node(CommandNodeKind.Partner, "THW");
        ForceUnit Unit(string brigade, Guid? node)
            => ForceUnit.Rehydrate(Guid.NewGuid(), brigade, null, 3, 0, null, null, 0, nodeId: node);
        var incident = Rehydrated(
            [live, gone, partner],
            forces:
            [
                Unit("Placed", live.Id),
                Unit("Null", null),
                Unit("Missing", Guid.NewGuid()),
                Unit("Dissolved", gone.Id),
                Unit("Partner", partner.Id),
            ]);

        var unplaced = ReportingLine.UnplacedUnits(incident);

        Assert.Equal(["Null", "Missing", "Dissolved", "Partner"], unplaced.Select(u => u.Brigade).ToArray());
    }

    [Fact]
    public void A_live_Unterabschnitt_under_a_dissolved_Abschnitt_stands_alone_with_its_unit_and_Leiter()
    {
        var parent = Node(CommandNodeKind.Abschnitt, "Nord", T0);
        var sub = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Unterabschnitt, "Dach", parent.Id, null, null, null, null, null, null);
        var unit = ForceUnit.Rehydrate(Guid.NewGuid(), "Aich", null, 6, 0, null, null, 0, nodeId: sub.Id);
        var leiter = RoleAssignment.Create("UAL", "Huber", nodeId: sub.Id, slot: RoleSlot.Leiter);
        var incident = Rehydrated([parent, sub], roles: [leiter], forces: [unit]);

        var entry = ReportingLine.Of(incident).Single(e => e.Node?.Id == sub.Id);

        Assert.Equal(1, entry.Depth);
        Assert.Equal(ReportingEntryKind.Unterabschnitt, entry.Kind);
        Assert.Equal(leiter, entry.Leiter);
        Assert.Equal(unit, Assert.Single(entry.Units));
        Assert.Empty(ReportingLine.UnplacedUnits(incident));
    }

    [Fact]
    public void Free_text_sections_group_spellings_and_keep_first_appearance_order()
    {
        var incident = NewIncident();
        var a = incident.AssignRole(Clock, Op, "AL", "Huber", section: "Nord");
        var b = incident.AssignRole(Clock, Op, "AL", "Mayer", section: "Ost");
        var c = incident.AssignRole(Clock, Op, "Trupp", "Bauer", section: "nord");
        incident.AssignRole(Clock, Op, "EL", "Lang");

        var sections = ReportingLine.FreeTextSections(incident);

        Assert.Equal(["Nord", "Ost"], sections.Select(s => s.Section).ToArray());
        Assert.Equal([a, c], sections[0].Assignments.ToArray());
        Assert.Equal([b], sections[1].Assignments.ToArray());
    }

    [Fact]
    public void A_role_pointing_at_a_missing_node_matches_no_entry_and_does_not_throw()
    {
        var role = RoleAssignment.Create("AL", "Huber", nodeId: Guid.NewGuid(), slot: RoleSlot.Leiter);
        var incident = Rehydrated([], roles: [role]);

        var entry = Assert.Single(ReportingLine.Of(incident));

        Assert.Null(entry.Leiter);
        Assert.Empty(ReportingLine.FreeTextSections(incident));
    }
}
