using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class LeadershipPersonDialogViewModelTests
{
    private static readonly MasterDataSet MasterData = MasterDataSet.Empty with
    {
        Roles =
        [
            new Role("Abschnittsleiter", RoleUniqueness.Multiple, RoleSlot.Leiter),
            new Role("EL", RoleUniqueness.UniquePerIncident, RoleSlot.Leiter),
            new Role("UAL", RoleUniqueness.UniquePerSection, RoleSlot.Leiter),
            new Role("Melder", RoleUniqueness.Multiple, RoleSlot.Melder),
            new Role("S1 Personal", RoleUniqueness.Multiple, RoleSlot.Fuehrungsassistent),
            new Role("S2 Lage", RoleUniqueness.Multiple, RoleSlot.Fuehrungsassistent),
            new Role("Fachberater THW", RoleUniqueness.Multiple, RoleSlot.Fachberater),
            new Role("Fachberater Hydrant", RoleUniqueness.Multiple, RoleSlot.Fachberater),
            new Role("GF", RoleUniqueness.Multiple, RoleSlot.None),
            new Role("Pressesprecher", RoleUniqueness.UniquePerIncident, RoleSlot.None),
            new Role("Sicherheitsbeauftragter", RoleUniqueness.UniquePerSection, RoleSlot.None),
        ],
        Personnel =
        [
            new Person("Mustermann", "Max", null, "Florian Emmering 11/1", "0151 1111111"),
            new Person("Musterfrau", "Erika", null, null, "0151 2222222"),
        ],
    };

    private static LeadershipPersonDialogViewModel Open(
        IIncidentSession session, Guid? nodeId, RoleSlot slot, Action? onChanged = null, MasterDataSet? masterData = null) =>
        new(session, Clock, masterData ?? MasterData, nodeId, slot, onChanged);

    private static IEnumerable<RoleAssignment> Running(LocalIncidentSession session) => session.Incident.Roles.Where(r => r.To is null);

    private static LocalIncidentSession ReadOnlySession()
    {
        var store = new FakeStore();
        var seeded = NewSession(store);
        seeded.AddCommandNode(CommandNodeKind.Abschnitt, "Nord");
        seeded.Close();
        return LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
    }

    [Fact]
    public void Leiter_on_a_node_names_the_node_and_says_festlegen()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        var vm = Open(session, nord, RoleSlot.Leiter);

        Assert.Equal("Leiter festlegen – Nord", vm.Title);
        Assert.Equal("FESTLEGEN", vm.PrimaryText);
    }

    [Theory]
    [InlineData(RoleSlot.Leiter, "Einsatzleiter festlegen", "FESTLEGEN")]
    [InlineData(RoleSlot.Melder, "Melder festlegen – Einsatzleitung", "FESTLEGEN")]
    [InlineData(RoleSlot.Fuehrungsassistent, "Führungsassistenz hinzufügen", "HINZUFÜGEN")]
    [InlineData(RoleSlot.Fachberater, "Fachberater hinzufügen", "HINZUFÜGEN")]
    public void At_the_einsatzleitung_the_title_and_button_follow_the_slot(RoleSlot slot, string title, string primary)
    {
        var vm = Open(NewSession(), null, slot);

        Assert.Equal(title, vm.Title);
        Assert.Equal(primary, vm.PrimaryText);
    }

    [Theory]
    [InlineData(RoleSlot.Melder, "Melder festlegen – Nord", "FESTLEGEN")]
    [InlineData(RoleSlot.None, "Funktion hinzufügen – Nord", "HINZUFÜGEN")]
    public void On_a_node_the_title_and_button_follow_the_slot(RoleSlot slot, string title, string primary)
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        var vm = Open(session, nord, slot);

        Assert.Equal(title, vm.Title);
        Assert.Equal(primary, vm.PrimaryText);
    }

    [Fact]
    public void Role_options_are_the_stammdaten_roles_of_the_slot()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        Assert.Equal(["Abschnittsleiter", "EL", "UAL"], Open(session, nord, RoleSlot.Leiter).RoleOptions);
        Assert.Equal(["Melder"], Open(session, nord, RoleSlot.Melder).RoleOptions);
        Assert.Equal(["S1 Personal", "S2 Lage"], Open(session, null, RoleSlot.Fuehrungsassistent).RoleOptions);
        Assert.Equal(MasterData.Roles.Select(r => r.Name), Open(session, nord, RoleSlot.None).RoleOptions);
    }

    [Fact]
    public void Fachberater_options_add_the_common_fields_without_duplicates()
    {
        var options = Open(NewSession(), null, RoleSlot.Fachberater).RoleOptions;

        Assert.Equal("Fachberater THW", options[0]);
        Assert.Equal("Fachberater Hydrant", options[1]);
        Assert.Single(options, o => string.Equals(o, "Fachberater THW", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2 + FachberaterSuggestions.Roles.Count - 1, options.Count);
        Assert.Contains("Fachberater Polizei", options);
    }

    [Fact]
    public void Leiter_on_a_node_preselects_the_role_unique_per_abschnitt()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        Assert.Equal("UAL", Open(session, nord, RoleSlot.Leiter).Role);
    }

    [Fact]
    public void Leiter_at_the_einsatzleitung_preselects_the_role_unique_per_einsatz()
    {
        Assert.Equal("EL", Open(NewSession(), null, RoleSlot.Leiter).Role);
    }

    [Fact]
    public void Without_a_unique_leiter_role_the_first_leiter_role_is_preselected()
    {
        var md = MasterDataSet.Empty with
        {
            Roles = [new Role("GF", RoleUniqueness.Multiple, RoleSlot.None), new Role("Leiter", RoleUniqueness.Multiple, RoleSlot.Leiter)],
        };
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        Assert.Equal("Leiter", Open(session, nord, RoleSlot.Leiter, masterData: md).Role);
        Assert.Equal("Leiter", Open(session, null, RoleSlot.Leiter, masterData: md).Role);
    }

    [Fact]
    public void Melder_preselects_the_first_melder_role_and_other_slots_start_blank()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");

        Assert.Equal("Melder", Open(session, nord, RoleSlot.Melder).Role);
        Assert.Equal(string.Empty, Open(session, nord, RoleSlot.None).Role);
        Assert.Equal(string.Empty, Open(session, null, RoleSlot.Fuehrungsassistent).Role);
        Assert.Equal(string.Empty, Open(session, null, RoleSlot.Fachberater).Role);
    }

    [Fact]
    public void Setting_the_leiter_of_a_node_assigns_it_there_with_the_slot_and_closes()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var changes = new[] { 0 };
        var vm = Open(session, nord, RoleSlot.Leiter, () => changes[0]++);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        var r = Assert.Single(session.Incident.Roles);
        Assert.Equal(("UAL", "Mustermann, Max", nord, RoleSlot.Leiter, "Nord"), (r.Role, r.PersonName, r.NodeId, r.Slot, r.Section));
        Assert.Equal("Florian Emmering 11/1", r.CallSign);
        Assert.Equal("0151 1111111", r.Phone);
        Assert.Equal(T0, r.From);
        Assert.Equal(1, changes[0]);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_second_leiter_for_a_node_hands_over_from_the_running_one()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.AssignRole("Abschnittsleiter", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Leiter);
        var first = session.Incident.Roles.Single().Id;

        var vm = Open(session, nord, RoleSlot.Leiter);

        // The handover keeps the holder's Funktion, so the field shows it and is locked, rather than
        // offering the preselected "UAL" that the save would silently drop.
        Assert.Equal("Abschnittsleiter", vm.Role);
        Assert.False(vm.IsRoleEditable);
        Assert.Equal("Übergabe: Musterfrau, Erika → neu", vm.HandoverText);
        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.NotNull(session.Incident.Roles.Single(r => r.Id == first).To);
        var now = Assert.Single(Running(session));
        Assert.Equal(("Abschnittsleiter", "Mustermann, Max", nord, RoleSlot.Leiter), (now.Role, now.PersonName, now.NodeId, now.Slot));
    }

    [Fact]
    public void The_leiter_of_another_node_is_not_handed_over()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        session.AssignRole("Abschnittsleiter", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Leiter);

        var vm = Open(session, sued, RoleSlot.Leiter);
        vm.Role = "Abschnittsleiter";
        vm.PersonName = "Mustermann, Max";

        Assert.Null(vm.HandoverText);
        vm.SaveCommand.Execute(null);
        Assert.Equal(2, Running(session).Count());
    }

    [Fact]
    public void A_new_einsatzleiter_hands_over_from_the_running_one()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", slot: RoleSlot.Leiter);

        var vm = Open(session, null, RoleSlot.Leiter);

        Assert.Equal("Übergabe: Musterfrau, Erika → neu", vm.HandoverText);
        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        var now = Assert.Single(Running(session));
        Assert.Equal(("EL", "Mustermann, Max", (Guid?)null, RoleSlot.Leiter), (now.Role, now.PersonName, now.NodeId, now.Slot));
    }

    [Fact]
    public void A_leiter_in_a_free_text_abschnitt_is_not_the_einsatzleiter()
    {
        var session = NewSession();
        session.AssignRole("Abschnittsleiter", "Musterfrau, Erika", section: "Nord", slot: RoleSlot.Leiter);

        var vm = Open(session, null, RoleSlot.Leiter);
        vm.Role = "Abschnittsleiter";

        Assert.Null(vm.HandoverText);
    }

    [Fact]
    public void A_melder_slot_holder_is_handed_over()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.AssignRole("Melder", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Melder);

        var vm = Open(session, nord, RoleSlot.Melder);
        vm.PersonName = "Mustermann, Max";

        Assert.Equal("Übergabe: Musterfrau, Erika → neu", vm.HandoverText);
        vm.SaveCommand.Execute(null);
        var now = Assert.Single(Running(session));
        Assert.Equal(("Mustermann, Max", RoleSlot.Melder, nord), (now.PersonName, now.Slot, now.NodeId));
    }

    [Fact]
    public void A_fuehrungsassistent_added_twice_gives_two_assignments()
    {
        var session = NewSession();
        foreach (var name in new[] { "Mustermann, Max", "Musterfrau, Erika" })
        {
            var vm = Open(session, null, RoleSlot.Fuehrungsassistent);
            vm.Role = "S1 Personal";
            vm.PersonName = name;
            Assert.Null(vm.HandoverText);
            vm.SaveCommand.Execute(null);
        }

        Assert.Equal(2, Running(session).Count(r => r.Slot == RoleSlot.Fuehrungsassistent && r.NodeId is null));
    }

    [Fact]
    public void A_member_cannot_take_a_role_unique_per_einsatz_held_elsewhere()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        session.AssignRole("Pressesprecher", "Musterfrau, Erika", nodeId: nord);
        var before = session.Incident.Roles.Single();

        var vm = Open(session, sued, RoleSlot.None);
        vm.Role = "pressesprecher";
        vm.PersonName = "Mustermann, Max";

        AssertRefusedAndUnchanged(session, vm, "„Pressesprecher“ ist bereits mit Musterfrau, Erika besetzt – dort übergeben.", before);
    }

    [Fact]
    public void A_fuehrungsassistent_typed_as_el_leaves_the_running_el_where_it_is()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", slot: RoleSlot.Leiter);
        var before = session.Incident.Roles.Single();

        var vm = Open(session, null, RoleSlot.Fuehrungsassistent);
        vm.Role = "EL";
        vm.PersonName = "Mustermann, Max";

        AssertRefusedAndUnchanged(session, vm, "„EL“ ist bereits mit Musterfrau, Erika besetzt – dort übergeben.", before);
    }

    [Fact]
    public void A_fachberater_cannot_take_a_role_unique_per_einsatz_held_elsewhere()
    {
        var session = NewSession();
        session.AssignRole("Pressesprecher", "Musterfrau, Erika");
        var before = session.Incident.Roles.Single();

        var vm = Open(session, null, RoleSlot.Fachberater);
        vm.Role = "Pressesprecher";
        vm.PersonName = "Mustermann, Max";

        AssertRefusedAndUnchanged(session, vm, "„Pressesprecher“ ist bereits mit Musterfrau, Erika besetzt – dort übergeben.", before);
    }

    [Fact]
    public void Changing_away_from_a_held_unique_role_clears_the_error()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", slot: RoleSlot.Leiter);
        var vm = Open(session, null, RoleSlot.Fuehrungsassistent);

        vm.Role = "EL";
        Assert.NotNull(vm.RoleError);
        vm.Role = "S2 Lage";

        Assert.Null(vm.RoleError);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void A_role_unique_per_abschnitt_in_another_node_of_the_same_name_is_no_conflict()
    {
        var session = NewSession();
        var a1 = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var a2 = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2");
        var nord1 = AddNode(session, CommandNodeKind.Unterabschnitt, "Nord", a1);
        var nord2 = AddNode(session, CommandNodeKind.Unterabschnitt, "Nord", a2);
        session.AssignRole("Sicherheitsbeauftragter", "Musterfrau, Erika", nodeId: nord1);

        var vm = Open(session, nord2, RoleSlot.None);
        vm.Role = "Sicherheitsbeauftragter";
        vm.PersonName = "Mustermann, Max";

        Assert.Null(vm.HandoverText);
        vm.SaveCommand.Execute(null);
        Assert.Equal(2, Running(session).Count());
        Assert.Contains(Running(session), r => r.NodeId == nord2 && r.PersonName == "Mustermann, Max");
    }

    [Fact]
    public void A_member_cannot_take_a_role_unique_per_abschnitt_held_in_the_same_node()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.AssignRole("Sicherheitsbeauftragter", "Musterfrau, Erika", nodeId: nord);
        var before = session.Incident.Roles.Single();

        var vm = Open(session, nord, RoleSlot.None);
        vm.Role = "Sicherheitsbeauftragter";
        vm.PersonName = "Mustermann, Max";

        AssertRefusedAndUnchanged(
            session, vm, "„Sicherheitsbeauftragter“ ist bereits mit Musterfrau, Erika besetzt – dort übergeben.", before);
    }

    [Fact]
    public void The_recorded_slot_is_the_targets_even_for_an_off_catalogue_role()
    {
        var session = NewSession();
        var vm = Open(session, null, RoleSlot.Fachberater);
        vm.Role = "Fachberater Imker";
        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        var gf = Open(session, null, RoleSlot.Fuehrungsassistent);
        gf.Role = "GF";
        gf.PersonName = "Musterfrau, Erika";
        gf.SaveCommand.Execute(null);

        Assert.Equal(RoleSlot.Fachberater, session.Incident.Roles[0].Slot);
        Assert.Equal("Fachberater Imker", session.Incident.Roles[0].Role);
        Assert.Equal(RoleSlot.Fuehrungsassistent, session.Incident.Roles[1].Slot);
    }

    [Fact]
    public void A_typed_role_takes_the_catalogues_spelling()
    {
        var session = NewSession();
        var vm = Open(session, null, RoleSlot.Fuehrungsassistent);
        vm.Role = " s2 lage ";
        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.Equal("S2 Lage", session.Incident.Roles.Single().Role);
    }

    [Fact]
    public void Naming_a_person_from_the_roster_fills_and_swaps_funkrufname_and_handy()
    {
        var vm = Open(NewSession(), null, RoleSlot.Fachberater);

        vm.PersonName = "mustermann, max";
        Assert.Equal(("Florian Emmering 11/1", "0151 1111111"), (vm.CallSign, vm.Phone));

        vm.PersonName = "Musterfrau, Erika";
        Assert.Equal((null, "0151 2222222"), (vm.CallSign, vm.Phone));

        vm.Phone = "0170 9999999";
        vm.PersonName = "Mustermann, Max";
        Assert.Equal(("Florian Emmering 11/1", "0170 9999999"), (vm.CallSign, vm.Phone));
    }

    [Fact]
    public void Person_options_are_the_rosters_display_names()
    {
        Assert.Equal(["Mustermann, Max", "Musterfrau, Erika"], Open(NewSession(), null, RoleSlot.Leiter).PersonOptions);
    }

    [Fact]
    public void A_blank_role_and_name_are_refused_with_field_errors()
    {
        var session = NewSession();
        var vm = Open(session, null, RoleSlot.Fachberater);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        Assert.Null(vm.RoleError);
        Assert.Null(vm.PersonError);
        vm.SaveCommand.Execute(null);

        Assert.Equal("Funktion fehlt.", vm.RoleError);
        Assert.Equal("Name fehlt.", vm.PersonError);
        Assert.Empty(session.Incident.Roles);
        Assert.Equal(0, closed);

        vm.Role = "Fachberater THW";
        vm.PersonName = "Mustermann, Max";
        Assert.Null(vm.RoleError);
        Assert.Null(vm.PersonError);
    }

    [Fact]
    public void A_node_dissolved_meanwhile_is_refused_with_a_save_error()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = Open(session, nord, RoleSlot.Leiter);
        var closed = 0;
        vm.Closed += (_, _) => closed++;
        session.DissolveCommandNode(nord);

        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.Equal("Dieser Abschnitt wurde inzwischen aufgelöst.", vm.SaveError);
        Assert.Empty(session.Incident.Roles);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void A_handover_text_follows_a_peers_change_while_open()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = Open(session, nord, RoleSlot.Leiter);
        Assert.Null(vm.HandoverText);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.AssignRole("UAL", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Leiter);

        Assert.Contains(nameof(vm.HandoverText), raised);
        Assert.Equal("Übergabe: Musterfrau, Erika → neu", vm.HandoverText);
    }

    [Fact]
    public void Cancel_closes_at_once_when_nothing_was_typed_and_asks_after_typing()
    {
        var vm = Open(NewSession(), null, RoleSlot.Leiter);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        Assert.False(vm.HasChanges);
        vm.PersonName = "Mustermann, Max";
        Assert.True(vm.HasChanges);
        vm.CancelCommand.Execute(null);
        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(0, closed);

        vm.ConfirmDiscardCommand.Execute(null);
        Assert.Equal(1, closed);

        var untouched = Open(NewSession(), null, RoleSlot.Leiter);
        untouched.Closed += (_, _) => closed++;
        untouched.CancelCommand.Execute(null);
        Assert.Equal(2, closed);
    }

    [Fact]
    public void An_einsatzleiter_recorded_in_funktionen_is_placed_and_handed_over()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika"); // FUNKTIONEN: no node, no Abschnitt, slot None
        using var board = NewVm(session, masterData: MasterData);
        Assert.Null(board.El.Leiter);

        var vm = Open(session, null, RoleSlot.Leiter);
        vm.PersonName = "Mustermann, Max";

        Assert.Equal("„EL“ hat bereits Musterfrau, Erika – wird hierher übergeben.", vm.HandoverText);
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.SaveError);
        Assert.Equal("Mustermann, Max", board.El.Leiter?.PersonName);
        var el = ReportingLine.Of(session.Incident).Single(e => e.Kind == ReportingEntryKind.Einsatzleitung).Leiter;
        Assert.Equal(("EL", "Mustermann, Max", RoleSlot.Leiter), (el?.Role, el?.PersonName, el?.Slot));
        Assert.Single(Running(session));
    }

    [Fact]
    public void A_nodes_leiter_holding_the_el_funktion_is_moved_up_to_the_einsatzleitung()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.AssignRole("EL", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Leiter);
        using var board = NewVm(session, masterData: MasterData);

        var vm = Open(session, null, RoleSlot.Leiter);
        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.Equal("Mustermann, Max", board.El.Leiter?.PersonName);
        Assert.Null(board.Sections.Single().Leiter);
        var now = Assert.Single(Running(session));
        Assert.Equal(((Guid?)null, (string?)null), (now.NodeId, now.Section));
    }

    [Fact]
    public void Naming_the_holder_again_only_places_the_funktion()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", "Florian Emmering 1/1");
        var held = session.Incident.Roles.Single().Id;
        var changes = new[] { 0 };
        using var board = NewVm(session, masterData: MasterData);
        var vm = Open(session, null, RoleSlot.Leiter, () => changes[0]++);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.PersonName = "musterfrau, erika";

        Assert.Equal("„EL“ (Musterfrau, Erika) wird hierher übernommen.", vm.HandoverText);
        vm.SaveCommand.Execute(null);

        var only = Assert.Single(session.Incident.Roles);
        Assert.Equal((held, (DateTimeOffset?)null, RoleSlot.Leiter, "Florian Emmering 1/1"), (only.Id, only.To, only.Slot, only.CallSign));
        Assert.Equal("Musterfrau, Erika", board.El.Leiter?.PersonName);
        Assert.Equal(1, changes[0]);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Naming_the_holder_of_the_post_again_writes_nothing()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", slot: RoleSlot.Leiter);
        var before = session.Incident.Roles.Single();
        var changes = new[] { 0 };
        var vm = Open(session, null, RoleSlot.Leiter, () => changes[0]++);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.PersonName = "Musterfrau, Erika";

        Assert.Equal("„EL“ hat bereits Musterfrau, Erika.", vm.HandoverText);
        vm.SaveCommand.Execute(null);

        Assert.Same(before, session.Incident.Roles.Single());
        Assert.Equal(0, changes[0]);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_locked_funktion_does_not_block_the_handover()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.AssignRole("Melder", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Melder);
        var vm = Open(session, nord, RoleSlot.Melder);

        vm.Role = string.Empty;
        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.RoleError);
        Assert.Equal(("Melder", "Mustermann, Max"), (Running(session).Single().Role, Running(session).Single().PersonName));
    }

    [Fact]
    public void A_post_a_peer_fills_while_open_locks_the_funktion_to_the_holders()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var vm = Open(session, nord, RoleSlot.Leiter);
        Assert.True(vm.IsRoleEditable);

        session.AssignRole("Abschnittsleiter", "Musterfrau, Erika", nodeId: nord, slot: RoleSlot.Leiter);

        Assert.False(vm.IsRoleEditable);
        Assert.Equal("Abschnittsleiter", vm.Role);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void A_refused_handover_surfaces_as_a_save_error()
    {
        // A hostile file: the holder starts after now, so ending it is refused by the Domain.
        var holder = RoleAssignment.Create("EL", "Musterfrau, Erika", from: T0.AddHours(1), slot: RoleSlot.Leiter);
        var session = Hostile([], roles: [holder]);
        var vm = Open(session, null, RoleSlot.Leiter);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.StartsWith("Nicht möglich: ", vm.SaveError, StringComparison.Ordinal);
        Assert.Equal("Musterfrau, Erika", Running(session).Single().PersonName);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void A_refused_placement_surfaces_as_a_save_error()
    {
        // A hostile file carrying one id twice, ended first: the dialog finds the running EL, the
        // Domain's placement finds the ended copy and refuses.
        var running = RoleAssignment.Create("EL", "Musterfrau, Erika", from: T0.AddHours(-1));
        var session = Hostile([], roles: [running.EndedAt(T0.AddMinutes(-30)), running]);
        var vm = Open(session, null, RoleSlot.Leiter);
        var closed = 0;
        vm.Closed += (_, _) => closed++;

        vm.PersonName = "Mustermann, Max";
        vm.SaveCommand.Execute(null);

        Assert.StartsWith("Nicht möglich: ", vm.SaveError, StringComparison.Ordinal);
        Assert.Equal(("Musterfrau, Erika", RoleSlot.None), (Running(session).Single().PersonName, Running(session).Single().Slot));
        Assert.Equal(0, closed);
    }

    [Fact]
    public void Funkrufname_suggestions_are_the_stammdaten_call_signs()
    {
        var md = MasterData with { Vehicles = [new Vehicle("FF Aich", "Florian Aich 40/1", 9)] };

        Assert.Equal(["Florian Aich 40/1", "Florian Emmering 11/1"], Open(NewSession(), null, RoleSlot.Leiter, masterData: md).CallSignOptions);
    }

    [Fact]
    public void A_detached_dialog_stops_following_the_session()
    {
        var session = NewSession();
        var vm = Open(session, null, RoleSlot.Leiter);
        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;

        vm.Detach();
        session.AssignRole("EL", "Musterfrau, Erika", slot: RoleSlot.Leiter);

        Assert.Equal(0, raised);
    }

    // A uniqueness conflict outside a Leiter or Melder post: a field error, no hint, no save, and the
    // holder untouched even when the command is forced.
    private static void AssertRefusedAndUnchanged(
        LocalIncidentSession session, LeadershipPersonDialogViewModel vm, string error, RoleAssignment before)
    {
        Assert.Equal(error, vm.RoleError);
        Assert.Null(vm.HandoverText);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);

        Assert.Same(before, session.Incident.Roles.Single());
        Assert.Equal(error, vm.RoleError);
    }

    [Fact]
    public void A_read_only_incident_cannot_save()
    {
        var session = ReadOnlySession();
        var vm = Open(session, session.Incident.CommandNodes.Single().Id, RoleSlot.Leiter);

        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    // Ruling P10: naming the holder skips the handover, which is the only thing that would carry a
    // newly typed Funkrufname or Handynummer, so the fields show the holder's and lock instead of
    // swallowing the input.
    [Fact]
    public void Naming_the_holder_locks_the_contact_fields_to_the_holders_values()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", "Florian Emmering 1/1", phone: "0151 2222222");
        var vm = Open(session, null, RoleSlot.Leiter);
        Assert.True(vm.IsContactEditable);
        vm.CallSign = "Florian Aich 9/1";
        vm.Phone = "0170 0000000";

        vm.PersonName = "Musterfrau, Erika";

        Assert.False(vm.IsContactEditable);
        Assert.Equal("Florian Emmering 1/1", vm.CallSign);
        Assert.Equal("0151 2222222", vm.Phone);
        vm.SaveCommand.Execute(null);
        var only = Assert.Single(session.Incident.Roles);
        Assert.Equal(("Florian Emmering 1/1", "0151 2222222"), (only.CallSign, only.Phone));
    }

    [Fact]
    public void Naming_the_holder_of_the_post_again_locks_the_contact_fields_too()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", "Florian Emmering 1/1", slot: RoleSlot.Leiter);
        var vm = Open(session, null, RoleSlot.Leiter);
        vm.CallSign = "Florian Aich 9/1";

        vm.PersonName = "Musterfrau, Erika";

        Assert.False(vm.IsContactEditable);
        Assert.Equal("Florian Emmering 1/1", vm.CallSign);
    }

    [Fact]
    public void A_real_handover_keeps_the_contact_fields_editable_and_unlocks_again()
    {
        var session = NewSession();
        session.AssignRole("EL", "Musterfrau, Erika", "Florian Emmering 1/1", phone: "0151 2222222");
        var vm = Open(session, null, RoleSlot.Leiter);

        vm.PersonName = "Mustermann, Max";
        vm.CallSign = "Florian Aich 9/1";

        Assert.True(vm.IsContactEditable);
        Assert.Equal("Florian Aich 9/1", vm.CallSign);

        vm.PersonName = "Musterfrau, Erika";
        Assert.False(vm.IsContactEditable);

        vm.PersonName = "Mustermann, Max";
        Assert.True(vm.IsContactEditable);
        Assert.Equal("Florian Emmering 11/1", vm.CallSign); // back to the new person's roster values
        vm.SaveCommand.Execute(null);
        Assert.Equal("Florian Emmering 11/1", Assert.Single(Running(session)).CallSign);
    }
}
