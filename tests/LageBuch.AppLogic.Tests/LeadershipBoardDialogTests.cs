using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

// The board's commands that open the Abschnitt dialog, and the names they suggest.
public class LeadershipBoardDialogTests
{
    private static LeadershipViewModel NewVmOpening(IIncidentSession session, List<CommandNodeDialogViewModel> opened) =>
        new(session, Clock, MasterDataSet.Empty, () => { }, null, opened.Add);

    private static LocalIncidentSession ReadOnlySession(Action<LocalIncidentSession> seed)
    {
        var store = new FakeStore();
        var seeded = NewSession(store);
        seed(seeded);
        seeded.Close();
        return LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
    }

    [Fact]
    public void Add_Abschnitt_opens_the_create_dialog_with_the_next_free_number()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1");
        AddNode(session, CommandNodeKind.Abschnitt, "abschnitt 2");
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.AddAbschnittCommand.Execute(null);

        var dialog = Assert.Single(opened);
        Assert.Equal("Abschnitt bilden", dialog.Title);
        Assert.Equal("Abschnitt 3", dialog.Name);
    }

    [Fact]
    public void A_dissolved_Abschnitts_number_is_suggested_again()
    {
        var session = NewSession();
        var first = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1");
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2");
        session.DissolveCommandNode(first);
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.AddAbschnittCommand.Execute(null);

        Assert.Equal("Abschnitt 1", opened[0].Name);
    }

    [Fact]
    public void Add_Bereitstellung_suggests_the_bare_word_then_numbers()
    {
        var session = NewSession();
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.AddBereitstellungCommand.Execute(null);
        Assert.Equal("Bereitstellung einrichten", opened[0].Title);
        Assert.Equal("Bereitstellung", opened[0].Name);

        AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung");
        vm.AddBereitstellungCommand.Execute(null);
        Assert.Equal("Bereitstellung 2", opened[1].Name);
    }

    [Theory]
    [InlineData("Rettungsdienst", "Rettungsdienst")]
    [InlineData("Polizei", "Polizei")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Add_partner_passes_the_preset_as_the_suggested_name(string? preset, string expected)
    {
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(NewSession(), opened);

        vm.AddPartnerCommand.Execute(preset);

        Assert.Equal("Einsatzleitung eintragen", opened[0].Title);
        Assert.Equal(expected, opened[0].Name);
    }

    [Fact]
    public void A_nodes_edit_command_opens_the_edit_dialog_for_that_node()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.Sections.Single().EditCommand.Execute(null);

        Assert.Equal("Abschnitt bearbeiten", opened.Single().Title);
        Assert.Equal("Nord", opened.Single().Name);
    }

    [Fact]
    public void An_Abschnitts_add_Unterabschnitt_command_opens_the_dialog_under_it()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(session, opened);

        vm.Sections.Single().AddUnterabschnittCommand.Execute(null);
        opened.Single().Name = "Dach";
        opened.Single().SaveCommand.Execute(null);

        Assert.Equal("Unterabschnitt bilden", opened.Single().Title);
        Assert.Equal(nord, session.Incident.CommandNodes[^1].ParentId);
    }

    [Fact]
    public void Only_an_Abschnitt_can_form_an_Unterabschnitt()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung");
        AddNode(session, CommandNodeKind.Partner, "Polizei");
        using var vm = NewVmOpening(session, []);

        Assert.False(vm.Sections.Single().AddUnterabschnittCommand.CanExecute(null));
        Assert.False(vm.Partners.Single().AddUnterabschnittCommand.CanExecute(null));
        Assert.True(vm.Sections.Single().EditCommand.CanExecute(null));
        Assert.False(vm.Sections.Single().IsAbschnitt);
        Assert.False(vm.Partners.Single().IsAbschnitt);
    }

    [Fact]
    public void An_Abschnitt_card_says_it_is_one_so_the_board_offers_the_Unterabschnitt()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", nord);
        using var vm = NewVmOpening(session, []);

        var card = vm.Sections.Single();
        Assert.True(card.IsAbschnitt);
        Assert.False(card.Children.Single().IsAbschnitt);
    }

    [Fact]
    public void The_Einsatzleitungs_edit_post_command_opens_the_post_dialog()
    {
        var opened = new List<CommandNodeDialogViewModel>();
        using var vm = NewVmOpening(NewSession(), opened);

        vm.El.EditPostCommand.Execute(null);

        Assert.Equal("Einsatzleitung Feuerwehr", opened.Single().Title);
        Assert.False(opened.Single().ShowName);
    }

    [Fact]
    public void Nothing_opens_without_a_host()
    {
        using var vm = NewVm(NewSession());

        vm.AddAbschnittCommand.Execute(null);
        vm.El.EditPostCommand.Execute(null);
    }

    [Fact]
    public void Every_dialog_command_is_off_on_a_read_only_incident()
    {
        var session = ReadOnlySession(s => AddNode(s, CommandNodeKind.Abschnitt, "Nord"));
        using var vm = NewVmOpening(session, []);

        Assert.False(vm.AddAbschnittCommand.CanExecute(null));
        Assert.False(vm.AddBereitstellungCommand.CanExecute(null));
        Assert.False(vm.AddPartnerCommand.CanExecute("Polizei"));
        Assert.False(vm.El.EditPostCommand.CanExecute(null));
        Assert.False(vm.Sections.Single().EditCommand.CanExecute(null));
        Assert.False(vm.Sections.Single().AddUnterabschnittCommand.CanExecute(null));
    }
}
