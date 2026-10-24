using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// The "Einsatzdaten" overlay: Stichwort, Einsatznummer, Straße and Ortsteil edited together and
// buffered until SPEICHERN. Replaces the inline Einsatznummer editor (#69) and gives the address
// its first UI -- until now the PDF's "Adresse" line was always empty.
public class IncidentDataDialogViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession(FakeStore store, string? keyword = null, IncidentNumber? number = null)
    {
        return TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>(),
            number,
            keyword: keyword);
    }

    [Fact]
    public void Fields_are_seeded_from_the_incident()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 1.2 260715 123"));
        session.SetAddress("Hauptstr. 12", "FFB");

        var vm = new IncidentDataDialogViewModel(session, () => { });

        Assert.Equal("B3P", vm.Keyword);
        Assert.Equal("B 1.2 260715 123", vm.IncidentNumber);
        Assert.Equal("Hauptstr. 12", vm.Street);
        Assert.Equal("FFB", vm.District);
    }

    [Fact]
    public void Save_writes_all_four_fields_and_persists_them()
    {
        var store = new FakeStore();
        var session = NewSession(store);
        var changed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => changed++);

        vm.Keyword = " B3P ";
        vm.IncidentNumber = " B 1.2 260715 123 ";
        vm.Street = "Hauptstr. 12";
        vm.District = "FFB";
        vm.SaveCommand.Execute(null);

        var saved = store.Load("/x.lagebuch");
        Assert.Equal("B3P", saved.Keyword);
        Assert.Equal("B 1.2 260715 123", saved.IncidentNumber!.Value);
        Assert.Equal("Hauptstr. 12", saved.Street);
        Assert.Equal("FFB", saved.District);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Save_writes_hausnummer_plz_and_ort_next_to_strasse_and_ortsteil()
    {
        var store = new FakeStore();
        var session = NewSession(store);
        var vm = new IncidentDataDialogViewModel(session, () => { });

        vm.Street = "Hauptstr.";
        vm.HouseNumber = " 12 ";
        vm.PostalCode = "82256";
        vm.Place = "Fürstenfeldbruck";
        vm.District = "Puch";
        vm.SaveCommand.Execute(null);

        var saved = store.Load("/x.lagebuch");
        Assert.Equal("12", saved.HouseNumber);
        Assert.Equal("82256", saved.PostalCode);
        Assert.Equal("Fürstenfeldbruck", saved.Place);
        Assert.Equal("Hauptstr. 12, Puch, 82256 Fürstenfeldbruck", Formatting.Address(saved));
    }

    [Fact]
    public void Changing_only_the_plz_counts_as_a_change_and_keeps_the_other_parts()
    {
        var store = new FakeStore();
        var session = NewSession(store);
        session.SetAddress("Hauptstr.", "Puch", houseNumber: "12", place: "Fürstenfeldbruck");
        var vm = new IncidentDataDialogViewModel(session, () => { });

        vm.PostalCode = "82256";

        Assert.True(vm.HasChanges);
        vm.SaveCommand.Execute(null);
        Assert.Equal("Hauptstr. 12, Puch, 82256 Fürstenfeldbruck", Formatting.Address(store.Load("/x.lagebuch")));
    }

    [Fact]
    public void Stichwort_suggestions_come_from_the_stammdaten()
    {
        var vm = new IncidentDataDialogViewModel(NewSession(new FakeStore()), () => { }, ["B3 Person", "THL 1"]);

        Assert.Equal(["B3 Person", "THL 1"], vm.KeywordOptions);
    }

    [Fact]
    public void Save_records_a_stichwort_in_the_stammdaten_spelling()
    {
        var store = new FakeStore();
        var vm = new IncidentDataDialogViewModel(NewSession(store), () => { }, ["B3 Person"]);

        vm.Keyword = " b3 person ";
        vm.SaveCommand.Execute(null);

        Assert.Equal("B3 Person", store.Load("/x.lagebuch").Keyword);
    }

    [Fact]
    public void Save_keeps_a_stichwort_the_stammdaten_do_not_list()
    {
        var store = new FakeStore();
        var vm = new IncidentDataDialogViewModel(NewSession(store), () => { }, ["B3 Person"]);

        vm.Keyword = "Ölspur";
        vm.SaveCommand.Execute(null);

        Assert.Equal("Ölspur", store.Load("/x.lagebuch").Keyword);
    }

    [Fact]
    public void Blank_fields_clear_the_values()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 99"));
        session.SetAddress("Hauptstr. 12", "FFB");
        var vm = new IncidentDataDialogViewModel(session, () => { });

        vm.Keyword = "   ";
        vm.IncidentNumber = string.Empty;
        vm.Street = null;
        vm.District = " ";
        vm.SaveCommand.Execute(null);

        var saved = store.Load("/x.lagebuch");
        Assert.Null(saved.Keyword);
        Assert.Null(saved.IncidentNumber);
        Assert.Null(saved.Street);
        Assert.Null(saved.District);
    }

    [Fact]
    public void Nothing_is_written_before_save()
    {
        var store = new FakeStore();
        var session = NewSession(store);
        var vm = new IncidentDataDialogViewModel(session, () => { });
        var before = store.SaveCount;

        vm.Keyword = "B3P";
        vm.Street = "Hauptstr. 12";

        Assert.Equal(before, store.SaveCount);
        Assert.Null(store.Load("/x.lagebuch").Keyword);
    }

    [Fact]
    public void Changing_only_the_einsatznummer_reports_a_change()
    {
        var changes = 0;
        var vm = new IncidentDataDialogViewModel(NewSession(new FakeStore()), () => changes++);

        vm.IncidentNumber = "B 1.2 260715 123";
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Changing_only_the_address_reports_a_change()
    {
        var changes = 0;
        var vm = new IncidentDataDialogViewModel(NewSession(new FakeStore()), () => changes++);

        vm.District = "Puch";
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, changes);
    }

    // #652: typed Einsatzdaten are not thrown away on a stray Esc; ABBRECHEN asks first.
    [Fact]
    public void Cancel_with_typed_fields_asks_before_closing()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P");
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;

        vm.Keyword = "B4";
        vm.CancelCommand.Execute(null);

        Assert.Equal(0, closed);
        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.Equal("B4", vm.Keyword);
        Assert.False(vm.SaveCommand.CanExecute(null)); // Enter in a field must not save past the question
    }

    [Fact]
    public void Confirming_the_discard_closes_without_saving()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P");
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;
        var before = store.SaveCount;
        vm.Keyword = "B4";
        vm.CancelCommand.Execute(null);

        vm.ConfirmDiscardCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.Equal(before, store.SaveCount);
        Assert.Equal("B3P", store.Load("/x.lagebuch").Keyword);
    }

    [Fact]
    public void Declining_the_discard_keeps_editing()
    {
        var session = NewSession(new FakeStore(), keyword: "B3P");
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;
        vm.Street = "Hauptstr. 12";
        vm.CancelCommand.Execute(null);

        vm.CancelDiscardCommand.Execute(null);

        Assert.Equal(0, closed);
        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.Equal("Hauptstr. 12", vm.Street);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Cancel_with_nothing_changed_closes_straight_away()
    {
        var session = NewSession(new FakeStore(), keyword: "B3P");
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;
        vm.Keyword = " B3P "; // the same once trimmed, as SPEICHERN would see it
        vm.District = string.Empty;

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.False(vm.IsDiscardConfirmOpen);
    }

    [Fact]
    public void Save_closes_the_dialog()
    {
        var session = NewSession(new FakeStore());
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, closed);
    }

    [Fact]
    public void Unchanged_fields_do_not_trigger_a_save()
    {
        // A no-op SPEICHERN must not fan out into three redundant saves / sync commands.
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 99"));
        session.SetAddress("Hauptstr. 12", "FFB");
        var changed = 0;
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => changed++);
        vm.Closed += (_, _) => closed++;
        var before = store.SaveCount;

        vm.SaveCommand.Execute(null);

        Assert.Equal(before, store.SaveCount);
        Assert.Equal(0, changed); // nothing saved, so the host's "gespeichert" line must not tick
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Only_the_changed_field_is_saved()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 99"));
        var vm = new IncidentDataDialogViewModel(session, () => { });
        var before = store.SaveCount;

        vm.Street = "Hauptstr. 12";
        vm.SaveCommand.Execute(null);

        Assert.Equal(before + 1, store.SaveCount);
        Assert.Equal("Hauptstr. 12", store.Load("/x.lagebuch").Street);
    }

    [Fact]
    public void Save_is_blocked_on_a_readonly_session()
    {
        var store = new FakeStore();
        NewSession(store);
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");

        var vm = new IncidentDataDialogViewModel(ro, () => { });

        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    // #461: the Stammdaten's Straßen and Ortsteile. The two Aicher Straßen agree on PLZ and Ort but
    // not on the Ortsteil -- the shape a street running through two Ortsteile takes.
    private static readonly Street[] Streets =
    [
        new("Hauptstraße", "Puch", "82256", "Fürstenfeldbruck"),
        new("Pucher Straße", string.Empty, "82256", "Fürstenfeldbruck"),
        new("Lindacher Straße", "Lindach", "82256", "Fürstenfeldbruck"),
        new("Aicher Straße", "Aich", "82256", "Fürstenfeldbruck"),
        new("Aicher Straße", "Puch", "82256", "Fürstenfeldbruck"),
    ];

    private static IncidentDataDialogViewModel WithStreets(LocalIncidentSession session) =>
        new(session, () => { }, districts: ["Puch", "Aich", "Pfaffing"], streets: Streets);

    [Fact]
    public void Street_and_district_suggestions_come_from_the_stammdaten_without_duplicates()
    {
        var vm = WithStreets(NewSession(new FakeStore()));

        Assert.Equal(
            new[] { "Hauptstraße", "Pucher Straße", "Lindacher Straße", "Aicher Straße" },
            vm.StreetOptions);

        // Lindach has a street but no entry of its own; it is offered all the same.
        Assert.Equal(new[] { "Puch", "Aich", "Pfaffing", "Lindach" }, vm.DistrictOptions);
    }

    [Fact]
    public void Picking_a_street_fills_its_ortsteil_plz_and_ort()
    {
        var vm = WithStreets(NewSession(new FakeStore()));

        vm.Street = "Hauptstraße";

        Assert.Equal("Puch", vm.District);
        Assert.Equal("82256", vm.PostalCode);
        Assert.Equal("Fürstenfeldbruck", vm.Place);
    }

    [Fact]
    public void A_typed_street_matches_apart_from_case_and_whitespace_and_saves_in_the_stammdaten_spelling()
    {
        var store = new FakeStore();
        var vm = WithStreets(NewSession(store));

        vm.Street = " hauptstraße ";
        vm.District = "puch";
        vm.SaveCommand.Execute(null);

        var saved = store.Load("/x.lagebuch");
        Assert.Equal("Hauptstraße", saved.Street);
        Assert.Equal("Puch", saved.District);
        Assert.Equal("82256", saved.PostalCode);
    }

    [Fact]
    public void A_field_the_rows_disagree_on_is_left_empty()
    {
        var vm = WithStreets(NewSession(new FakeStore()));

        vm.Street = "Aicher Straße";

        Assert.Null(vm.District); // Aich or Puch: guessing would be worse than asking
        Assert.Equal("82256", vm.PostalCode);
        Assert.Equal("Fürstenfeldbruck", vm.Place);
    }

    [Fact]
    public void A_typed_ortsteil_is_never_overwritten()
    {
        var vm = WithStreets(NewSession(new FakeStore()));
        vm.District = "Aich";

        vm.Street = "Hauptstraße";

        Assert.Equal("Aich", vm.District);
        Assert.Equal("82256", vm.PostalCode);
    }

    [Fact]
    public void The_address_already_in_the_incident_is_never_overwritten()
    {
        var session = NewSession(new FakeStore());
        session.SetAddress("Bahnhofstraße", "Aich", null, "82256", "Fürstenfeldbruck");
        var vm = WithStreets(session);

        vm.Street = "Lindacher Straße";

        Assert.Equal("Aich", vm.District);
    }

    [Fact]
    public void Picking_another_street_replaces_what_the_first_one_filled_in()
    {
        var vm = WithStreets(NewSession(new FakeStore()));
        vm.Street = "Hauptstraße";

        vm.Street = "Lindacher Straße";
        Assert.Equal("Lindach", vm.District);

        // Pucher Straße has no Ortsteil, so Lindach would now be wrong: it goes.
        vm.Street = "Pucher Straße";
        Assert.Null(vm.District);
        Assert.Equal("82256", vm.PostalCode);
    }

    [Fact]
    public void Typing_past_a_known_street_keeps_what_it_filled_in()
    {
        // "Hauptstraße 12" out of habit: no longer a match, but the Ortsteil was right.
        var vm = WithStreets(NewSession(new FakeStore()));
        vm.Street = "Hauptstraße";

        vm.Street = "Hauptstraße 12";

        Assert.Equal("Puch", vm.District);
    }

    [Fact]
    public void An_unknown_street_is_kept_as_typed_and_fills_nothing()
    {
        var store = new FakeStore();
        var vm = WithStreets(NewSession(store));

        vm.Street = "Feldweg am Weiher";
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.District);
        Assert.Null(vm.PostalCode);
        Assert.Equal("Feldweg am Weiher", store.Load("/x.lagebuch").Street);
    }
}
