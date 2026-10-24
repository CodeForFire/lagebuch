using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// Moving an Abschnitt from its BEARBEITEN dialog (LIEGT IN, EINORDNEN VOR) and by dragging its card.
public class CommandNodeMoveViewModelTests
{
    private static CommandNodeDialogViewModel Edit(LocalIncidentSession session, Guid nodeId) =>
        CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, nodeId);

    private static string[] Labels(IReadOnlyList<NodeChoice> choices) => [.. choices.Select(c => c.Label)];

    private static void Pick(IReadOnlyList<NodeChoice> choices, string label) =>
        choices.Single(c => c.Label == label).SelectCommand.Execute(null);

    [Fact]
    public void An_abschnitt_can_go_on_its_own_or_into_any_other_abschnitt()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        AddNode(session, CommandNodeKind.Bereitstellung, "Marktplatz");

        var vm = Edit(session, sued);

        Assert.True(vm.ShowParent);
        Assert.True(vm.ShowPosition);
        Assert.Equal([CommandNodeDialogViewModel.OwnAbschnittLabel, "Nord"], Labels(vm.ParentChoices));
        Assert.Equal(CommandNodeDialogViewModel.OwnAbschnittLabel, vm.ParentLabel);
        Assert.Equal(["Nord", "Marktplatz", CommandNodeDialogViewModel.AtEndLabel], Labels(vm.PositionChoices));
        Assert.Equal("Marktplatz", vm.PositionLabel);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void An_abschnitt_leading_unterabschnitte_stays_on_its_own_and_says_why()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", sued);

        var vm = Edit(session, sued);

        Assert.Equal([CommandNodeDialogViewModel.OwnAbschnittLabel], Labels(vm.ParentChoices));
        Assert.Equal("Erst die Unterabschnitte verschieben.", vm.ParentHint);
    }

    [Fact]
    public void A_bereitstellung_is_only_reordered_and_a_partner_not_moved_at_all()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var markt = AddNode(session, CommandNodeKind.Bereitstellung, "Marktplatz");
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");

        var bereitstellung = Edit(session, markt);
        var partner = Edit(session, rd);

        Assert.False(bereitstellung.ShowParent);
        Assert.True(bereitstellung.ShowPosition);
        Assert.False(partner.ShowParent);
        Assert.False(partner.ShowPosition);
    }

    [Fact]
    public void Choosing_an_abschnitt_offers_its_unterabschnitte_and_saving_demotes_the_node()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var west = AddNode(session, CommandNodeKind.Unterabschnitt, "West", nord);
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var vm = Edit(session, sued);

        Pick(vm.ParentChoices, "Nord");
        Assert.Equal("Nord", vm.ParentLabel);
        Assert.Equal(["West", CommandNodeDialogViewModel.AtEndLabel], Labels(vm.PositionChoices));
        Assert.Equal(CommandNodeDialogViewModel.AtEndLabel, vm.PositionLabel);
        Assert.True(vm.HasChanges);
        Pick(vm.PositionChoices, "West");
        vm.SaveCommand.Execute(null);

        var moved = session.Incident.FindCommandNode(sued)!;
        Assert.Equal((CommandNodeKind.Unterabschnitt, nord), (moved.Kind, moved.ParentId));
        Assert.Equal([sued, west], session.Incident.CommandNodes.Where(n => n.ParentId == nord).Select(n => n.Id));
    }

    [Fact]
    public void Reordering_alone_saves_only_the_move()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var entries = session.Incident.Journal.Count;
        var vm = Edit(session, sued);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        Pick(vm.PositionChoices, "Nord");
        vm.SaveCommand.Execute(null);

        Assert.Equal([sued, nord], session.Incident.CommandNodes.Select(n => n.Id));
        Assert.Equal(entries, session.Incident.Journal.Count);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_name_taken_at_the_new_place_is_a_name_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Süd", nord);
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var vm = Edit(session, sued);

        Pick(vm.ParentChoices, "Nord");

        Assert.Equal("Name ist bereits vergeben.", vm.NameError);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void A_move_refused_meanwhile_shows_the_reason_and_keeps_the_dialog_open()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var vm = Edit(session, sued);
        var closed = 0;
        vm.Closed += (_, _) => closed++;
        Pick(vm.ParentChoices, "Nord");

        // A peer forms an Unterabschnitt under Süd before the Lagebuchführer saves.
        session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Dach", sued);
        vm.SaveCommand.Execute(null);

        Assert.Equal("Nicht möglich: Erst die Unterabschnitte verschieben.", vm.SaveError);
        Assert.Equal(0, closed);
        Assert.Equal(CommandNodeKind.Abschnitt, session.Incident.FindCommandNode(sued)!.Kind);
        Assert.Null(session.Incident.FindCommandNode(sued)!.ParentId);
    }

    [Fact]
    public void The_board_moves_a_dragged_node_where_the_rules_allow_and_nowhere_else()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        using var vm = NewVm(session);

        vm.BeginNodeDrag(sued);
        Assert.Equal(sued, vm.DraggingNodeId);
        Assert.True(vm.IsDraggingNode);
        Assert.False(vm.CanMoveNode(sued, sued, null));
        Assert.False(vm.CanMoveNode(rd, null, nord));
        Assert.False(vm.CanMoveNode(sued, null, null), "Süd is already last: nothing would change");
        Assert.True(vm.CanMoveNode(sued, nord, null));

        Assert.True(vm.MoveNode(sued, nord, null));
        vm.EndNodeDrag();

        Assert.Null(vm.DraggingNodeId);
        var child = Assert.Single(vm.Sections.Single().Children);
        Assert.Equal((sued, "UAL"), (child.Id, child.LeiterLabel));
    }

    [Fact]
    public void A_read_only_board_moves_nothing()
    {
        var store = new FakeStore();
        var seeded = NewSession(store);
        var nord = AddNode(seeded, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(seeded, CommandNodeKind.Abschnitt, "Süd");
        seeded.Close();
        using var vm = NewVm(LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath));

        vm.BeginNodeDrag(sued);

        Assert.Null(vm.DraggingNodeId);
        Assert.False(vm.CanMoveNode(sued, nord, null));
        Assert.False(vm.MoveNode(sued, nord, null));
    }
}
