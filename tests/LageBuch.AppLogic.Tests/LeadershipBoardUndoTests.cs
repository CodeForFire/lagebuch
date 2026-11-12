using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// A drop on the board is one stray release of the mouse button, and it moves a unit and its status,
// a whole Abschnitt, or the Einsatzleitung's Führungsfahrzeug. So each is offered back, the way
// ticking off an Aufgabe is (#543).
public class LeadershipBoardUndoTests
{
    private static readonly MasterDataSet Statuses =
        MasterDataSet.Empty with { UnitStatus = ["Alarmiert", "Bereitstellungsraum", "Im Einsatz"] };

    private static LeadershipViewModel NewVmOffering(
        IIncidentSession session, List<(string Message, Action Undo)> offered, MasterDataSet? masterData = null) =>
        new(session, Clock, masterData ?? MasterDataSet.Empty, () => { }, offerUndo: (message, undo) => offered.Add((message, undo)));

    [Fact]
    public void Undoing_a_unit_move_puts_the_unit_back_with_the_status_it_had()
    {
        var session = NewSession();
        var br = AddNode(session, CommandNodeKind.Bereitstellung, "BR Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", br);
        session.UpdateForceUnit(unit, "Bereitstellungsraum", "Schlauchreserve");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered, Statuses);

        Assert.True(vm.MoveUnit(unit, sued));
        Assert.Equal("Im Einsatz", session.Incident.Forces.Single().Status);

        var (message, undo) = Assert.Single(offered);
        Assert.Equal("„Florian Emmering 40/1“ in „Abschnitt Süd“ eingeordnet, Status „Im Einsatz“.", message);
        undo();

        var back = session.Incident.Forces.Single();
        Assert.Equal((br, "Bereitstellungsraum", "Schlauchreserve"), (back.NodeId, back.Status, back.Notes));
    }

    [Fact]
    public void Undoing_taking_a_unit_out_puts_it_back_in_its_abschnitt()
    {
        var session = NewSession();
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt Süd");
        var unit = AddUnit(session, "FF Emmering", null, sued);
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);

        Assert.True(vm.MoveUnit(unit, null));

        var (message, undo) = Assert.Single(offered);
        Assert.Equal("„FF Emmering“ aus „Abschnitt Süd“ genommen.", message);
        undo();
        Assert.Equal(sued, session.Incident.Forces.Single().NodeId);
    }

    [Fact]
    public void Undoing_a_card_move_puts_the_abschnitt_back_in_its_old_place()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var west = AddNode(session, CommandNodeKind.Abschnitt, "West");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);

        Assert.True(vm.MoveNode(sued, nord, null));
        Assert.Equal(CommandNodeKind.Unterabschnitt, session.Incident.FindCommandNode(sued)?.Kind);

        var (message, undo) = Assert.Single(offered);
        Assert.Equal("„Süd“ verschoben.", message);
        undo();

        Assert.Equal(CommandNodeKind.Abschnitt, session.Incident.FindCommandNode(sued)?.Kind);
        Assert.Equal([nord, sued, west], vm.Sections.Select(s => s.Id));
    }

    [Fact]
    public void Undoing_a_new_fuehrungsfahrzeug_brings_the_previous_one_back_and_keeps_later_post_edits()
    {
        var session = NewSession();
        var funk = new Funkgruppen(["FW 1000"], []);
        session.UpdateLeadershipPost("Feuerwehrhaus", "Florian Emmering 1", null, funk, "Florian Emmering 12/1");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 11/1");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);

        Assert.True(vm.AssignFuehrungsfahrzeug(unit));
        var (message, undo) = Assert.Single(offered);
        Assert.Equal("„Florian Emmering 11/1“ ist Führungsfahrzeug der Einsatzleitung.", message);

        // A peer corrects the Standort in the seconds before RÜCKGÄNGIG.
        session.UpdateLeadershipPost("Schule", "Florian Emmering 1", null, funk, "Florian Emmering 11/1");
        undo();

        var post = session.Incident.LeadershipPost;
        Assert.Equal(("Schule", "Florian Emmering 12/1"), (post.Standort, post.Fuehrungsfahrzeug));
    }

    [Fact]
    public void Undoing_a_fuehrungsfahrzeug_that_filled_the_rufname_empties_both_and_puts_the_unit_back_on_the_strip()
    {
        var session = NewSession();
        var unit = AddUnit(session, "FFB", "FFB 1/12/1");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);
        Assert.True(vm.AssignFuehrungsfahrzeug(unit));
        Assert.Empty(vm.UnplacedUnits);

        Assert.Single(offered).Undo();

        var post = session.Incident.LeadershipPost;
        Assert.Equal((null, null), (post.Fuehrungsfahrzeug, post.Rufname));
        Assert.Equal(unit, Assert.Single(vm.UnplacedUnits).Id);
    }

    [Fact]
    public void Undoing_a_fuehrungsfahrzeug_keeps_a_rufname_typed_after_the_drop()
    {
        var session = NewSession();
        var unit = AddUnit(session, "FFB", "FFB 1/12/1");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);
        Assert.True(vm.AssignFuehrungsfahrzeug(unit));

        // A peer names the Einsatzleitung after the EL himself in the seconds before RÜCKGÄNGIG.
        session.UpdateLeadershipPost(null, "Florian Emmering 1", null, Funkgruppen.Empty, "FFB 1/12/1");
        Assert.Single(offered).Undo();

        var post = session.Incident.LeadershipPost;
        Assert.Equal((null, "Florian Emmering 1"), (post.Fuehrungsfahrzeug, post.Rufname));
    }

    [Fact]
    public void A_move_that_keeps_the_status_does_not_name_it_and_its_undo_leaves_the_status_alone()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", nord);
        session.UpdateForceUnit(unit, "Im Einsatz", null);
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered, Statuses);

        Assert.True(vm.MoveUnit(unit, sued));

        var (message, undo) = Assert.Single(offered);
        Assert.Equal("„Florian Emmering 40/1“ in „Abschnitt Süd“ eingeordnet.", message);

        // A peer sets a new status in the seconds before RÜCKGÄNGIG: the move did not touch the
        // status, so taking the move back must not either.
        session.UpdateForceUnit(unit, "Alarmiert", null);
        undo();

        var back = session.Incident.Forces.Single();
        Assert.Equal((nord, "Alarmiert"), (back.NodeId, back.Status));
    }

    [Fact]
    public void Undoing_taking_a_unit_out_takes_back_only_the_place()
    {
        var session = NewSession();
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", sued);
        session.UpdateForceUnit(unit, "Im Einsatz", null);
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered, Statuses);

        // Taking a unit out leaves its status alone, so its undo has no status to put back either,
        // and a status a peer set meanwhile stays.
        Assert.True(vm.MoveUnit(unit, null));
        session.UpdateForceUnit(unit, "Alarmiert", null);
        offered.Single().Undo();

        var back = session.Incident.Forces.Single();
        Assert.Equal((sued, "Alarmiert"), (back.NodeId, back.Status));
    }

    [Fact]
    public void Undoing_a_status_change_keeps_notes_written_since_the_move()
    {
        var session = NewSession();
        var br = AddNode(session, CommandNodeKind.Bereitstellung, "BR Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt Süd");
        var other = AddUnit(session, "FF Grafrath", "Florian Grafrath 40/1", br);
        session.UpdateForceUnit(other, "Bereitstellungsraum", "Atemschutz-Reserve");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", br);
        session.UpdateForceUnit(unit, "Bereitstellungsraum", "Schlauchreserve");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered, Statuses);

        Assert.True(vm.MoveUnit(unit, sued));
        session.UpdateForceUnit(unit, "Im Einsatz", "Schlauchreserve, 2 B-Längen abgegeben");
        offered.Single().Undo();

        var back = session.Incident.Forces.Single(u => u.Id == unit);
        Assert.Equal(("Bereitstellungsraum", "Schlauchreserve, 2 B-Längen abgegeben"), (back.Status, back.Notes));
    }

    [Fact]
    public void A_change_the_session_refuses_after_the_board_allowed_it_offers_nothing_back()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", "Florian Emmering 11/1");
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);

        // The board still reads as editable, but the Einsatz is closed under it.
        session.Close();

        Assert.False(vm.AssignFuehrungsfahrzeug(unit));
        Assert.NotNull(vm.ActionError);
        Assert.False(vm.MoveNode(sued, nord, null));
        Assert.NotNull(vm.ActionError);
        Assert.Empty(offered);
    }

    [Fact]
    public void A_refused_move_offers_nothing_back()
    {
        var session = NewSession();
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", null, sued);
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);

        Assert.False(vm.MoveUnit(unit, sued));
        Assert.False(vm.MoveNode(sued, sued, null));

        Assert.Empty(offered);
    }

    [Fact]
    public void An_undo_the_session_refuses_becomes_the_action_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var unit = AddUnit(session, "FF Emmering", null, nord);
        var offered = new List<(string Message, Action Undo)>();
        using var vm = NewVmOffering(session, offered);
        Assert.True(vm.MoveUnit(unit, sued));

        // A peer dissolves Nord before RÜCKGÄNGIG: the unit has nowhere to go back to.
        session.DissolveCommandNode(nord);
        offered.Single().Undo();

        Assert.NotNull(vm.ActionError);
        Assert.Equal(sued, session.Incident.Forces.Single().NodeId);
    }
}
