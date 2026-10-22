using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// #703: the Funktionen list is a tree. Each Funktion sits under the one it reports to, with guide
// lines in front of it, and a drag onto another row (or the "oberste Ebene" zone) re-links it. The
// drop is only offered where the domain would accept it, so it can never be refused.
public class RolesTreeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, RolesViewModel Vm, Func<string, Guid> Id) NewVm(Action? onChanged = null)
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Huber", from: T0);
        session.AssignRole("Presse", "Lang", from: T0);
        session.AssignRole("Abschnitt Nord", "Schmidt", from: T0);
        session.AssignRole("Abschnitt Süd", "Wagner", from: T0);
        session.AssignRole("Atemschutz", "Maier", from: T0);
        Guid Id(string role) => session.Incident.Roles.Single(r => r.Role == role && r.To is null).Id;
        return (session, new RolesViewModel(session, clock, MasterDataSet.Empty, onChanged ?? (() => { })), Id);
    }

    private static List<string> Tree(RolesViewModel vm) => vm.Roles.Select(r => r.TreeGuide + r.Role).ToList();

    [Fact]
    public void Each_funktion_is_listed_under_the_one_it_reports_to()
    {
        var (session, vm, id) = NewVm();
        session.SetRoleReportsTo(id("Abschnitt Nord"), id("EL"));
        session.SetRoleReportsTo(id("Abschnitt Süd"), id("EL"));
        session.SetRoleReportsTo(id("Atemschutz"), id("Abschnitt Nord"));

        Assert.Equal(
            [
                "EL",
                "├─ Abschnitt Nord",
                "│  └─ Atemschutz",
                "└─ Abschnitt Süd",
                "Presse",
            ],
            Tree(vm));
        Assert.Equal([0, 1, 2, 1, 0], vm.Roles.Select(r => r.Depth));
        Assert.Equal(28, vm.Roles.Single(r => r.Role == "Atemschutz").IndentWidth);
    }

    [Fact]
    public void A_last_child_leaves_no_guide_line_below_it()
    {
        var (session, vm, id) = NewVm();
        session.SetRoleReportsTo(id("Abschnitt Nord"), id("EL"));
        session.SetRoleReportsTo(id("Atemschutz"), id("Abschnitt Nord"));

        Assert.Equal("   └─ Atemschutz", Tree(vm)[2]);
    }

    // A handover ends the old row and starts a new one in its place; the old one, shown on demand,
    // keeps its place under the same superior.
    [Fact]
    public void An_ended_funktion_keeps_its_place_in_the_tree()
    {
        var (session, vm, id) = NewVm();
        session.SetRoleReportsTo(id("Abschnitt Nord"), id("EL"));
        session.SetRoleReportsTo(id("Atemschutz"), id("Abschnitt Nord"));
        session.TransferRole(id("Abschnitt Nord"), "Bauer");

        vm.ShowAllRoles = true;

        Assert.Equal(
            [
                "EL",
                "├─ Abschnitt Nord",
                "└─ Abschnitt Nord",
                "   └─ Atemschutz",
                "Presse",
                "Abschnitt Süd",
            ],
            Tree(vm));
        Assert.Equal("Schmidt", vm.Roles[1].PersonName);
        Assert.Equal("Bauer", vm.Roles[2].PersonName);
    }

    [Fact]
    public void Dropping_a_row_on_another_puts_it_under_that_one()
    {
        var changes = 0;
        var (session, vm, id) = NewVm(() => changes++);

        Assert.True(vm.CanMoveUnder(id("Atemschutz"), id("Abschnitt Süd")));
        Assert.True(vm.MoveUnder(id("Atemschutz"), id("Abschnitt Süd")));

        Assert.Equal(id("Abschnitt Süd"), session.Incident.Roles.Single(r => r.Role == "Atemschutz").ReportsToId);
        Assert.Equal("└─ Atemschutz", Tree(vm)[4]);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Dropping_on_the_top_zone_takes_the_row_out_from_under_its_superior()
    {
        var (session, vm, id) = NewVm();
        session.SetRoleReportsTo(id("Atemschutz"), id("EL"));

        Assert.True(vm.MoveUnder(id("Atemschutz"), null));

        Assert.Null(session.Incident.Roles.Single(r => r.Role == "Atemschutz").ReportsToId);
    }

    [Fact]
    public void A_drop_the_domain_would_refuse_is_not_offered()
    {
        var changes = 0;
        var (session, vm, id) = NewVm(() => changes++);
        session.SetRoleReportsTo(id("Abschnitt Nord"), id("EL"));
        session.SetRoleReportsTo(id("Atemschutz"), id("Abschnitt Nord"));

        Assert.False(vm.CanMoveUnder(id("EL"), id("EL")), "onto itself");
        Assert.False(vm.CanMoveUnder(id("EL"), id("Atemschutz")), "under its own subordinate");
        Assert.False(vm.CanMoveUnder(id("Atemschutz"), id("Abschnitt Nord")), "where it already is");
        Assert.False(vm.CanMoveUnder(id("EL"), null), "already at the top");
        Assert.False(vm.CanMoveUnder(Guid.NewGuid(), id("EL")), "a row the incident does not hold");
        Assert.False(vm.CanMoveUnder(id("Presse"), Guid.NewGuid()), "under a row the incident does not hold");
        Assert.False(vm.MoveUnder(id("EL"), id("Atemschutz")));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void An_ended_funktion_is_neither_dragged_nor_a_target()
    {
        var (session, vm, id) = NewVm();
        session.TransferRole(id("Abschnitt Nord"), "Bauer");
        var ended = session.Incident.Roles.Single(r => r.PersonName == "Schmidt").Id;

        Assert.False(vm.CanMoveUnder(ended, id("EL")));
        Assert.False(vm.CanMoveUnder(id("Presse"), ended));
    }

    [Fact]
    public void A_closed_einsatz_takes_no_drop()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.AssignRole("EL", "Huber", from: T0);
        seed.AssignRole("GF", "Maier", from: T0);
        var readOnly = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new RolesViewModel(readOnly, clock, MasterDataSet.Empty, () => { });

        Assert.False(vm.CanMoveUnder(vm.Roles[1].Id, vm.Roles[0].Id));
        Assert.False(vm.MoveUnder(vm.Roles[1].Id, vm.Roles[0].Id));
    }

    [Fact]
    public void The_top_zone_shows_only_while_a_row_is_dragged()
    {
        var (_, vm, id) = NewVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(vm.IsDraggingRole);

        vm.BeginDrag(id("Presse"));
        Assert.True(vm.IsDraggingRole);
        Assert.Equal(id("Presse"), vm.DraggingId);
        Assert.Contains(nameof(RolesViewModel.IsDraggingRole), raised);

        vm.EndDrag();
        Assert.False(vm.IsDraggingRole);
        Assert.Null(vm.DraggingId);
    }

    [Fact]
    public void Moving_a_row_keeps_the_row_instances()
    {
        var (_, vm, id) = NewVm();
        var atemschutz = vm.Roles.Single(r => r.Role == "Atemschutz");
        var raised = new List<string?>();
        atemschutz.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.MoveUnder(id("Atemschutz"), id("EL"));

        Assert.Same(atemschutz, vm.Roles[1]);
        Assert.Contains(nameof(RoleAssignmentRow.TreeGuide), raised);
        Assert.Contains(nameof(RoleAssignmentRow.TreeSegments), raised);
        Assert.Contains(nameof(RoleAssignmentRow.Depth), raised);
        Assert.Contains(nameof(RoleAssignmentRow.IndentWidth), raised);
    }
}
