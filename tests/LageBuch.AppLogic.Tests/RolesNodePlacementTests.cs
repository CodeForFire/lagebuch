using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// FUNKTIONEN next to the Führungstafel: a Funktion the board placed in a node shows that node's
// label and is compared by node, because Unterabschnitte under different Abschnitte may share a name.
public class RolesNodePlacementTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private sealed class Fixture
    {
        public Fixture(RoleUniqueness uniqueness)
        {
            Clock = new FixedClock(T0);
            Session = TestSession.StartNew(
                new FakeStore(),
                Clock,
                Op,
                "/x.fwincident",
                Array.Empty<(string, bool)>(),
                Array.Empty<(string, bool)>());
            Md = MasterDataSet.Empty with { Roles = new[] { new Role("UEAL", uniqueness), new Role("ZF") } };
        }

        public static SessionOperator Op { get; } = new("Müller");

        public FixedClock Clock { get; }

        public LocalIncidentSession Session { get; }

        public MasterDataSet Md { get; }

        public Guid Node(CommandNodeKind kind, string name, Guid? parent = null)
        {
            Session.Incident.AddCommandNode(Clock, Op, kind, name, parent);
            return Session.Incident.CommandNodes[^1].Id;
        }

        public void Place(string person, Guid? nodeId, string? section = null) =>
            Session.Incident.AssignRole(Clock, Op, "UEAL", person, from: T0, section: section, nodeId: nodeId, slot: nodeId is null ? RoleSlot.None : RoleSlot.Leiter);

        public RolesViewModel Vm() => new(Session, Clock, Md, () => { });
    }

    [Fact]
    public void A_placed_row_shows_the_label_of_its_node()
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        var abschnitt = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var nord = f.Node(CommandNodeKind.Unterabschnitt, "Nord", abschnitt);
        f.Place("Mustermann, Max", nord);
        f.Session.Incident.AssignRole(f.Clock, Fixture.Op, "ZF", "Musterfrau, Erika", from: T0, section: "Wache Süd");

        using var vm = f.Vm();

        var placed = vm.Roles.Single(r => r.Role == "UEAL");
        var typed = vm.Roles.Single(r => r.Role == "ZF");
        Assert.Equal("Abschnitt 1 / Nord", placed.SectionLabel);
        Assert.True(placed.IsPlaced);
        Assert.Equal("Unter FÜHRUNG zugeordnet", placed.SectionToolTip);
        Assert.Equal("Wache Süd", typed.SectionLabel);
        Assert.False(typed.IsPlaced);
        Assert.Null(typed.SectionToolTip);
    }

    [Fact]
    public void A_row_follows_the_board_when_the_funktion_is_placed_or_the_node_renamed()
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        var nord = f.Node(CommandNodeKind.Abschnitt, "Nord");
        f.Session.Incident.AssignRole(f.Clock, Fixture.Op, "UEAL", "Mustermann, Max", from: T0);
        using var vm = f.Vm();
        var row = vm.Roles.Single();
        Assert.Null(row.SectionLabel);

        f.Session.PlaceRoleAssignment(row.Id, nord, RoleSlot.Leiter);

        Assert.Same(row, vm.Roles.Single());
        Assert.Equal(nord, row.NodeId);
        Assert.Equal("Nord", row.SectionLabel);
        Assert.True(row.IsPlaced);
    }

    [Fact]
    public void Renaming_a_node_on_the_board_relabels_its_placed_row_in_place()
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        var abschnitt = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var nord = f.Node(CommandNodeKind.Unterabschnitt, "Nord", abschnitt);
        f.Place("Mustermann, Max", nord);
        using var vm = f.Vm();
        var row = vm.Roles.Single();

        f.Session.UpdateCommandNode(abschnitt, "Abschnitt 1 Brandbekämpfung", null, null, null, null, null);
        Assert.Equal("Abschnitt 1 Brandbekämpfung / Nord", row.SectionLabel);

        f.Session.UpdateCommandNode(nord, "Dach", null, null, null, null, null);
        Assert.Same(row, vm.Roles.Single());
        Assert.Equal("Abschnitt 1 Brandbekämpfung / Dach", row.SectionLabel);
    }

    [Fact]
    public void An_ended_row_keeps_its_last_label_after_its_node_is_dissolved()
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        var nord = f.Node(CommandNodeKind.Abschnitt, "Nord");
        f.Place("Mustermann, Max", nord);
        using var vm = f.Vm();
        var ended = vm.Roles.Single();

        // A handover ends the row; the successor then leaves for the Einsatzleitung, which frees the node.
        f.Session.TransferRole(ended.Id, "Musterfrau, Erika");
        var successor = f.Session.Incident.Roles.Single(r => r.To is null);
        f.Session.PlaceRoleAssignment(successor.Id, null, RoleSlot.Leiter);
        f.Session.DissolveCommandNode(nord);
        vm.ShowAllRoles = true;

        var row = vm.Roles.Single(r => r.Id == ended.Id);
        Assert.NotNull(row.To);
        Assert.Equal("Nord", row.SectionLabel);
        Assert.Null(vm.Roles.Single(r => r.Id == successor.Id).SectionLabel);
    }

    [Fact]
    public void Same_named_unterabschnitte_under_different_abschnitte_are_not_a_duplicate()
    {
        var f = new Fixture(RoleUniqueness.UniquePerSection);
        var eins = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var zwei = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 2");
        f.Place("Mustermann, Max", f.Node(CommandNodeKind.Unterabschnitt, "Nord", eins));
        f.Place("Musterfrau, Erika", f.Node(CommandNodeKind.Unterabschnitt, "Nord", zwei));

        using var vm = f.Vm();

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.False(r.IsDuplicate));
    }

    [Fact]
    public void Two_holders_in_the_same_node_are_a_duplicate()
    {
        var f = new Fixture(RoleUniqueness.UniquePerSection);
        var nord = f.Node(CommandNodeKind.Abschnitt, "Nord");
        f.Place("Mustermann, Max", nord);
        f.Place("Musterfrau, Erika", nord);

        using var vm = f.Vm();

        Assert.All(vm.Roles, r => Assert.True(r.IsDuplicate));
    }

    [Fact]
    public void A_placed_nord_and_a_free_text_nord_are_one_place()
    {
        var f = new Fixture(RoleUniqueness.UniquePerSection);
        f.Place("Mustermann, Max", f.Node(CommandNodeKind.Abschnitt, "Nord"));
        f.Place("Musterfrau, Erika", null, section: "Nord");

        using var vm = f.Vm();

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.True(r.IsDuplicate));
    }

    [Fact]
    public void A_placed_nord_and_a_free_text_sued_are_two_places()
    {
        var f = new Fixture(RoleUniqueness.UniquePerSection);
        f.Place("Mustermann, Max", f.Node(CommandNodeKind.Abschnitt, "Nord"));
        f.Place("Musterfrau, Erika", null, section: "Süd");

        using var vm = f.Vm();

        Assert.All(vm.Roles, r => Assert.False(r.IsDuplicate));
    }

    // --- #824: an Abschnitt typed as FÜHRUNG names it is that node, not a Freitext-Abschnitt. ---
    [Fact]
    public void The_abschnitt_box_suggests_the_live_nodes_by_label_and_follows_the_board()
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        var abschnitt = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1");
        f.Node(CommandNodeKind.Unterabschnitt, "Nord", abschnitt);
        f.Node(CommandNodeKind.Bereitstellung, "Marktplatz");
        f.Node(CommandNodeKind.Partner, "Rettungsdienst");
        var gone = f.Node(CommandNodeKind.Abschnitt, "Alt");
        f.Session.Incident.DissolveCommandNode(f.Clock, Fixture.Op, gone);

        using var vm = f.Vm();
        Assert.Equal(["Abschnitt 1", "Abschnitt 1 / Nord", "Marktplatz"], vm.SectionOptions);

        f.Session.AddCommandNode(CommandNodeKind.Abschnitt, "Süd");
        Assert.Equal(["Abschnitt 1", "Abschnitt 1 / Nord", "Marktplatz", "Süd"], vm.SectionOptions);
    }

    [Theory]
    [InlineData("Abschnitt 1 / Nord")]
    [InlineData(" abschnitt 1 / nord ")]
    [InlineData("Nord")]
    public void Typing_a_nodes_label_or_its_only_name_places_the_funktion_there(string typed)
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        var abschnitt = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var nord = f.Node(CommandNodeKind.Unterabschnitt, "Nord", abschnitt);
        using var vm = f.Vm();
        vm.NewRole = "UEAL";
        vm.NewPersonName = "Mustermann, Max";
        vm.NewSection = typed;

        vm.AddRoleCommand.Execute(null);

        var assigned = Assert.Single(f.Session.Incident.Roles);
        Assert.Equal(nord, assigned.NodeId);
        Assert.Equal("Nord", assigned.Section);
        Assert.Empty(ReportingLine.FreeTextSections(f.Session.Incident));
    }

    [Fact]
    public void A_name_two_nodes_share_or_unknown_text_stays_a_freitext_abschnitt()
    {
        var f = new Fixture(RoleUniqueness.Multiple);
        f.Node(CommandNodeKind.Unterabschnitt, "Nord", f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1"));
        f.Node(CommandNodeKind.Unterabschnitt, "Nord", f.Node(CommandNodeKind.Abschnitt, "Abschnitt 2"));
        using var vm = f.Vm();

        foreach (var typed in new[] { "Nord", "Wache Süd" })
        {
            vm.NewRole = "ZF";
            vm.NewPersonName = "Musterfrau, Erika";
            vm.NewSection = typed;
            vm.AddRoleCommand.Execute(null);

            var assigned = f.Session.Incident.Roles[^1];
            Assert.Null(assigned.NodeId);
            Assert.Equal(typed, assigned.Section);
        }
    }

    [Fact]
    public void A_unique_per_abschnitt_funktion_typed_with_a_nodes_label_meets_its_holder_there()
    {
        var f = new Fixture(RoleUniqueness.UniquePerSection);
        var abschnitt1 = f.Node(CommandNodeKind.Abschnitt, "Abschnitt 1");
        var nord1 = f.Node(CommandNodeKind.Unterabschnitt, "Nord", abschnitt1);
        f.Node(CommandNodeKind.Unterabschnitt, "Nord", f.Node(CommandNodeKind.Abschnitt, "Abschnitt 2"));
        f.Place("Mustermann, Max", nord1);
        using var vm = f.Vm();
        vm.NewRole = "UEAL";

        vm.NewSection = "Abschnitt 2 / Nord";
        Assert.Null(vm.ConflictingRow);

        vm.NewSection = "Abschnitt 1 / Nord";
        Assert.Equal("Mustermann, Max", vm.ConflictingRow?.PersonName);
    }
}
