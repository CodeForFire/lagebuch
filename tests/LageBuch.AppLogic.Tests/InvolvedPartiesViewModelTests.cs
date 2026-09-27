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
        string? confirmMessage = null;
        Action? confirmAction = null;
        var vm = new InvolvedPartiesViewModel(session, () => { }, (message, onConfirmed) =>
        {
            confirmMessage = message;
            confirmAction = onConfirmed;
        })
        {
            NewName = "Erika Beispiel",
        };
        vm.AddCommand.Execute(null);

        vm.Parties[0].RemoveCommand.Execute(null);

        Assert.Single(vm.Parties); // nothing happened yet — the host only recorded the request
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
