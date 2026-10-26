using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// The board's commands that open the person dialog for a Leiter, Melder, member, Einsatzleiter,
// Führungsassistent or Fachberater.
public class LeadershipBoardPersonTests
{
    private static LeadershipViewModel NewVmOpening(IIncidentSession session, List<LeadershipPersonDialogViewModel> opened) =>
        new(session, Clock, MasterDataSet.Empty, () => { }, null, null, opened.Add);

    [Fact]
    public void A_nodes_person_commands_open_the_dialog_for_that_node_and_slot()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var opened = new List<LeadershipPersonDialogViewModel>();
        using var vm = NewVmOpening(session, opened);
        var card = vm.Sections.Single();

        card.SetLeiterCommand.Execute(null);
        card.SetMelderCommand.Execute(null);
        card.AddMemberCommand.Execute(null);

        Assert.Equal(
            ["EAL festlegen – Nord", "Melder festlegen – Nord", "Funktion hinzufügen – Nord"],
            opened.Select(d => d.Title));
        opened[2].Role = "GF";
        opened[2].PersonName = "Mustermann, Max";
        opened[2].SaveCommand.Execute(null);
        var r = session.Incident.Roles.Single();
        Assert.Equal((nord, RoleSlot.None), (r.NodeId, r.Slot));
    }

    [Fact]
    public void An_Unterabschnitts_card_opens_the_dialog_for_itself()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", nord);
        var opened = new List<LeadershipPersonDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.Sections.Single().Children.Single().SetLeiterCommand.Execute(null);

        Assert.Equal("UAL festlegen – Dach", opened.Single().Title);
    }

    [Fact]
    public void The_einsatzleitungs_and_the_boards_person_commands_open_the_root_dialogs()
    {
        var opened = new List<LeadershipPersonDialogViewModel>();
        using var vm = NewVmOpening(NewSession(), opened);

        vm.El.SetLeiterCommand.Execute(null);
        vm.El.SetMelderCommand.Execute(null);
        vm.El.AddAssistantCommand.Execute(null);
        vm.AddFachberaterCommand.Execute(null);

        Assert.Equal(
            ["EL festlegen", "Melder festlegen – Einsatzleitung", "FüAss hinzufügen", "Fachberater hinzufügen"],
            opened.Select(d => d.Title));
    }

    [Fact]
    public void The_Einsatzleitungs_Melder_dialog_sets_a_root_Melder_and_hands_it_over()
    {
        var session = NewSession();
        var opened = new List<LeadershipPersonDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.El.SetMelderCommand.Execute(null);
        opened[0].Role = "Melder";
        opened[0].PersonName = "Tobias Wagner";
        opened[0].SaveCommand.Execute(null);

        var first = session.Incident.Roles.Single();
        Assert.Equal(((Guid?)null, RoleSlot.Melder), (first.NodeId, first.Slot));
        Assert.Equal("Tobias Wagner", vm.El.Melder?.PersonName);

        vm.El.SetMelderCommand.Execute(null);
        Assert.Equal("Übergabe: Tobias Wagner → neu", opened[1].HandoverText);
        opened[1].PersonName = "Lena Lehner";
        opened[1].SaveCommand.Execute(null);

        Assert.Equal("Lena Lehner", vm.El.Melder?.PersonName);
        Assert.Single(session.Incident.Roles, r => r.To is null);
    }

    [Fact]
    public void Nothing_opens_without_a_host()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        using var vm = NewVm(session);

        vm.El.SetLeiterCommand.Execute(null);
        vm.AddFachberaterCommand.Execute(null);
        vm.Sections.Single().SetLeiterCommand.Execute(null);
    }

    [Fact]
    public void Every_person_command_is_off_on_a_read_only_incident()
    {
        var store = new FakeStore();
        var seeded = NewSession(store);
        AddNode(seeded, CommandNodeKind.Abschnitt, "Nord");
        seeded.Close();
        var session = LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
        using var vm = NewVmOpening(session, []);
        var card = vm.Sections.Single();

        Assert.False(card.SetLeiterCommand.CanExecute(null));
        Assert.False(card.SetMelderCommand.CanExecute(null));
        Assert.False(card.AddMemberCommand.CanExecute(null));
        Assert.False(vm.El.SetLeiterCommand.CanExecute(null));
        Assert.False(vm.El.SetMelderCommand.CanExecute(null));
        Assert.False(vm.El.AddAssistantCommand.CanExecute(null));
        Assert.False(vm.AddFachberaterCommand.CanExecute(null));
    }

    [Fact]
    public void Person_commands_are_on_for_an_open_incident()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Partner, "Polizei");
        using var vm = NewVmOpening(session, []);

        Assert.True(vm.Partners.Single().SetLeiterCommand.CanExecute(null));
        Assert.True(vm.El.SetLeiterCommand.CanExecute(null));
        Assert.True(vm.El.SetMelderCommand.CanExecute(null));
        Assert.True(vm.AddFachberaterCommand.CanExecute(null));
    }
}
