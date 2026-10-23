using System.Collections.Specialized;
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

        session.UpdateCommandNode(abschnitt, "Abschnitt 1 Nordost", "Riegelstellung", null, "TMO 312", null, null);

        Assert.Same(card, vm.Sections[0]);
        Assert.Equal("Abschnitt 1 Nordost", card.Name);
        Assert.Equal("Riegelstellung", card.Auftrag);
        Assert.Equal("TMO 312", card.Funkgruppe);
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
}
