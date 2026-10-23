using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class CommandNodeDialogViewModelTests
{
    private static MasterDataSet WithTalkGroups() =>
        MasterDataSet.Empty with
        {
            TalkGroups =
            [
                new TalkGroup("FW_MUSTER", "1000", TalkGroupKind.Tmo),
                new TalkGroup("301_F", null, TalkGroupKind.Dmo),
            ],
        };

    private static LocalIncidentSession ReadOnlySession()
    {
        var store = new FakeStore();
        NewSession(store).Close();
        return LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
    }

    private static CommandNodeDialogViewModel Create(
        IIncidentSession session, CommandNodeKind kind = CommandNodeKind.Abschnitt, Guid? parent = null, string name = "Abschnitt 1") =>
        CommandNodeDialogViewModel.ForCreate(session, MasterDataSet.Empty, kind, parent, name);

    [Theory]
    [InlineData(CommandNodeKind.Abschnitt, "Abschnitt bilden", "BILDEN")]
    [InlineData(CommandNodeKind.Bereitstellung, "Bereitstellung einrichten", "EINRICHTEN")]
    [InlineData(CommandNodeKind.Partner, "Einsatzleitung eintragen", "EINTRAGEN")]
    public void Create_mode_names_the_dialog_and_its_button_for_the_kind(CommandNodeKind kind, string title, string primary)
    {
        var vm = Create(NewSession(), kind);

        Assert.Equal(title, vm.Title);
        Assert.Equal(primary, vm.PrimaryText);
        Assert.True(vm.ShowName);
        Assert.True(vm.ShowAuftrag);
    }

    [Fact]
    public void Create_mode_for_an_Unterabschnitt_says_bilden()
    {
        var session = NewSession();
        var parent = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        var vm = Create(session, CommandNodeKind.Unterabschnitt, parent, string.Empty);

        Assert.Equal("Unterabschnitt bilden", vm.Title);
        Assert.Equal("BILDEN", vm.PrimaryText);
    }

    [Theory]
    [InlineData(CommandNodeKind.Abschnitt, "Abschnitt bearbeiten")]
    [InlineData(CommandNodeKind.Bereitstellung, "Bereitstellung bearbeiten")]
    [InlineData(CommandNodeKind.Partner, "Einsatzleitung bearbeiten")]
    public void Edit_mode_names_the_dialog_for_the_kind_and_seeds_the_fields(CommandNodeKind kind, string title)
    {
        var session = NewSession();
        session.AddCommandNode(kind, "Nord", null, "Löschangriff", "Hofeinfahrt", "TMO FW_MUSTER 1000", "Florian Nord", "0151 1234567");
        var id = session.Incident.CommandNodes[^1].Id;

        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);

        Assert.Equal(title, vm.Title);
        Assert.Equal("SPEICHERN", vm.PrimaryText);
        Assert.Equal("Nord", vm.Name);
        Assert.Equal("Löschangriff", vm.Auftrag);
        Assert.Equal("Hofeinfahrt", vm.Standort);
        Assert.Equal("TMO FW_MUSTER 1000", vm.Funkgruppe);
        Assert.Equal("Florian Nord", vm.Rufname);
        Assert.Equal("0151 1234567", vm.Telefon);
    }

    [Fact]
    public void Post_mode_has_no_name_or_auftrag_and_seeds_the_post()
    {
        var session = NewSession();
        session.UpdateLeadershipPost("Feuerwehrhaus", "Florian Emmering", "08141 123", "TMO FW_MUSTER 1000");

        var vm = CommandNodeDialogViewModel.ForPost(session, MasterDataSet.Empty);

        Assert.Equal("Einsatzleitung Feuerwehr", vm.Title);
        Assert.Equal("SPEICHERN", vm.PrimaryText);
        Assert.False(vm.ShowName);
        Assert.False(vm.ShowAuftrag);
        Assert.Equal("Feuerwehrhaus", vm.Standort);
        Assert.Equal("Florian Emmering", vm.Rufname);
        Assert.Equal("08141 123", vm.Telefon);
        Assert.Equal("TMO FW_MUSTER 1000", vm.Funkgruppe);
    }

    [Fact]
    public void Funkgruppe_options_are_the_talk_groups_display_strings()
    {
        var vm = CommandNodeDialogViewModel.ForCreate(NewSession(), WithTalkGroups(), CommandNodeKind.Abschnitt, null, "Abschnitt 1");

        Assert.Equal(["TMO FW_MUSTER 1000", "DMO 301_F"], vm.FunkgruppeOptions);
    }

    [Fact]
    public void Create_saves_one_node_with_every_detail_and_closes()
    {
        var session = NewSession();
        var changes = new[] { 0 };
        var vm = CommandNodeDialogViewModel.ForCreate(
            session, MasterDataSet.Empty, CommandNodeKind.Abschnitt, null, "Abschnitt 1", () => changes[0]++);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.Name = " Nord ";
        vm.Auftrag = "Brandbekämpfung";
        vm.Standort = "Hauptstraße";
        vm.Funkgruppe = "TMO X";
        vm.Rufname = "Florian Nord";
        vm.Telefon = "0151 1";
        vm.SaveCommand.Execute(null);

        var node = Assert.Single(session.Incident.CommandNodes);
        Assert.Equal(("Nord", CommandNodeKind.Abschnitt), (node.Name, node.Kind));
        Assert.Equal("Brandbekämpfung", node.Auftrag);
        Assert.Equal("Hauptstraße", node.Standort);
        Assert.Equal("TMO X", node.Funkgruppe);
        Assert.Equal("Florian Nord", node.Rufname);
        Assert.Equal("0151 1", node.Telefon);
        Assert.Equal(1, closed);
        Assert.Equal(1, changes[0]);
    }

    [Fact]
    public void Create_under_a_parent_forms_an_Unterabschnitt()
    {
        var session = NewSession();
        var parent = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = Create(session, CommandNodeKind.Unterabschnitt, parent, "Dach");

        vm.SaveCommand.Execute(null);

        var node = session.Incident.CommandNodes[^1];
        Assert.Equal((CommandNodeKind.Unterabschnitt, "Dach", (Guid?)parent), (node.Kind, node.Name, node.ParentId));
    }

    [Fact]
    public void Edit_saves_the_changed_details()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);

        vm.Name = "Nord 2";
        vm.Auftrag = "Riegelstellung";
        vm.SaveCommand.Execute(null);

        var node = session.Incident.CommandNodes.Single();
        Assert.Equal("Nord 2", node.Name);
        Assert.Equal("Riegelstellung", node.Auftrag);
    }

    [Fact]
    public void Edit_without_changes_writes_nothing_and_still_closes()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var changes = new[] { 0 };
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id, () => changes[0]++);
        var closed = 0;
        vm.Closed += (_, _) => closed++;
        var before = session.Incident.CommandNodes.Single();

        vm.SaveCommand.Execute(null);

        Assert.Equal(0, changes[0]);
        Assert.Same(before, session.Incident.CommandNodes.Single());
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Post_mode_saves_the_post_only_when_it_changed()
    {
        var session = NewSession();
        var changes = new[] { 0 };
        var vm = CommandNodeDialogViewModel.ForPost(session, MasterDataSet.Empty, () => changes[0]++);
        vm.SaveCommand.Execute(null);
        Assert.Equal(0, changes[0]);

        var again = CommandNodeDialogViewModel.ForPost(session, MasterDataSet.Empty, () => changes[0]++);
        again.Standort = "Feuerwehrhaus";
        again.Rufname = "Florian Emmering";
        again.Telefon = "08141 123";
        again.Funkgruppe = "TMO X";
        again.SaveCommand.Execute(null);

        var post = session.Incident.LeadershipPost;
        Assert.Equal(("Feuerwehrhaus", "Florian Emmering", "08141 123", "TMO X"), (post.Standort, post.Rufname, post.Telefon, post.Funkgruppe));
        Assert.Equal(1, changes[0]);
    }

    [Fact]
    public void A_blank_name_is_refused_with_a_field_error()
    {
        var session = NewSession();
        var vm = Create(session, name: string.Empty);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        Assert.Equal("Name fehlt.", vm.NameError);
        Assert.Empty(session.Incident.CommandNodes);
        Assert.Equal(0, closed);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Typing_a_name_clears_the_error()
    {
        var vm = Create(NewSession(), name: string.Empty);
        vm.SaveCommand.Execute(null);

        vm.Name = "Nord";

        Assert.Null(vm.NameError);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void A_taken_name_is_refused_ignoring_case_and_whitespace()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = Create(session, name: "x");

        vm.Name = " nord ";

        Assert.Equal("Name ist bereits vergeben.", vm.NameError);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Single(session.Incident.CommandNodes);
    }

    [Fact]
    public void A_dissolved_nodes_name_can_be_used_again()
    {
        var session = NewSession();
        var old = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.DissolveCommandNode(old);
        var vm = Create(session, name: "Nord");

        vm.Name = "Nord ";

        Assert.Null(vm.NameError);
    }

    [Fact]
    public void A_sibling_under_another_parent_does_not_clash()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", nord);
        var vm = Create(session, CommandNodeKind.Unterabschnitt, sued, "x");

        vm.Name = "Dach";

        Assert.Null(vm.NameError);

        var sameParent = Create(session, CommandNodeKind.Unterabschnitt, nord, "x");
        sameParent.Name = "dach";
        Assert.Equal("Name ist bereits vergeben.", sameParent.NameError);
    }

    [Fact]
    public void Renaming_a_node_to_its_own_name_in_another_case_is_allowed()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);

        vm.Name = "NORD";
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.NameError);
        Assert.Equal("NORD", session.Incident.CommandNodes.Single().Name);
    }

    [Fact]
    public void Editing_a_node_a_peer_dissolved_shows_an_error_and_saves_nothing()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);
        vm.Auftrag = "Neu";
        session.DissolveCommandNode(id);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        Assert.Equal("Dieser Abschnitt wurde inzwischen aufgelöst.", vm.SaveError);
        Assert.Null(session.Incident.CommandNodes.Single().Auftrag);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void A_domain_refusal_that_still_comes_back_surfaces_as_a_save_error()
    {
        var session = NewSession();
        var vm = Create(session, name: "Nord");
        vm.Telefon = new string('1', 51);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        Assert.StartsWith("Nicht möglich: ", vm.SaveError, StringComparison.Ordinal);
        Assert.Empty(session.Incident.CommandNodes);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void Funkgruppe_is_saved_in_the_catalogues_spelling_and_off_catalogue_text_as_typed()
    {
        var session = NewSession();
        var vm = CommandNodeDialogViewModel.ForCreate(session, WithTalkGroups(), CommandNodeKind.Abschnitt, null, "Nord");
        vm.Funkgruppe = "tmo fw_muster 1000";
        vm.SaveCommand.Execute(null);

        var other = CommandNodeDialogViewModel.ForCreate(session, WithTalkGroups(), CommandNodeKind.Abschnitt, null, "Süd");
        other.Funkgruppe = " Kanal 7 ";
        other.SaveCommand.Execute(null);

        Assert.Equal("TMO FW_MUSTER 1000", session.Incident.CommandNodes[0].Funkgruppe);
        Assert.Equal("Kanal 7", session.Incident.CommandNodes[1].Funkgruppe);
    }

    [Fact]
    public void Cancel_closes_at_once_when_nothing_changed()
    {
        var vm = Create(NewSession());
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.False(vm.IsDiscardConfirmOpen);
    }

    [Fact]
    public void Cancel_after_typing_asks_first_and_the_question_blocks_saving()
    {
        var session = NewSession();
        var vm = Create(session);
        var closed = 0;
        vm.Closed += (_, _) => closed++;
        vm.Auftrag = "Löschangriff";

        vm.CancelCommand.Execute(null);

        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.Equal(0, closed);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.CancelDiscardCommand.Execute(null);
        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);
        vm.ConfirmDiscardCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.Empty(session.Incident.CommandNodes);
    }

    [Fact]
    public void Edit_cancel_asks_only_when_a_field_changed()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);

        Assert.False(vm.HasChanges);
        vm.Rufname = "Florian";
        Assert.True(vm.HasChanges);
    }

    [Fact]
    public void An_untouched_edit_dialog_does_not_ask_after_a_peer_edited_the_node()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        session.UpdateCommandNode(id, "Nord", "Riegelstellung", null, null, "Florian Emmering 11/1", null);

        Assert.False(vm.HasChanges);
        vm.CancelCommand.Execute(null);
        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Saving_an_untouched_edit_dialog_keeps_a_peers_edit()
    {
        var session = NewSession();
        var id = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = CommandNodeDialogViewModel.ForEdit(session, MasterDataSet.Empty, id);

        session.UpdateCommandNode(id, "Nord", "Riegelstellung", null, null, null, null);
        vm.SaveCommand.Execute(null);

        Assert.Equal("Riegelstellung", session.Incident.CommandNodes.Single().Auftrag);
    }

    [Fact]
    public void An_untouched_edit_dialog_does_not_ask_over_a_funkgruppe_spelt_off_catalogue()
    {
        var session = NewSession();
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord", null, null, null, "tmo fw_muster 1000", null, null);
        var id = session.Incident.CommandNodes[^1].Id;
        var vm = CommandNodeDialogViewModel.ForEdit(session, WithTalkGroups(), id);

        Assert.False(vm.HasChanges);

        // Once another field changes, the save writes the Funkgruppe in the catalogue's spelling.
        vm.Rufname = "Florian Emmering 11/1";
        vm.SaveCommand.Execute(null);
        Assert.Equal("TMO FW_MUSTER 1000", session.Incident.CommandNodes.Single().Funkgruppe);
    }

    [Fact]
    public void An_untouched_post_dialog_does_not_ask_after_a_peer_edited_the_post()
    {
        var session = NewSession();
        var vm = CommandNodeDialogViewModel.ForPost(session, MasterDataSet.Empty);

        session.UpdateLeadershipPost("Feuerwehrhaus Emmering", null, null, null);

        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void A_read_only_incident_cannot_save()
    {
        var vm = Create(ReadOnlySession());

        Assert.False(vm.SaveCommand.CanExecute(null));
    }
}
