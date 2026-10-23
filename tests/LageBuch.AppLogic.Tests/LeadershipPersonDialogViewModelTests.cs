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
    public void A_role_unique_per_einsatz_held_elsewhere_is_handed_over_from_that_holder()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        var sued = AddNode(session, CommandNodeKind.Abschnitt, "Süd");
        session.AssignRole("Pressesprecher", "Musterfrau, Erika", nodeId: nord);
        var held = session.Incident.Roles.Single().Id;

        var vm = Open(session, sued, RoleSlot.None);
        vm.Role = "pressesprecher";
        vm.PersonName = "Mustermann, Max";

        Assert.Equal("„Pressesprecher“ hat bereits Musterfrau, Erika – wird übergeben.", vm.HandoverText);
        vm.SaveCommand.Execute(null);

        Assert.NotNull(session.Incident.Roles.Single(r => r.Id == held).To);
        var now = Assert.Single(Running(session));
        Assert.Equal(("Pressesprecher", "Mustermann, Max"), (now.Role, now.PersonName));
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
    public void A_role_unique_per_abschnitt_in_the_same_node_is_handed_over()
    {
        var session = NewSession();
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.AssignRole("Sicherheitsbeauftragter", "Musterfrau, Erika", nodeId: nord);

        var vm = Open(session, nord, RoleSlot.None);
        vm.Role = "Sicherheitsbeauftragter";

        Assert.Equal("„Sicherheitsbeauftragter“ hat bereits Musterfrau, Erika – wird übergeben.", vm.HandoverText);
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
    public void A_read_only_incident_cannot_save()
    {
        var session = ReadOnlySession();
        var vm = Open(session, session.Incident.CommandNodes.Single().Id, RoleSlot.Leiter);

        Assert.False(vm.SaveCommand.CanExecute(null));
    }
}
