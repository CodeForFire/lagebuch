using System.Collections.Specialized;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class LeadershipViewModelRefreshTests
{
    [Fact]
    public void Picking_an_Abschnitt_moves_an_unplaced_unit_onto_its_card()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var unitId = AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        var changes = new int[1];
        using var vm = NewVm(session, changes);

        var row = Assert.Single(vm.UnplacedUnits);
        row.NodeChoices.Single(c => c.NodeId == abschnitt).SelectCommand.Execute(null);

        Assert.Equal(abschnitt, session.Incident.Forces.Single(u => u.Id == unitId).NodeId);
        Assert.Equal(1, changes[0]);
        Assert.Empty(vm.UnplacedUnits);
        Assert.False(vm.HasUnplacedUnits);
        Assert.Equal(unitId, Assert.Single(vm.Sections[0].Units).Id);
    }

    [Fact]
    public void Picking_the_current_entry_does_nothing()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddUnit(session, "FF Emmering", "Florian Emmering 40/1");
        var changes = new int[1];
        using var vm = NewVm(session, changes);

        vm.UnplacedUnits[0].NodeChoices.Single(c => c.IsCurrent).SelectCommand.Execute(null);

        Assert.Equal(0, changes[0]);
        Assert.Single(vm.UnplacedUnits);
    }

    [Fact]
    public void Picking_an_Abschnitt_on_a_read_only_incident_does_nothing()
    {
        var store = new FakeStore();
        var seed = NewSession(store);
        var abschnitt = AddNode(seed, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddUnit(seed, "FF Emmering", "Florian Emmering 40/1");
        seed.Close();
        var session = LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath);
        var changes = new int[1];
        using var vm = NewVm(session, changes);

        vm.UnplacedUnits[0].NodeChoices.Single(c => c.NodeId == abschnitt).SelectCommand.Execute(null);

        Assert.Equal(0, changes[0]);
        Assert.Null(session.Incident.Forces[0].NodeId);
        Assert.Single(vm.UnplacedUnits);
    }

    [Fact]
    public void A_refresh_with_no_change_keeps_every_card_and_row_and_raises_nothing()
    {
        var session = NewSession();
        session.AssignRole("Einsatzleiter", "Max Mustermann", slot: RoleSlot.Leiter);
        session.AssignRole("Führungsassistent", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        session.AssignRole("Abschnittsleiter", "Hans Huber", nodeId: abschnitt, slot: RoleSlot.Leiter);
        session.AssignRole("Sicherheitsbeauftragter", "Peter Maier", nodeId: abschnitt);
        AddUnit(session, "FF Emmering", "Florian Emmering 40/1", abschnitt);
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", abschnitt);
        AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        session.AssignRole("Fachberater THW", "Thomas Weber", slot: RoleSlot.Fachberater);
        AddUnit(session, "FF Aich", "Florian Aich 42/1");
        session.AssignRole("Abschnittsleiter", "Josef Bauer", section: "Süd", slot: RoleSlot.Leiter);
        for (var i = 2; i <= 6; i++)
        {
            AddNode(session, CommandNodeKind.Abschnitt, $"Abschnitt {i}");
        }

        using var vm = NewVm(session);
        var card = vm.Sections[0];
        var leiter = vm.El.Leiter;
        var cardLeiter = card.Leiter;
        var unit = card.Units[0];
        var choices = unit.NodeChoices;
        var snapshot = new object?[]
        {
            card, card.Children[0], card.Members[0], vm.Partners[0], vm.Fachberater[0], vm.UnplacedUnits[0],
            vm.FreeTextSections[0], vm.FreeTextSections[0].People[0], vm.El.Assistants[0],
        };
        var collectionChanges = 0;
        void Count(object? sender, NotifyCollectionChangedEventArgs e) => collectionChanges++;
        foreach (var collection in new INotifyCollectionChanged[]
        {
            vm.Sections, vm.Partners, vm.Fachberater, vm.UnplacedUnits, vm.FreeTextSections, vm.Hints,
            vm.El.Assistants, card.Members, card.Units, card.Children, vm.FreeTextSections[0].People,
        })
        {
            collection.CollectionChanged += Count;
        }

        var raised = new List<string?>();
        unit.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.El.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung Abschnitt 1"); // raises Changed only

        Assert.Equal(0, collectionChanges);
        Assert.Empty(raised);
        Assert.Same(card, vm.Sections[0]);
        Assert.Same(leiter, vm.El.Leiter);
        Assert.Same(cardLeiter, card.Leiter);
        Assert.Same(unit, card.Units[0]);
        Assert.Same(choices, unit.NodeChoices);
        Assert.Equal(
            snapshot,
            new object?[]
            {
                vm.Sections[0], card.Children[0], card.Members[0], vm.Partners[0], vm.Fachberater[0],
                vm.UnplacedUnits[0], vm.FreeTextSections[0], vm.FreeTextSections[0].People[0], vm.El.Assistants[0],
            },
            ReferenceEqualityComparer.Instance);
    }

    [Fact]
    public void Renaming_a_node_updates_the_same_card()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddUnit(session, "FF Emmering", "Florian Emmering 40/1", abschnitt);
        using var vm = NewVm(session);
        var card = vm.Sections[0];
        var unit = card.Units[0];

        session.UpdateCommandNode(
            abschnitt, "Abschnitt 1 Nordost", "Riegelstellung", null, null, null, null, new Funkgruppen(null, ["302_F"]));

        Assert.Same(card, vm.Sections[0]);
        Assert.Equal("Abschnitt 1 Nordost", card.Name);
        Assert.Equal("Riegelstellung", card.Auftrag);
        Assert.Equal([new FunkgruppeLine("302_F", null)], card.DmoLines);
        Assert.Same(unit, card.Units[0]);
        Assert.Equal("Abschnitt 1 Nordost", unit.NodeLabel);
    }

    [Fact]
    public void A_corrected_phone_number_and_Staerke_update_the_same_rows()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        session.AssignRole("Melder", "Lena Lehner", nodeId: abschnitt, slot: RoleSlot.Melder);
        var unitId = AddUnit(session, "FF Emmering", "Florian Emmering 40/1", abschnitt);
        using var vm = NewVm(session);
        var melder = vm.Sections[0].Melder;
        var unit = vm.Sections[0].Units[0];
        Assert.NotNull(melder);

        session.EditRolePhone(melder.AssignmentId, "0170 1112223");
        session.UpdateForceStrength(unitId, 2, 12, 6, 1);

        Assert.Same(melder, vm.Sections[0].Melder);
        Assert.Equal("0170 1112223", melder.Phone);
        Assert.Same(unit, vm.Sections[0].Units[0]);
        Assert.Equal("1/2/9/12", unit.StrengthText);
        Assert.Equal(6, unit.ScbaCount);
    }

    [Fact]
    public void Handing_over_the_Einsatzleitung_shows_the_new_Leiter()
    {
        var session = NewSession();
        session.AssignRole("Einsatzleiter", "Max Mustermann", slot: RoleSlot.Leiter);
        using var vm = NewVm(session);
        var before = vm.El.Leiter;
        Assert.NotNull(before);

        session.TransferRole(before.AssignmentId, "Erika Musterfrau", "Florian Emmering 2");

        Assert.NotNull(vm.El.Leiter);
        Assert.NotSame(before, vm.El.Leiter);
        Assert.Equal("Erika Musterfrau · Florian Emmering 2", vm.El.Leiter.Text);
    }

    [Fact]
    public void A_dissolved_node_leaves_the_board_and_the_others_keep_their_cards()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var bereitstellung = AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz");
        using var vm = NewVm(session);
        var card = vm.Sections.Single(c => c.Id == abschnitt);

        session.DissolveCommandNode(bereitstellung);

        Assert.Same(card, Assert.Single(vm.Sections));
    }

    [Fact]
    public void Dispose_stops_following_the_session()
    {
        var session = NewSession();
        var vm = NewVm(session);

        vm.Dispose();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");

        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void The_top_row_lists_the_Einsatzleitung_the_partners_and_the_Fachberater_in_one_flat_list()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        session.AssignRole("Fachberater THW", "Peter Maier", slot: RoleSlot.Fachberater);
        using var vm = NewVm(session);

        Assert.Equal(3, vm.TopRow.Count);
        Assert.Same(vm.El, vm.TopRow[0]);
        Assert.Same(vm.Partners.Single(), vm.TopRow[1]);
        var group = Assert.IsType<FachberaterGroup>(vm.TopRow[2]);
        Assert.Same(vm.Fachberater, group.People);
    }

    [Fact]
    public void The_top_row_keeps_its_cards_across_a_refresh_and_slots_a_new_partner_before_the_Fachberater()
    {
        var session = NewSession();
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        session.AssignRole("Fachberater THW", "Peter Maier", slot: RoleSlot.Fachberater);
        using var vm = NewVm(session);
        var before = vm.TopRow.ToList();
        var resets = 0;
        vm.TopRow.CollectionChanged += (_, e) => resets += e.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;

        session.UpdateLeadershipPost("Feuerwehrhaus Emmering", null, null, null);
        Assert.Equal(before.Count, vm.TopRow.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Same(before[i], vm.TopRow[i]);
        }

        AddNode(session, CommandNodeKind.Partner, "Polizei");
        Assert.Equal(4, vm.TopRow.Count);
        Assert.Same(before[0], vm.TopRow[0]);
        Assert.Same(before[1], vm.TopRow[1]);
        Assert.Same(vm.Partners[1], vm.TopRow[2]);
        Assert.Same(before[2], vm.TopRow[3]);

        session.DissolveCommandNode(rd);
        Assert.Equal(3, vm.TopRow.Count);
        Assert.Same(vm.Partners.Single(), vm.TopRow[1]);
        Assert.Equal(0, resets);
    }

    [Fact]
    public void Without_Fachberater_the_top_row_has_no_Fachberater_card()
    {
        using var vm = NewVm(NewSession());

        Assert.Same(vm.El, Assert.Single(vm.TopRow));
    }

    [Fact]
    public void Changing_the_einsatzleitungs_funkgruppen_re_marks_the_same_cards()
    {
        var session = NewSession();
        session.UpdateLeadershipPost(null, null, null, new Funkgruppen(["FW 1000"], ["301_F"]));
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.UpdateCommandNode(nord, "Nord", null, null, null, null, null, new Funkgruppen(["FW 2000"], ["305_F"]));
        var dach = AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", nord);
        session.UpdateCommandNode(dach, "Dach", null, null, null, null, null, new Funkgruppen(["FW 1000"], ["305_F"]));
        using var vm = NewVm(session);
        var card = vm.Sections[0];
        var sub = Assert.Single(card.Children);

        Assert.Equal("Keine gemeinsame TMO- und DMO-Funkgruppe mit Einsatzleitung", card.FunkHint);
        Assert.Equal("Keine gemeinsame TMO-Funkgruppe mit Abschnitt Nord", sub.FunkHint);
        Assert.Equal([new FunkgruppeLine("305_F", true)], sub.DmoLines);
        Assert.True(sub.HasDmo);

        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        session.UpdateLeadershipPost(null, null, null, new Funkgruppen(["FW 2000"], ["305_F"]));

        Assert.Same(card, vm.Sections[0]);
        Assert.Null(card.FunkHint);
        Assert.Equal([new FunkgruppeLine("FW 2000", true)], card.TmoLines);
        Assert.Equal(
            [nameof(BoardNodeCard.TmoLines), nameof(BoardNodeCard.HasTmo), nameof(BoardNodeCard.DmoLines), nameof(BoardNodeCard.HasDmo)],
            raised.Where(p => p is "TmoLines" or "HasTmo" or "DmoLines" or "HasDmo"));

        // The Abschnitt's DMO taken away: the row goes, and the view hears it.
        raised.Clear();
        session.UpdateCommandNode(nord, "Nord", null, null, null, null, null, new Funkgruppen(["FW 2000"], null));
        Assert.True(card.HasTmo);
        Assert.False(card.HasDmo);
        Assert.Equal([nameof(BoardNodeCard.DmoLines), nameof(BoardNodeCard.HasDmo)], raised.Where(p => p is "DmoLines" or "HasDmo"));

        // A refresh that changes nothing on the card leaves its lines alone.
        raised.Clear();
        session.UpdateLeadershipPost("Feuerwehrhaus", null, null, new Funkgruppen(["FW 2000"], ["305_F"]));
        Assert.DoesNotContain(nameof(BoardNodeCard.TmoLines), raised);
        Assert.DoesNotContain(nameof(BoardNodeCard.DmoLines), raised);
    }

    [Fact]
    public void A_card_with_two_tmo_funkgruppen_one_shared_shows_no_warning()
    {
        // #887: one Funkgruppe in common with the level above is enough.
        var session = NewSession();
        session.UpdateLeadershipPost(null, null, null, new Funkgruppen(["FW 1000"], null));
        var nord = AddNode(session, CommandNodeKind.Abschnitt, "Nord");
        session.UpdateCommandNode(nord, "Nord", null, null, null, null, null, new Funkgruppen(["FW 1000", "FW 2000"], null));
        using var vm = NewVm(session);
        var card = vm.Sections[0];

        Assert.Null(card.FunkHint);
        Assert.Equal([new FunkgruppeLine("FW 1000", true), new FunkgruppeLine("FW 2000", null)], card.TmoLines);
        Assert.DoesNotContain(card.TmoLines, l => l.IsMarkedNotShared);
    }

    [Fact]
    public void The_funk_hint_names_each_kind_without_a_shared_funkgruppe()
    {
        var shared = new FunkKindCheck(true, ["FW 1000"]);
        var none = new FunkKindCheck(true, []);
        FunkCheckResult Result(FunkKindCheck tmo, FunkKindCheck dmo) => new(Guid.NewGuid(), "Abschnitt Nord", tmo, dmo);

        Assert.Null(BoardNodeCard.FunkHintOf(null));
        Assert.Null(BoardNodeCard.FunkHintOf(Result(shared, FunkKindCheck.NotChecked)));
        Assert.Equal("Keine gemeinsame TMO-Funkgruppe mit Abschnitt Nord", BoardNodeCard.FunkHintOf(Result(none, shared)));
        Assert.Equal("Keine gemeinsame DMO-Funkgruppe mit Abschnitt Nord", BoardNodeCard.FunkHintOf(Result(FunkKindCheck.NotChecked, none)));
        Assert.Equal("Keine gemeinsame TMO- und DMO-Funkgruppe mit Abschnitt Nord", BoardNodeCard.FunkHintOf(Result(none, none)));
    }

    [Fact]
    public void A_funkgruppe_line_names_its_mark_for_a_screen_reader()
    {
        Assert.Equal(
            ["gemeinsame Funkgruppe", "keine gemeinsame Funkgruppe", string.Empty],
            new bool?[] { true, false, null }.Select(s => new FunkgruppeLine("FW 1000", s).MarkName));
        Assert.Equal((true, false), (new FunkgruppeLine("FW 1000", true).IsMarkedShared, new FunkgruppeLine("FW 1000", true).IsMarkedNotShared));
        Assert.Equal((false, true), (new FunkgruppeLine("FW 1000", false).IsMarkedShared, new FunkgruppeLine("FW 1000", false).IsMarkedNotShared));
        Assert.Equal((false, false), (new FunkgruppeLine("FW 1000", null).IsMarkedShared, new FunkgruppeLine("FW 1000", null).IsMarkedNotShared));
    }
}
