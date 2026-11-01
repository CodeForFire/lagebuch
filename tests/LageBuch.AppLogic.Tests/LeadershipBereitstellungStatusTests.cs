using LageBuch.Domain.Leadership;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// #825: a unit whose status says Bereitstellungsraum but that sits in no node waits in every
// Bereitstellungsraum on the board, marked, until it is placed in one.
public class LeadershipBereitstellungStatusTests
{
    private const string Br = "Bereitstellungsraum";

    [Fact]
    public void A_new_unit_with_status_Bereitstellungsraum_appears_in_the_Bereitstellungsraum()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        using var vm = NewVm(session);

        var row = Assert.Single(vm.Sections[0].Units);
        Assert.True(row.IsWaitingByStatus);
        Assert.Empty(vm.UnplacedUnits);
        Assert.False(vm.HasUnplacedUnits);
    }

    [Fact]
    public void A_unit_switched_to_Bereitstellungsraum_moves_from_unplaced_into_the_Bereitstellungsraum()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        var unitId = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        using var vm = NewVm(session);
        Assert.Single(vm.UnplacedUnits);

        session.UpdateForceUnit(unitId, Br, null);

        Assert.Empty(vm.UnplacedUnits);
        Assert.Equal(unitId, Assert.Single(vm.Sections[0].Units).Id);
    }

    [Fact]
    public void With_two_Bereitstellungsraeume_the_waiting_unit_appears_in_both_marked()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Gewerbegebiet");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        using var vm = NewVm(session);

        Assert.Equal(2, vm.Sections.Count);
        Assert.All(vm.Sections, card => Assert.True(Assert.Single(card.Units).IsWaitingByStatus));
    }

    [Fact]
    public void Without_a_Bereitstellungsraum_the_unit_stays_unplaced()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        using var vm = NewVm(session);

        Assert.Single(vm.UnplacedUnits);
        Assert.False(vm.UnplacedUnits[0].IsWaitingByStatus);
        Assert.Empty(vm.Sections[0].Units);
    }

    [Fact]
    public void An_Abschnitt_does_not_show_a_waiting_unit()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        using var vm = NewVm(session);

        Assert.Empty(vm.Sections.Single(c => c.Kind == CommandNodeKind.Abschnitt).Units);
        Assert.Single(vm.Sections.Single(c => c.Kind == CommandNodeKind.Bereitstellung).Units);
    }

    [Fact]
    public void A_unit_placed_in_an_Abschnitt_is_not_shown_in_the_Bereitstellungsraum()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        var unitId = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", abschnitt);
        session.UpdateForceUnit(unitId, Br, null);
        using var vm = NewVm(session);

        Assert.Empty(vm.Sections.Single(c => c.Kind == CommandNodeKind.Bereitstellung).Units);
        var row = Assert.Single(vm.Sections.Single(c => c.Kind == CommandNodeKind.Abschnitt).Units);
        Assert.False(row.IsWaitingByStatus);
    }

    [Fact]
    public void Placing_the_waiting_unit_in_one_Bereitstellungsraum_removes_it_from_the_others_and_clears_the_marker()
    {
        var session = NewSession();
        var sportplatz = AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Gewerbegebiet");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        var unitId = session.Incident.Forces[^1].Id;
        using var vm = NewVm(session);
        var card = vm.Sections.Single(c => c.Id == sportplatz);
        var row = Assert.Single(card.Units);

        Assert.True(vm.MoveUnit(unitId, sportplatz));

        Assert.Same(row, Assert.Single(card.Units));
        Assert.False(row.IsWaitingByStatus);
        Assert.Empty(vm.Sections.Single(c => c.Id != sportplatz).Units);
    }

    [Fact]
    public void The_Bereitstellungsraum_strength_counts_only_placed_units_but_still_shows_the_table()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        using var vm = NewVm(session);

        Assert.False(vm.Sections[0].HasStrengthTotal);
        Assert.True(vm.Sections[0].HasUnitTable);
    }

    [Fact]
    public void The_units_table_hides_once_the_last_waiting_unit_leaves()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: Br);
        var unitId = session.Incident.Forces[^1].Id;
        using var vm = NewVm(session);
        var card = vm.Sections[0];
        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.UpdateForceUnit(unitId, "Im Einsatz", null);

        Assert.False(card.HasUnitTable);
        Assert.Contains(nameof(card.HasUnitTable), raised);
    }

    [Fact]
    public void The_status_matches_case_and_whitespace_insensitively()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "BR Sportplatz");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", status: " bereitstellungsRAUM ");
        session.AddForceUnit("FF Aich", 9, "Florian Aich 42/1", status: "Im Einsatz");
        using var vm = NewVm(session);

        Assert.Equal("Florian Emmering 40/1", Assert.Single(vm.Sections[0].Units).Title);
        Assert.Equal("Florian Aich 42/1", Assert.Single(vm.UnplacedUnits).Title);
    }
}
