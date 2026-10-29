using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class RoleAssignmentDialogViewModelTests
{
    private static readonly MasterDataSet MasterData = MasterDataSet.Empty with
    {
        Roles =
        [
            new Role("S2 Lage", RoleUniqueness.Multiple, RoleSlot.Fuehrungsassistent),
            new Role("Sicherheitsbeauftragter", RoleUniqueness.UniquePerSection, RoleSlot.None),
        ],
        Personnel =
        [
            new Person("Mustermann", "Max", null, "Florian Emmering 11/1", "0151 1111111"),
        ],
    };

    private static RoleAssignmentDialogViewModel Open(
        IIncidentSession session, Guid id, RoleAssignmentDialogMode mode, Action? onChanged = null) =>
        new(session, MasterData, id, mode, onChanged);

    private static Guid Assign(
        LocalIncidentSession session, string role, string person, string? section = null, RoleSlot slot = RoleSlot.None)
    {
        session.AssignRole(role, person, "Florian 1/11", section: section, phone: "0171", slot: slot);
        return session.Incident.Roles[^1].Id;
    }

    private static RoleAssignment Running(LocalIncidentSession session, string role) =>
        session.Incident.Roles.Single(r => r.To is null && r.Role == role);

    [Fact]
    public void Bearbeiten_starts_from_what_is_recorded_and_corrects_it_in_place()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfru", slot: RoleSlot.Fuehrungsassistent);
        var changes = 0;
        var closed = false;
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit, () => changes++);
        vm.Closed += (_, _) => closed = true;

        Assert.Equal("S2 Lage bearbeiten", vm.Title);
        Assert.Equal("SPEICHERN", vm.PrimaryText);
        Assert.Equal("Erika Musterfru", vm.PersonName);
        Assert.Equal("Florian 1/11", vm.CallSign);
        Assert.Equal("0171", vm.Phone);
        Assert.False(vm.HasChanges);

        vm.PersonName = "Erika Musterfrau";
        vm.SaveCommand.Execute(null);

        var row = Assert.Single(session.Incident.Roles);
        Assert.Equal(id, row.Id);
        Assert.Equal("Erika Musterfrau", row.PersonName);
        Assert.Equal(RoleSlot.Fuehrungsassistent, row.Slot);
        Assert.Equal(1, changes);
        Assert.True(closed);
    }

    [Fact]
    public void Uebertragen_hands_the_funktion_over_to_the_person_named()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);
        var vm = Open(session, id, RoleAssignmentDialogMode.Transfer);

        Assert.Equal("Übergabe S2 Lage: Erika Musterfrau", vm.Title);
        Assert.Equal("ÜBERTRAGEN", vm.PrimaryText);
        Assert.Equal(string.Empty, vm.PersonName);
        Assert.False(vm.IsSectionEditable);

        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        var next = Running(session, "S2 Lage");
        Assert.NotEqual(id, next.Id);
        Assert.Equal("Mustermann, Max", next.PersonName);
        Assert.Equal("Florian Emmering 11/1", next.CallSign); // from the roster
        Assert.Equal(RoleSlot.Fuehrungsassistent, next.Slot);
        Assert.NotNull(session.Incident.Roles.Single(r => r.Id == id).To);
    }

    [Theory]
    [InlineData(RoleAssignmentDialogMode.Edit)]
    [InlineData(RoleAssignmentDialogMode.Transfer)]
    public void A_blank_name_is_refused_and_nothing_is_written(RoleAssignmentDialogMode mode)
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var before = session.Incident.Journal.Count;
        var vm = Open(session, id, mode);

        vm.PersonName = " ";
        vm.SaveCommand.Execute(null);

        Assert.Equal("Name fehlt.", vm.PersonError);
        Assert.Equal(before, session.Incident.Journal.Count);
        Assert.Equal("Erika Musterfrau", Running(session, "S2 Lage").PersonName);
    }

    [Fact]
    public void Bearbeiten_offers_the_abschnitt_only_for_a_funktion_the_board_did_not_place()
    {
        var session = NewSession();
        var typed = Assign(session, "S2 Lage", "Erika Musterfrau", section: "Nord");
        var placed = Assign(session, "S2 Lage", "Mustermann, Max", slot: RoleSlot.Fuehrungsassistent);

        Assert.True(Open(session, typed, RoleAssignmentDialogMode.Edit).IsSectionEditable);
        Assert.False(Open(session, placed, RoleAssignmentDialogMode.Edit).IsSectionEditable);
    }

    [Fact]
    public void Bearbeiten_moves_a_typed_funktion_to_another_abschnitt()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau", section: "Nord");
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit);

        vm.Section = " Süd ";
        vm.SaveCommand.Execute(null);

        Assert.Equal("Süd", Running(session, "S2 Lage").Section);
    }

    [Fact]
    public void Moving_a_funktion_unique_per_abschnitt_onto_a_held_abschnitt_is_refused()
    {
        var session = NewSession();
        Assign(session, "Sicherheitsbeauftragter", "Mustermann, Max", section: "Süd");
        var id = Assign(session, "Sicherheitsbeauftragter", "Erika Musterfrau", section: "Nord");
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit);

        vm.Section = "Süd";

        Assert.Equal("„Sicherheitsbeauftragter“ ist in Süd bereits mit Mustermann, Max besetzt.", vm.SectionError);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Section = "Nord";

        Assert.Null(vm.SectionError);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Abbrechen_after_typing_asks_first_and_closes_at_once_otherwise()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var closed = 0;
        var untouched = Open(session, id, RoleAssignmentDialogMode.Edit);
        untouched.Closed += (_, _) => closed++;

        untouched.CancelCommand.Execute(null);

        Assert.Equal(1, closed);

        var typed = Open(session, id, RoleAssignmentDialogMode.Edit);
        typed.Closed += (_, _) => closed++;
        typed.Phone = "0172";
        typed.CancelCommand.Execute(null);

        Assert.True(typed.IsDiscardConfirmOpen);
        Assert.False(typed.SaveCommand.CanExecute(null));
        Assert.Equal(1, closed);

        typed.ConfirmDiscardCommand.Execute(null);

        Assert.Equal(2, closed);
        Assert.Equal("0171", Running(session, "S2 Lage").Phone);
    }

    [Fact]
    public void A_handover_elsewhere_while_open_is_said_and_blocks_the_save()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit);
        vm.PersonName = "Erika Musterfrau-Beispiel";

        session.TransferRole(id, "Mustermann, Max");

        Assert.Equal("Diese Funktion wurde inzwischen übergeben.", vm.SaveError);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Detach();
    }

    [Fact]
    public void A_read_only_einsatz_saves_nothing()
    {
        var store = new FakeStore();
        var seeded = NewSession(store);
        Assign(seeded, "S2 Lage", "Erika Musterfrau");
        seeded.Close();
        var session = LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
        var id = session.Incident.Roles[0].Id;

        Assert.False(Open(session, id, RoleAssignmentDialogMode.Edit).SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Opening_on_an_unknown_assignment_is_an_argument_error()
    {
        Assert.Throws<ArgumentException>(() => Open(NewSession(), Guid.NewGuid(), RoleAssignmentDialogMode.Edit));
    }

    [Fact]
    public void Opening_on_a_running_funktion_says_nothing_and_opening_on_an_ended_one_blocks_the_save()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");

        var running = Open(session, id, RoleAssignmentDialogMode.Edit);
        Assert.Null(running.SaveError);
        Assert.True(running.SaveCommand.CanExecute(null));
        running.Detach();

        session.TransferRole(id, "Mustermann, Max");
        var ended = Open(session, id, RoleAssignmentDialogMode.Edit);

        Assert.Equal("Diese Funktion wurde inzwischen übergeben.", ended.SaveError);
        Assert.False(ended.SaveCommand.CanExecute(null));
        ended.Detach();
    }

    [Fact]
    public void Uebertragen_never_offers_the_abschnitt_even_of_a_typed_funktion()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau", section: "Nord");

        Assert.False(Open(session, id, RoleAssignmentDialogMode.Transfer).IsSectionEditable);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("callSign")]
    [InlineData("phone")]
    [InlineData("section")]
    public void Bearbeiten_counts_a_change_to_any_one_field(string field)
    {
        var session = NewSession();
        var vm = Open(session, Assign(session, "S2 Lage", "Erika Musterfrau", section: "Nord"), RoleAssignmentDialogMode.Edit);

        switch (field)
        {
            case "name": vm.PersonName = "Erika Musterfrau-Huber"; break;
            case "callSign": vm.CallSign = "Florian 1/12"; break;
            case "phone": vm.Phone = "0172"; break;
            default: vm.Section = "Süd"; break;
        }

        Assert.True(vm.HasChanges);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("Mustermann, Max", null, null, true)]
    [InlineData(null, "Florian 1/12", null, true)]
    [InlineData(null, null, "0172", true)]
    public void Uebertragen_counts_whatever_was_typed(string? name, string? callSign, string? phone, bool expected)
    {
        var session = NewSession();
        var vm = Open(session, Assign(session, "S2 Lage", "Erika Musterfrau"), RoleAssignmentDialogMode.Transfer);
        vm.CallSign = callSign;
        vm.Phone = phone;
        if (name is not null)
        {
            vm.PersonName = name;
        }

        Assert.Equal(expected, vm.HasChanges);
    }

    [Fact]
    public void Typing_a_name_after_a_refused_save_clears_the_name_error()
    {
        var session = NewSession();
        var vm = Open(session, Assign(session, "S2 Lage", "Erika Musterfrau"), RoleAssignmentDialogMode.Transfer);
        vm.SaveCommand.Execute(null);
        Assert.Equal("Name fehlt.", vm.PersonError);

        vm.PersonName = " ";
        Assert.Equal("Name fehlt.", vm.PersonError);

        vm.PersonName = "Mustermann, Max";
        Assert.Null(vm.PersonError);
    }

    [Fact]
    public void A_holder_another_device_puts_into_the_abschnitt_typed_is_reported_at_once()
    {
        var session = NewSession();
        var id = Assign(session, "Sicherheitsbeauftragter", "Erika Musterfrau", section: "Nord");
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit);
        vm.Section = "Süd";
        Assert.Null(vm.SectionError);

        Assign(session, "Sicherheitsbeauftragter", "Mustermann, Max", section: "Süd");

        Assert.NotNull(vm.SectionError);
        vm.Detach();
    }

    [Fact]
    public void Moving_a_funktion_several_may_hold_onto_a_held_abschnitt_is_no_conflict()
    {
        var session = NewSession();
        Assign(session, "S2 Lage", "Mustermann, Max", section: "Süd");
        var vm = Open(session, Assign(session, "S2 Lage", "Erika Musterfrau", section: "Nord"), RoleAssignmentDialogMode.Edit);

        vm.Section = "Süd";

        Assert.Null(vm.SectionError);
    }

    [Fact]
    public void Retyping_its_own_abschnitt_is_no_conflict_even_beside_an_older_duplicate()
    {
        var session = NewSession();

        // A duplicate from an older file or another device: the other holder is found first.
        Assign(session, "Sicherheitsbeauftragter", "Mustermann, Max", section: "Nord");
        var vm = Open(session, Assign(session, "Sicherheitsbeauftragter", "Erika Musterfrau", section: "Nord"), RoleAssignmentDialogMode.Edit);

        vm.Section = "Nord ";

        Assert.Null(vm.SectionError);
    }

    [Fact]
    public void A_different_spelling_of_its_own_abschnitt_does_not_report_the_holder_itself()
    {
        var session = NewSession();
        var vm = Open(session, Assign(session, "Sicherheitsbeauftragter", "Erika Musterfrau", section: "Nord"), RoleAssignmentDialogMode.Edit);

        vm.Section = "nord";

        Assert.Null(vm.SectionError);
    }

    [Fact]
    public void Bearbeiten_keeps_the_place_of_a_funktion_the_board_placed()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit);

        // Not offered on screen, so whatever stands in the property is not sent.
        vm.Section = "Süd";
        vm.PersonName = "Erika Musterfrau-Huber";
        vm.SaveCommand.Execute(null);

        var row = Running(session, "S2 Lage");
        Assert.Null(vm.SaveError);
        Assert.Null(row.Section);
        Assert.Equal("Erika Musterfrau-Huber", row.PersonName);
    }

    [Fact]
    public void A_refusal_from_the_domain_stands_as_the_save_error_and_keeps_the_dialog_open()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var changes = 0;
        var closed = false;
        var vm = Open(session, id, RoleAssignmentDialogMode.Transfer, () => changes++);
        vm.Closed += (_, _) => closed = true;
        session.TransferRole(id, "Mustermann, Max");

        // The button is off by now; a press that still arrives is refused by the domain.
        vm.PersonName = "Musterfrau, Erika";
        vm.SaveCommand.Execute(null);

        Assert.Equal("Nicht möglich: Funktionszuweisung ist bereits beendet.", vm.SaveError);
        Assert.False(closed);
        Assert.Equal(0, changes);
        vm.Detach();
    }

    [Fact]
    public void Zurueck_returns_to_the_form_and_abbrechen_in_the_question_closes_it()
    {
        var session = NewSession();
        var vm = Open(session, Assign(session, "S2 Lage", "Erika Musterfrau"), RoleAssignmentDialogMode.Edit);
        var closed = 0;
        vm.Closed += (_, _) => closed++;
        vm.Phone = "0172";

        vm.CancelCommand.Execute(null);
        vm.CancelDiscardCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.Equal(0, closed);

        vm.CancelCommand.Execute(null);
        vm.ConfirmDiscardCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_closed_or_detached_dialog_stops_following_the_session()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var closedOne = Open(session, id, RoleAssignmentDialogMode.Edit);
        closedOne.CancelCommand.Execute(null);
        var detached = Open(session, id, RoleAssignmentDialogMode.Edit);
        detached.Detach();
        var raised = 0;
        detached.SaveCommand.CanExecuteChanged += (_, _) => raised++;

        session.TransferRole(id, "Mustermann, Max");

        Assert.Null(closedOne.SaveError);
        Assert.Null(detached.SaveError);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_handover_elsewhere_turns_the_save_button_off_as_it_happens()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var vm = Open(session, id, RoleAssignmentDialogMode.Edit);
        var raised = 0;
        vm.SaveCommand.CanExecuteChanged += (_, _) => raised++;

        session.TransferRole(id, "Mustermann, Max");

        Assert.True(raised > 0);
        vm.Detach();
    }

    [Fact]
    public void The_boards_fuehrungsunterstuetzung_rows_open_bearbeiten_and_uebertragen()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);
        session.AssignRole("EL", "Mustermann, Max", slot: RoleSlot.Leiter);
        var opened = new List<RoleAssignmentDialogViewModel>();
        using var vm = new LeadershipViewModel(session, Clock, MasterData, () => { }, null, null, null, opened.Add);

        var row = Assert.Single(vm.El.Assistants);
        Assert.Equal("S2 Lage (Erika Musterfrau) ändern", row.EditAutomationName);
        row.EditCommand.Execute(null);
        row.TransferCommand.Execute(null);

        Assert.Equal([RoleAssignmentDialogMode.Edit, RoleAssignmentDialogMode.Transfer], opened.Select(d => d.Mode));
        Assert.All(opened, d => Assert.Equal("S2 Lage", d.Role));

        // The Leiter post is set through FESTLEGEN, not here.
        Assert.False(vm.El.Leiter!.EditCommand.CanExecute(null));
        Assert.Equal(id, row.AssignmentId);
    }

    // A closed Einsatz has ended every Funktion, so the card is driven directly: the rule is the
    // card's, whatever the reporting line hands it (a joined client that lost write access).
    [Fact]
    public void A_read_only_cards_fuehrungsunterstuetzung_offers_neither()
    {
        var incident = NewSession().Incident;
        var opened = 0;
        var card = new ElCard(_ => { }, canEdit: false, onAssistant: (_, _) => opened++);
        var assistant = RoleAssignment.Create("S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);

        card.Update(
            new ReportingEntry(0, ReportingEntryKind.Einsatzleitung, null, null, null, [assistant], []),
            incident.LeadershipPost,
            null,
            Services.FuehrungsstufeAdvisor.Suggest(incident));

        var row = Assert.Single(card.Assistants);
        Assert.False(row.EditCommand.CanExecute(null));
        Assert.False(row.TransferCommand.CanExecute(null));
        row.EditCommand.Execute(null);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void Funktionens_bearbeiten_opens_the_dialog_for_a_running_row_only()
    {
        var session = NewSession();
        var id = Assign(session, "S2 Lage", "Erika Musterfrau");
        var opened = new List<RoleAssignmentDialogViewModel>();
        using var vm = new RolesViewModel(session, Clock, MasterData, () => { }, opened.Add);
        vm.ShowAllRoles = true;

        var row = vm.Roles.Single(r => r.Id == id);
        row.BeginEditCommand.Execute(null);

        var dialog = Assert.Single(opened);
        Assert.Equal(RoleAssignmentDialogMode.Edit, dialog.Mode);
        Assert.Equal("Erika Musterfrau", dialog.PersonName);

        session.TransferRole(id, "Mustermann, Max");

        Assert.False(vm.Roles.Single(r => r.Id == id).BeginEditCommand.CanExecute(null));
        dialog.Detach();
    }

    [Fact]
    public void Funktionen_without_a_dialog_host_offers_no_bearbeiten()
    {
        var session = NewSession();
        Assign(session, "S2 Lage", "Erika Musterfrau");
        using var vm = new RolesViewModel(session, Clock, MasterData, () => { });

        Assert.False(vm.Roles.Single().BeginEditCommand.CanExecute(null));
    }
}
