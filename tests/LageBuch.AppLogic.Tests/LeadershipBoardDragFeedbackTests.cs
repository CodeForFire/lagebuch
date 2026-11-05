using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// While something is held over the board, a place that refuses it says why, and the thing held is
// marked where it came from. A no-drop cursor alone left the Lagebuchführer guessing.
public class LeadershipBoardDragFeedbackTests
{
    [Fact]
    public void A_unit_held_over_a_partner_says_why_it_cannot_go_there()
    {
        var session = NewSession();
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        using var vm = NewVm(session);

        Assert.False(vm.PreviewUnitDrop(unit, rd));

        Assert.Equal(Incident.PartnerLeadsNoUnits, vm.DropRefusal);
    }

    [Fact]
    public void A_card_held_where_the_rules_refuse_it_says_the_rule()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", sued);
        using var vm = NewVm(session);

        Assert.False(vm.PreviewNodeDrop(sued, nord, null));

        Assert.Equal("Erst die Unterabschnitte verschieben.", vm.DropRefusal);
    }

    [Fact]
    public void A_unit_without_a_funkrufname_over_the_einsatzleitung_says_why()
    {
        var session = NewSession();
        var unit = AddUnit(session, "FF Emmering", null);
        using var vm = NewVm(session);

        Assert.False(vm.PreviewFuehrungsfahrzeugDrop(unit));

        Assert.Equal(LeadershipViewModel.FuehrungsfahrzeugNeedsCallSign, vm.DropRefusal);
    }

    [Fact]
    public void A_drop_that_would_change_nothing_or_is_allowed_says_nothing()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", nord);
        session.UpdateLeadershipPost(null, null, null, null, "Florian Emmering 40/1");
        using var vm = NewVm(session);

        Assert.False(vm.PreviewUnitDrop(unit, nord));
        Assert.Null(vm.DropRefusal);
        Assert.False(vm.PreviewFuehrungsfahrzeugDrop(unit));
        Assert.Null(vm.DropRefusal);
        Assert.False(vm.PreviewNodeDrop(sued, null, null), "Süd is already last");
        Assert.Null(vm.DropRefusal);
        Assert.True(vm.PreviewUnitDrop(unit, sued));
        Assert.Null(vm.DropRefusal);
    }

    [Fact]
    public void Leaving_the_board_or_ending_the_drag_clears_the_reason()
    {
        var session = NewSession();
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        using var vm = NewVm(session);

        vm.PreviewUnitDrop(unit, rd);
        vm.ClearDropPreview();
        Assert.Null(vm.DropRefusal);

        vm.BeginDrag(unit);
        vm.PreviewUnitDrop(unit, rd);
        vm.EndDrag();
        Assert.Null(vm.DropRefusal);
    }

    [Fact]
    public void A_read_only_einsatz_explains_nothing()
    {
        var store = new FakeStore();
        var seed = NewSession(store);
        var rd = AddNode(seed, CommandNodeKind.Partner, "Rettungsdienst");
        var nord = AddNode(seed, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(seed, CommandNodeKind.Abschnitt, "Süd");
        AddNode(seed, CommandNodeKind.Unterabschnitt, "Dach", sued);
        var unit = AddUnit(seed, "FF Emmering", "Florian Emmering 40/1");
        var withoutCallSign = AddUnit(seed, "FF Aich", null);
        seed.Close();
        using var vm = NewVm(LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath));

        Assert.False(vm.PreviewUnitDrop(unit, rd));
        Assert.Null(vm.DropRefusal);
        Assert.False(vm.PreviewFuehrungsfahrzeugDrop(withoutCallSign));
        Assert.Null(vm.DropRefusal);
        Assert.False(vm.PreviewNodeDrop(sued, nord, null));
        Assert.Null(vm.DropRefusal);
    }

    [Fact]
    public void An_unknown_unit_over_the_einsatzleitung_explains_nothing()
    {
        var session = NewSession();
        using var vm = NewVm(session);

        Assert.False(vm.PreviewFuehrungsfahrzeugDrop(Guid.NewGuid()));

        Assert.Null(vm.DropRefusal);
    }

    [Fact]
    public void An_unknown_unit_over_a_partner_explains_nothing()
    {
        // A peer may remove the unit while it is held; there is no partner rule left to explain.
        var session = NewSession();
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        using var vm = NewVm(session);

        Assert.False(vm.PreviewUnitDrop(Guid.NewGuid(), rd));

        Assert.Null(vm.DropRefusal);
    }

    [Fact]
    public void An_unplaced_unit_and_an_unterabschnitt_are_marked_too()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var dach = AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", nord);
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        using var vm = NewVm(session);

        vm.BeginDrag(unit);
        Assert.True(vm.UnplacedUnits.Single().IsDragged);
        vm.EndDrag();

        vm.BeginNodeDrag(dach);
        Assert.True(vm.Sections.Single().Children.Single().IsDragged);
        Assert.False(vm.Sections.Single().IsDragged);
    }

    [Fact]
    public void A_unit_a_peer_moves_mid_drag_stays_marked_on_its_new_card()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", nord);
        using var vm = NewVm(session);
        vm.BeginDrag(unit);

        session.MoveForceUnit(unit, sued);

        Assert.True(vm.Sections.Single(c => c.Id == sued).Units.Single().IsDragged);
    }

    [Fact]
    public void The_dragged_unit_and_card_are_marked_until_the_drag_ends()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", nord);
        AddUnit(session, "FF Aich", "Florian Aich 40/1", nord);
        using var vm = NewVm(session);
        var nordCard = vm.Sections.Single(c => c.Id == nord);

        vm.BeginDrag(unit);
        Assert.Equal([true, false], nordCard.Units.Select(u => u.IsDragged));

        // A peer's change mid-drag rebuilds the board; the mark stays on the unit held.
        session.AddForceUnit("FF Emmering", 3, "Florian Emmering 11/2");
        Assert.True(vm.Sections.Single(c => c.Id == nord).Units.Single(u => u.Id == unit).IsDragged);

        vm.EndDrag();
        Assert.DoesNotContain(nordCard.Units, u => u.IsDragged);

        vm.BeginNodeDrag(sued);
        Assert.Equal([false, true], vm.Sections.Select(c => c.IsDragged));
        vm.EndNodeDrag();
        Assert.DoesNotContain(vm.Sections, c => c.IsDragged);
    }
}
