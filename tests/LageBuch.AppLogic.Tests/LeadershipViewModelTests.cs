using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using static LageBuch.AppLogic.Tests.LeadershipBoard;

namespace LageBuch.AppLogic.Tests;

public class LeadershipViewModelTests
{
    [Fact]
    public void An_empty_incident_shows_an_empty_sheet_and_suggests_Stufe_A()
    {
        using var vm = NewVm(NewSession());

        Assert.Empty(vm.Sections);
        Assert.Empty(vm.Partners);
        Assert.Empty(vm.Fachberater);
        Assert.Empty(vm.UnplacedUnits);
        Assert.Empty(vm.FreeTextSections);
        Assert.Empty(vm.Hints);
        Assert.False(vm.HasPartners);
        Assert.False(vm.HasFachberater);
        Assert.False(vm.HasUnplacedUnits);
        Assert.False(vm.HasFreeTextSections);
        Assert.Null(vm.El.Leiter);
        Assert.Empty(vm.El.Assistants);
        Assert.Equal("Keine Führungsstufe festgelegt", vm.El.StufeText);
        Assert.Equal("Vorschlag: Stufe A – keine Abschnitte, keine Einheiten", vm.El.SuggestionText);
        Assert.False(vm.El.MatchesSuggestion);
        Assert.Equal("ILS", vm.DispatchCentreName);
        Assert.False(vm.IsReadOnly);
    }

    [Fact]
    public void The_dispatch_centre_name_comes_from_the_Stammdaten()
    {
        var masterData = MasterDataSet.Empty with
        {
            Settings = IncidentSettings.Defaults with { DispatchCentreName = "ILS Fürstenfeldbruck" },
        };

        using var vm = NewVm(NewSession(), masterData: masterData);

        Assert.Equal("ILS Fürstenfeldbruck", vm.DispatchCentreName);
    }

    [Fact]
    public void A_full_sheet_shows_every_part_of_the_Fuehrungsorganisation()
    {
        var session = NewSession();
        session.UpdateLeadershipPost("Feuerwehrhaus Emmering", "Florian Emmering 1", "08141 12345", "TMO FW_EMMERING 1000");
        session.AssignRole("Einsatzleiter", "Max Mustermann", "Florian Emmering 1", slot: RoleSlot.Leiter);
        session.AssignRole("Führungsassistent", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);
        var bereitstellung = AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz");
        session.AddCommandNode(
            CommandNodeKind.Abschnitt,
            "Abschnitt 1 Nord",
            auftrag: "Brandbekämpfung Wohnhaus",
            standort: "Hauptstraße 12",
            funkgruppe: "DMO 301_F",
            rufname: "Florian Emmering 11",
            telefon: "0170 1234567");
        var abschnitt = session.Incident.CommandNodes[^1].Id;
        session.AssignRole("Abschnittsleiter", "Hans Huber", "Florian Aich 1", nodeId: abschnitt, slot: RoleSlot.Leiter);
        session.AssignRole("Melder", "Lena Lehner", nodeId: abschnitt, slot: RoleSlot.Melder);
        session.AssignRole("Sicherheitsbeauftragter", "Peter Maier", phone: "0170 7654321", nodeId: abschnitt);
        AddUnit(session, "FF Emmering", "Florian Emmering 40/1", abschnitt);
        AddUnit(session, "FF Aich", null, abschnitt);
        var dach = AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", abschnitt);
        var rd = AddNode(session, CommandNodeKind.Partner, "Rettungsdienst");
        session.AssignRole("Einsatzleiter RD", "Sabine Schmid", nodeId: rd, slot: RoleSlot.Leiter);
        session.AssignRole("Fachberater THW", "Thomas Weber", "Heros Fürstenfeldbruck 10/1", slot: RoleSlot.Fachberater);
        AddUnit(session, "FF Steinhöring", "Florian Steinhöring 40/1");
        session.AssignRole("Abschnittsleiter", "Josef Bauer", section: "Süd", slot: RoleSlot.Leiter);
        session.SetFuehrungsstufe(Fuehrungsstufe.B);

        using var vm = NewVm(session);

        // Einsatzleitung
        Assert.Equal("Feuerwehrhaus Emmering", vm.El.Standort);
        Assert.Equal("Florian Emmering 1", vm.El.Rufname);
        Assert.Equal("08141 12345", vm.El.Telefon);
        Assert.Equal("TMO FW_EMMERING 1000", vm.El.Funkgruppe);
        Assert.NotNull(vm.El.Leiter);
        Assert.Equal("Einsatzleiter", vm.El.Leiter.Role);
        Assert.Equal("Max Mustermann · Florian Emmering 1", vm.El.Leiter.Text);
        var assistant = Assert.Single(vm.El.Assistants);
        Assert.Equal("Erika Musterfrau", assistant.Text);
        Assert.Equal("Stufe B", vm.El.StufeText);
        Assert.Equal("Vorschlag: Stufe D – Unterabschnitte gebildet", vm.El.SuggestionText);
        Assert.False(vm.El.MatchesSuggestion);

        // Sections in ReportingLine order, the Unterabschnitt nested
        Assert.Equal(new[] { bereitstellung, abschnitt }, vm.Sections.Select(c => c.Id));
        var staging = vm.Sections[0];
        Assert.Equal(CommandNodeKind.Bereitstellung, staging.Kind);
        Assert.Equal("Bereitstellung", staging.KindLabel);
        Assert.Equal("Bereitstellung Marktplatz", staging.Name);
        Assert.Empty(staging.Units);
        Assert.Empty(staging.Children);

        var card = vm.Sections[1];
        Assert.Equal("Abschnitt", card.KindLabel);
        Assert.Equal("Abschnitt 1 Nord", card.Name);
        Assert.Equal("Brandbekämpfung Wohnhaus", card.Auftrag);
        Assert.Equal("DMO 301_F", card.Funkgruppe);
        Assert.Equal("Hauptstraße 12", card.Standort);
        Assert.Equal("Florian Emmering 11", card.Rufname);
        Assert.Equal("0170 1234567", card.Telefon);
        Assert.NotNull(card.Leiter);
        Assert.Equal("Hans Huber · Florian Aich 1", card.Leiter.Text);
        Assert.NotNull(card.Melder);
        Assert.Equal("Lena Lehner", card.Melder.Text);
        var member = Assert.Single(card.Members);
        Assert.Equal("Sicherheitsbeauftragter", member.Role);
        Assert.Equal("Peter Maier", member.PersonName);
        Assert.Equal("0170 7654321", member.Phone);
        Assert.Null(member.CallSign);
        Assert.Equal(new[] { "Florian Emmering 40/1", "FF Aich" }, card.Units.Select(u => u.Title));
        var unit = card.Units[0];
        Assert.Equal("FF Emmering", unit.Brigade);
        Assert.Equal("Florian Emmering 40/1", unit.CallSign);
        Assert.Equal("0/1/8/9", unit.StrengthText);
        Assert.Equal(4, unit.ScbaCount);
        Assert.Equal("Abschnitt 1 Nord", unit.NodeLabel);
        Assert.Equal(abschnitt, Assert.Single(unit.NodeChoices, c => c.IsCurrent).NodeId);
        Assert.Null(card.Hint);

        var sub = Assert.Single(card.Children);
        Assert.Equal(dach, sub.Id);
        Assert.Equal("Unterabschnitt", sub.KindLabel);
        Assert.Equal("Dach", sub.Name);

        // Partner, Fachberater, unplaced, free text
        Assert.True(vm.HasPartners);
        var partner = Assert.Single(vm.Partners);
        Assert.Equal("Rettungsdienst", partner.Name);
        Assert.Equal("Einsatzleitung", partner.KindLabel);
        Assert.Equal("Sabine Schmid", partner.Leiter?.PersonName);

        Assert.True(vm.HasFachberater);
        var fachberater = Assert.Single(vm.Fachberater);
        Assert.Equal("Fachberater THW", fachberater.Role);
        Assert.Equal("Thomas Weber · Heros Fürstenfeldbruck 10/1", fachberater.Text);

        Assert.True(vm.HasUnplacedUnits);
        var unplaced = Assert.Single(vm.UnplacedUnits);
        Assert.Equal("Florian Steinhöring 40/1", unplaced.Title);
        Assert.Equal("—", unplaced.NodeLabel);

        Assert.True(vm.HasFreeTextSections);
        var freeText = Assert.Single(vm.FreeTextSections);
        Assert.Equal("Süd", freeText.Section);
        Assert.Equal("Abschnitt (Freitext): Süd", freeText.Title);
        Assert.Equal("Josef Bauer", Assert.Single(freeText.People).PersonName);

        Assert.Empty(vm.Hints);
    }

    [Fact]
    public void A_set_Stufe_that_matches_the_suggestion_says_so()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        session.SetFuehrungsstufe(Fuehrungsstufe.B);

        using var vm = NewVm(session);

        Assert.Equal("Vorschlag: Stufe B – 1 Abschnitt, keine Einheiten", vm.El.SuggestionText);
        Assert.True(vm.El.MatchesSuggestion);
    }

    [Fact]
    public void A_Leiter_with_a_free_text_Section_is_not_the_Einsatzleiter()
    {
        var session = NewSession();
        session.AssignRole("Abschnittsleiter", "Josef Bauer", section: "Abschnitt Süd", slot: RoleSlot.Leiter);

        using var vm = NewVm(session);

        Assert.Null(vm.El.Leiter);
        var section = Assert.Single(vm.FreeTextSections);
        Assert.Equal("Abschnitt (Freitext): Abschnitt Süd", section.Title);
        Assert.Equal("Josef Bauer", Assert.Single(section.People).PersonName);
    }

    [Fact]
    public void A_unit_in_a_Partner_or_a_missing_node_is_unplaced()
    {
        var partner = Node(CommandNodeKind.Partner, "Polizei");
        var session = Hostile(
            [partner],
            forces: [Unit("FF Emmering", partner.Id), Unit("FF Aich", Guid.NewGuid())]);

        using var vm = NewVm(session);

        Assert.Equal(new[] { "FF Emmering", "FF Aich" }, vm.UnplacedUnits.Select(u => u.Title));
        Assert.Empty(Assert.Single(vm.Partners).Units);
        Assert.All(vm.UnplacedUnits, u => Assert.Null(Assert.Single(u.NodeChoices, c => c.IsCurrent).NodeId));
    }

    [Fact]
    public void An_orphaned_Unterabschnitt_stands_as_a_top_level_section()
    {
        var parent = Node(CommandNodeKind.Abschnitt, "Abschnitt 1 Nord", dissolvedAt: T0);
        var sub = Node(CommandNodeKind.Unterabschnitt, "Dach", parent.Id);
        var session = Hostile([parent, sub], forces: [Unit("FF Aich", sub.Id)]);

        using var vm = NewVm(session);

        var card = Assert.Single(vm.Sections);
        Assert.Equal(sub.Id, card.Id);
        Assert.Equal("Unterabschnitt", card.KindLabel);
        Assert.Equal("FF Aich", Assert.Single(card.Units).Title);
        Assert.Empty(vm.UnplacedUnits);
    }

    [Fact]
    public void An_empty_node_can_be_dissolved()
    {
        var session = NewSession();
        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");

        using var vm = NewVm(session);

        Assert.True(vm.Sections[0].CanDissolve);
        Assert.Null(vm.Sections[0].DissolveBlockedReason);
    }

    [Fact]
    public void A_node_with_units_names_them_as_the_reason_it_cannot_be_dissolved()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddUnit(session, "FF Emmering", "Florian Emmering 40/1", abschnitt);
        session.AssignRole("Abschnittsleiter", "Hans Huber", nodeId: abschnitt, slot: RoleSlot.Leiter);
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", abschnitt);

        using var vm = NewVm(session);

        Assert.False(vm.Sections[0].CanDissolve);
        Assert.Equal("Erst die Einheiten verschieben.", vm.Sections[0].DissolveBlockedReason);
    }

    [Fact]
    public void A_node_with_a_running_Funktion_cannot_be_dissolved()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        session.AssignRole("Abschnittsleiter", "Hans Huber", nodeId: abschnitt, slot: RoleSlot.Leiter);
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", abschnitt);

        using var vm = NewVm(session);

        Assert.False(vm.Sections[0].CanDissolve);
        Assert.Equal("Erst die Funktionen beenden.", vm.Sections[0].DissolveBlockedReason);
    }

    [Fact]
    public void An_Abschnitt_with_a_live_Unterabschnitt_cannot_be_dissolved()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        AddNode(session, CommandNodeKind.Unterabschnitt, "Dach", abschnitt);

        using var vm = NewVm(session);

        Assert.False(vm.Sections[0].CanDissolve);
        Assert.Equal("Erst die Unterabschnitte auflösen.", vm.Sections[0].DissolveBlockedReason);
        Assert.True(vm.Sections[0].Children[0].CanDissolve);
    }

    [Fact]
    public void Nothing_can_be_dissolved_on_a_read_only_incident()
    {
        var store = new FakeStore();
        var seed = NewSession(store);
        AddNode(seed, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var sued = AddNode(seed, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        AddUnit(seed, "FF Aich", "Florian Aich 42/1", sued);
        AddNode(seed, CommandNodeKind.Partner, "Rettungsdienst");
        seed.Close();

        using var vm = NewVm(LocalIncidentSession.OpenReadOnly(store, Clock, IncidentPath));

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.Sections[0].CanDissolve);
        Assert.False(vm.Partners[0].CanDissolve);
        Assert.False(vm.Sections[1].CanDissolve);
        Assert.Null(vm.Sections[1].DissolveBlockedReason); // nothing to do about it on a closed Einsatz
    }

    [Fact]
    public void Six_Abschnitte_raise_the_FwDV_span_hint_for_the_Einsatzleitung()
    {
        var session = NewSession();
        for (var i = 1; i <= 5; i++)
        {
            AddNode(session, CommandNodeKind.Abschnitt, $"Abschnitt {i}");
        }

        using var vm = NewVm(session);
        Assert.Empty(vm.Hints);

        AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 6");

        Assert.Equal(new[] { "FwDV 100: höchstens 5 Einsatzabschnitte" }, vm.Hints);
        Assert.All(vm.Sections, c => Assert.Null(c.Hint));
    }

    [Fact]
    public void Six_units_in_one_Abschnitt_put_the_span_hint_on_its_card()
    {
        var session = NewSession();
        var abschnitt = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var other = AddNode(session, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        for (var i = 1; i <= 6; i++)
        {
            AddUnit(session, "FF Emmering", $"Florian Emmering 40/{i}", abschnitt);
        }

        using var vm = NewVm(session);

        Assert.Equal("Mehr als 5 Einheiten – Unterabschnitt bilden?", vm.Sections.Single(c => c.Id == abschnitt).Hint);
        Assert.Null(vm.Sections.Single(c => c.Id == other).Hint);
        Assert.Empty(vm.Hints);
    }

    [Fact]
    public void IsNarrow_is_settable_and_notifies()
    {
        using var vm = NewVm(NewSession());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsNarrow = true;

        Assert.True(vm.IsNarrow);
        Assert.Contains(nameof(LeadershipViewModel.IsNarrow), raised);
    }
}
