using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// #703: the MELDET AN picker on a Funktion row. It offers only links the domain accepts, so picking
// one can never be refused: no Funktion reports to itself or to one of its own subordinates.
public class RolesReportsToTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, RolesViewModel Vm) NewVm()
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
        session.AssignRole("Abschnittsleiter", "Schmidt", from: T0, section: "Nord");
        session.AssignRole("GF", "Maier", from: T0, section: "Nord");
        return (session, new RolesViewModel(session, clock, MasterDataSet.Empty, () => { }));
    }

    private static RoleAssignmentRow Row(RolesViewModel vm, string role) => vm.Roles.Single(r => r.Role == role);

    [Fact]
    public void Picking_a_superior_records_it_and_the_button_names_it()
    {
        var (session, vm) = NewVm();
        var ea = Row(vm, "Abschnittsleiter");

        ea.ReportsToChoices.Single(c => c.Display == "EL (Huber)").SelectCommand.Execute(null);

        var el = session.Incident.Roles.Single(r => r.Role == "EL");
        Assert.Equal(el.Id, session.Incident.Roles.Single(r => r.Role == "Abschnittsleiter").ReportsToId);
        Assert.Equal("EL (Huber)", Row(vm, "Abschnittsleiter").ReportsToButtonText);
        Assert.Equal(ReportsToChoice.NoneDisplay, Row(vm, "EL").ReportsToButtonText);
    }

    [Fact]
    public void The_picker_offers_no_one_below_the_row_and_not_the_row_itself()
    {
        var (session, vm) = NewVm();
        Row(vm, "Abschnittsleiter").ReportsToChoices.Single(c => c.Display == "EL (Huber)").SelectCommand.Execute(null);
        Row(vm, "GF").ReportsToChoices.Single(c => c.Display == "Abschnittsleiter (Schmidt)").SelectCommand.Execute(null);

        var offered = Row(vm, "EL").ReportsToChoices.Select(c => c.Display).ToList();

        Assert.Equal([ReportsToChoice.NoneDisplay], offered);
        Assert.Equal(
            [ReportsToChoice.NoneDisplay, "EL (Huber)", "Abschnittsleiter (Schmidt)"],
            Row(vm, "GF").ReportsToChoices.Select(c => c.Display));
        Assert.True(Row(vm, "GF").ReportsToChoices.Single(c => c.Display == "Abschnittsleiter (Schmidt)").IsCurrent);
        Assert.Equal(3, session.Incident.Roles.Count);
    }

    [Fact]
    public void An_ended_funktion_is_not_offered_and_cannot_be_linked()
    {
        var (session, vm) = NewVm();
        var ea = session.Incident.Roles.Single(r => r.Role == "Abschnittsleiter");
        session.TransferRole(ea.Id, "Wagner");

        Assert.DoesNotContain(Row(vm, "GF").ReportsToChoices, c => c.Display == "Abschnittsleiter (Schmidt)");
        Assert.Contains(Row(vm, "GF").ReportsToChoices, c => c.Display == "Abschnittsleiter (Wagner)");
        vm.ShowAllRoles = true; // ended assignments are hidden by default
        Assert.False(vm.Roles.Single(r => r.PersonName == "Schmidt").CanSetReportsTo);
    }

    [Fact]
    public void The_row_tells_the_grid_when_its_superior_changes_and_the_workspace_is_told_too()
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
        session.AssignRole("GF", "Maier", from: T0);
        var changes = 0;
        var vm = new RolesViewModel(session, clock, MasterDataSet.Empty, () => changes++);
        var gf = Row(vm, "GF");
        var raised = new List<string?>();
        gf.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.True(gf.ReportsToChoices.Single(c => c.Id is null).IsCurrent);

        gf.ReportsToChoices.Single(c => c.Display == "EL (Huber)").SelectCommand.Execute(null);

        Assert.Same(gf, Row(vm, "GF"));
        Assert.Contains(nameof(RoleAssignmentRow.ReportsToButtonText), raised);
        Assert.Contains(nameof(RoleAssignmentRow.ReportsToChoices), raised);
        Assert.Contains(nameof(RoleAssignmentRow.CanSetReportsTo), raised);
        Assert.False(gf.ReportsToChoices.Single(c => c.Id is null).IsCurrent);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_closed_einsatz_offers_no_picker()
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
        var readOnly = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        var vm = new RolesViewModel(readOnly, clock, MasterDataSet.Empty, () => { });

        Assert.False(Assert.Single(vm.Roles).CanSetReportsTo);
    }

    [Fact]
    public void Choosing_none_clears_the_link()
    {
        var (session, vm) = NewVm();
        Row(vm, "GF").ReportsToChoices.Single(c => c.Display == "EL (Huber)").SelectCommand.Execute(null);

        Row(vm, "GF").ReportsToChoices.Single(c => c.Id is null).SelectCommand.Execute(null);

        Assert.Null(session.Incident.Roles.Single(r => r.Role == "GF").ReportsToId);
        Assert.Equal(ReportsToChoice.NoneDisplay, Row(vm, "GF").ReportsToButtonText);
    }

    // An older or foreign file can point at an assignment that is not in it. The row says so rather
    // than showing nothing, and the picker still lets the Lagebuchführer fix it.
    [Fact]
    public void A_dangling_link_reads_as_unknown()
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
        seed.AssignRole("GF", "Maier", from: T0);
        var saved = store.Load("/x.fwincident");
        var gf = saved.Roles.Single();
        var dangling = Incident.Rehydrate(
            saved.Id,
            saved.StartedAt,
            saved.State,
            saved.IncidentNumber,
            saved.Keyword,
            saved.Street,
            saved.District,
            saved.Status,
            saved.ClosedAt,
            saved.ClosedBy,
            saved.Checklists,
            saved.Journal,
            [gf with { ReportsToId = Guid.NewGuid() }],
            saved.Forces,
            saved.ScbaTrupps,
            saved.Audit,
            saved.Timers,
            saved.Files,
            saved.Tasks,
            saved.Buildings,
            saved.Dwellings,
            saved.InvolvedParties);
        store.Save("/x.fwincident", dangling);
        var session = LocalIncidentSession.Open(store, clock, "/x.fwincident", new SessionOperator("Müller"));

        var vm = new RolesViewModel(session, clock, MasterDataSet.Empty, () => { });

        Assert.Equal(ReportsToChoice.UnknownDisplay, Assert.Single(vm.Roles).ReportsToButtonText);
    }
}
