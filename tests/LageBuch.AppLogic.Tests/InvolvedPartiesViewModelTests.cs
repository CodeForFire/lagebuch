using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Involved;

namespace LageBuch.AppLogic.Tests;

public class InvolvedPartiesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession(FakeStore? store = null) =>
        TestSession.StartNew(
            store ?? new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    // #702: an Einsatz without CO-Messung keeps the card as it was.
    [Fact]
    public void Without_a_haus_in_the_co_messung_the_card_offers_no_wohnung()
    {
        var session = NewSession();
        session.AddInvolvedParty("Erika Beispiel", null, null);

        var row = Assert.Single(new InvolvedPartiesViewModel(session, () => { }).Parties);

        Assert.False(row.HasDwellingPicker);
        Assert.Equal(DwellingChoice.NoneDisplay, row.DwellingButtonText);
    }

    [Fact]
    public void The_wohnung_picker_lists_every_wohnung_top_down_and_records_the_pick()
    {
        var session = NewSession();
        session.AddCoBuilding("Hauptstr. 12", 1, 2);
        session.AddInvolvedParty("Erika Beispiel", null, null);
        var changes = 0;
        var vm = new InvolvedPartiesViewModel(session, () => changes++);
        var row = Assert.Single(vm.Parties);

        Assert.True(row.HasDwellingPicker);
        Assert.Equal(
            [DwellingChoice.NoneDisplay, "Hauptstr. 12, 1. OG, Whg. 1", "Hauptstr. 12, 1. OG, Whg. 2", "Hauptstr. 12, EG, Whg. 1", "Hauptstr. 12, EG, Whg. 2"],
            row.DwellingChoices.Select(c => c.Display));
        Assert.True(row.DwellingChoices[0].IsCurrent);

        row.DwellingChoices.Single(c => c.Display == "Hauptstr. 12, 1. OG, Whg. 2").SelectCommand.Execute(null);

        var linked = Assert.Single(vm.Parties);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", linked.DwellingButtonText);
        Assert.True(linked.DwellingChoices.Single(c => c.Display == "Hauptstr. 12, 1. OG, Whg. 2").IsCurrent);
        Assert.False(linked.DwellingChoices[0].IsCurrent);
        Assert.Equal(1, changes);

        linked.DwellingChoices[0].SelectCommand.Execute(null);

        Assert.Null(session.Incident.InvolvedParties.Single().DwellingId);
        Assert.Equal(2, changes);
    }

    // Once the Haus is gone the picker has nothing to offer, but the card still says where the
    // person was found, from the copy the link took.
    [Fact]
    public void A_link_to_a_removed_wohnung_still_shows_where()
    {
        var session = NewSession();
        session.AddCoBuilding("Hauptstr. 12", 1, 2);
        session.AddInvolvedParty("Erika Beispiel", null, null);
        var dwelling = session.Incident.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2);
        session.SetInvolvedPartyDwelling(session.Incident.InvolvedParties[0].Id, dwelling.Id);
        session.RemoveCoBuilding(dwelling.BuildingId);

        var row = Assert.Single(new InvolvedPartiesViewModel(session, () => { }).Parties);

        Assert.True(row.HasDwellingPicker);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", row.DwellingButtonText);
        Assert.Equal([DwellingChoice.NoneDisplay], row.DwellingChoices.Select(c => c.Display));
    }

    [Fact]
    public void Add_records_the_entry_clears_the_form_and_writes_no_etb_line()
    {
        var session = NewSession();
        var journalBefore = session.Incident.Journal.Count;
        var changes = 0;
        var vm = new InvolvedPartiesViewModel(session, () => changes++)
        {
            NewName = "Erika Beispiel",
            NewPhone = "0171 0000001",
            NewNotes = "Hauseigentümerin",
        };

        vm.AddCommand.Execute(null);

        var row = Assert.Single(vm.Parties);
        Assert.Equal("Erika Beispiel", row.Name);
        Assert.Equal("0171 0000001", row.Phone);
        Assert.Equal("Hauseigentümerin", row.Notes);
        Assert.Equal("Muster (FFB 12/1)", row.CreatedBy);
        Assert.Equal(string.Empty, vm.NewName);
        Assert.Null(vm.NewPhone);
        Assert.Null(vm.NewNotes);
        Assert.Equal(1, changes);
        Assert.Equal(journalBefore, session.Incident.Journal.Count);
    }

    [Fact]
    public void Add_without_a_name_says_so_instead_of_going_grey()
    {
        var session = NewSession();
        var vm = new InvolvedPartiesViewModel(session, () => { }) { NewPhone = "110" };

        Assert.Null(vm.NewNameError); // quiet until asked
        Assert.True(vm.AddCommand.CanExecute(null)); // the press is the question (#412)
        vm.AddCommand.Execute(null);

        Assert.Equal(ValidationMessages.NameRequired, vm.NewNameError);
        Assert.Empty(session.Incident.InvolvedParties);
        Assert.Equal("110", vm.NewPhone); // the form stays as the Lagebuchführer left it

        vm.NewName = "Erika Beispiel";
        Assert.Null(vm.NewNameError); // fixed as it is typed, without a second press
    }

    [Fact]
    public void Add_with_an_oversized_name_is_explained_and_not_sent()
    {
        var session = NewSession();
        var vm = new InvolvedPartiesViewModel(session, () => { })
        {
            NewName = new string('A', InvolvedParty.MaxNameLength + 1),
        };

        vm.AddCommand.Execute(null);

        Assert.NotNull(vm.NewNameError);
        Assert.Contains("zu lang", vm.NewNameError, StringComparison.Ordinal);
        Assert.Empty(session.Incident.InvolvedParties);
    }

    [Fact]
    public void Editing_a_row_writes_through_to_the_session()
    {
        var session = NewSession();
        var vm = new InvolvedPartiesViewModel(session, () => { }) { NewName = "Erika Beispiel" };
        vm.AddCommand.Execute(null);

        vm.Parties[0].Phone = "0171 0000001";

        var party = Assert.Single(session.Incident.InvolvedParties);
        Assert.Equal("0171 0000001", party.Phone);
        Assert.Equal("0171 0000001", Assert.Single(vm.Parties).Phone);
    }

    [Fact]
    public void Blanking_a_rows_name_is_refused_with_a_message_and_keeps_the_stored_name()
    {
        var session = NewSession();
        var vm = new InvolvedPartiesViewModel(session, () => { }) { NewName = "Erika Beispiel" };
        vm.AddCommand.Execute(null);
        var row = vm.Parties[0];

        row.Name = "   ";

        Assert.Equal(ValidationMessages.NameRequired, row.Error);
        Assert.Equal("Erika Beispiel", Assert.Single(session.Incident.InvolvedParties).Name);
    }

    [Fact]
    public void Removing_a_row_asks_for_confirmation_before_touching_the_session()
    {
        var session = NewSession();
        string? confirmTitle = null;
        string? confirmMessage = null;
        Action? confirmAction = null;
        var vm = new InvolvedPartiesViewModel(session, () => { }, (title, message, onConfirmed) =>
        {
            confirmTitle = title;
            confirmMessage = message;
            confirmAction = onConfirmed;
        })
        {
            NewName = "Erika Beispiel",
        };
        vm.AddCommand.Execute(null);

        vm.Parties[0].RemoveCommand.Execute(null);

        Assert.Single(vm.Parties); // nothing happened yet — the host only recorded the request
        Assert.Equal("Beteiligten entfernen?", confirmTitle);
        Assert.Equal("„Erika Beispiel“ aus den Beteiligten entfernen. Fortfahren?", confirmMessage);

        confirmAction!();

        Assert.Empty(vm.Parties);
        Assert.Empty(session.Incident.InvolvedParties);
    }

    [Fact]
    public void A_change_made_elsewhere_rebuilds_the_list()
    {
        var session = NewSession();
        using var vm = new InvolvedPartiesViewModel(session, () => { });

        session.AddInvolvedParty("POK Mustermann", null, "Polizei vor Ort");

        Assert.Equal("POK Mustermann", Assert.Single(vm.Parties).Name);
    }

    [Fact]
    public void A_readonly_incident_shows_its_entries_but_offers_no_way_to_change_them()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = NewSession(store);
        seed.AddInvolvedParty("Erika Beispiel", "0171 0000001", null);
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new InvolvedPartiesViewModel(ro, () => { });

        var row = Assert.Single(vm.Parties);
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.False(vm.ShowComposer);
        Assert.False(row.RemoveCommand.CanExecute(null));
        row.RemoveCommand.Execute(null); // inert, not throwing
        row.Notes = "nachträglich"; // inert, not throwing
        Assert.Null(Assert.Single(ro.Incident.InvolvedParties).Notes);
    }

    [Fact]
    public void On_a_phone_the_form_opens_on_demand_and_closes_after_adding()
    {
        var vm = new InvolvedPartiesViewModel(NewSession(), () => { }) { IsNarrow = true };

        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);

        vm.OpenComposerCommand.Execute(null);
        Assert.True(vm.ShowComposer);
        Assert.False(vm.ShowComposerButton);

        vm.NewName = "Erika Beispiel";
        vm.AddCommand.Execute(null);

        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);
    }

    [Fact]
    public void Closing_the_phone_form_drops_its_complaint()
    {
        var vm = new InvolvedPartiesViewModel(NewSession(), () => { }) { IsNarrow = true };
        vm.OpenComposerCommand.Execute(null);
        vm.AddCommand.Execute(null);
        Assert.NotNull(vm.NewNameError);

        vm.CloseComposerCommand.Execute(null);

        Assert.Null(vm.NewNameError);
    }

    // #540: the view refocuses on EntrySubmitted -- NAME after an add and after a refusal alike.
    [Fact]
    public void Submitting_reports_whether_the_party_was_added()
    {
        using var vm = new InvolvedPartiesViewModel(NewSession(), () => { });
        var outcomes = new List<bool>();
        vm.EntrySubmitted += (_, e) => outcomes.Add(e.Added);

        vm.AddCommand.Execute(null); // no name: refused
        vm.NewName = "Erika Beispiel";
        vm.AddCommand.Execute(null);

        Assert.Equal(new[] { false, true }, outcomes);
    }

    [Fact]
    public void Dispose_stops_listening_to_the_session()
    {
        var session = NewSession();
        var vm = new InvolvedPartiesViewModel(session, () => { });
        vm.Dispose();

        session.AddInvolvedParty("POK Mustermann", null, null);

        Assert.Empty(vm.Parties);
    }
}
