using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Equipment;

namespace LageBuch.AppLogic.Tests;

public class IssuedItemsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static readonly string[] Devices = ["Mobilteil 1", "AED", "Weste EAL"];

    private static LocalIncidentSession NewSession(FakeStore? store = null, FixedClock? clock = null) =>
        TestSession.StartNew(
            store ?? new FakeStore(),
            clock ?? new FixedClock(T0),
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    [Fact]
    public void Add_records_the_ausgabe_clears_the_form_and_writes_no_etb_line()
    {
        var session = NewSession();
        var journalBefore = session.Incident.Journal.Count;
        var changes = 0;
        var vm = new IssuedItemsViewModel(session, () => changes++, Devices)
        {
            NewDevice = "Mobilteil 1",
            NewIssuedTo = "Erika Beispiel",
            NewIssuedToPhone = "0171 0000001",
            NewIssuedToDetails = "THW OV Beispielstadt",
            NewRemark = "mit Ladekabel",
        };

        vm.AddCommand.Execute(null);

        var row = Assert.Single(vm.Items);
        Assert.Equal("1", row.Number);
        Assert.Equal("Mobilteil 1", row.Device);
        Assert.Equal("Erika Beispiel", row.IssuedTo);
        Assert.Equal("0171 0000001", row.IssuedToPhone);
        Assert.Equal("THW OV Beispielstadt", row.IssuedToDetails);
        Assert.Equal("mit Ladekabel", row.Remark);
        Assert.True(row.IsOut);
        Assert.Equal("Ausgabe 14:00", row.IssuedDisplay);
        Assert.Null(row.ReturnedDisplay);
        Assert.Null(vm.NewDevice);
        Assert.Null(vm.NewIssuedTo);
        Assert.Null(vm.NewIssuedToPhone);
        Assert.Null(vm.NewIssuedToDetails);
        Assert.Null(vm.NewRemark);
        Assert.Equal(1, changes);
        Assert.Equal(journalBefore, session.Incident.Journal.Count);
    }

    // A typed Gerät that matches the Stammdaten apart from case and spacing records in their spelling;
    // anything the list lacks is kept as typed (#337).
    [Theory]
    [InlineData("  mobilteil 1 ", "Mobilteil 1")]
    [InlineData("Wärmebildkamera", "Wärmebildkamera")]
    public void A_typed_geraet_adopts_the_stammdaten_spelling_or_stays_as_typed(string typed, string recorded)
    {
        var session = NewSession();
        var vm = new IssuedItemsViewModel(session, () => { }, Devices) { NewDevice = typed, NewIssuedTo = "Max Beispiel" };

        vm.AddCommand.Execute(null);

        Assert.Equal(recorded, Assert.Single(session.Incident.IssuedItems).Device);
    }

    [Fact]
    public void Add_without_a_geraet_or_a_recipient_says_so_and_marks_the_field()
    {
        var session = NewSession();
        var vm = new IssuedItemsViewModel(session, () => { }, Devices) { NewIssuedToPhone = "0171 0000001" };

        Assert.Null(vm.NewItemError); // quiet until asked
        Assert.True(vm.AddCommand.CanExecute(null)); // the press is the question (#412)
        vm.AddCommand.Execute(null);

        Assert.Equal(ValidationMessages.DeviceRequired, vm.NewItemError);
        Assert.True(vm.IsNewDeviceMissing);
        Assert.True(vm.IsNewIssuedToMissing);
        Assert.Empty(session.Incident.IssuedItems);
        Assert.Equal("0171 0000001", vm.NewIssuedToPhone); // the form stays as the Lagebuchführer left it

        vm.NewDevice = "AED";
        Assert.Equal(ValidationMessages.IssuedToRequired, vm.NewItemError);
        Assert.False(vm.IsNewDeviceMissing);

        vm.NewIssuedTo = "Max Beispiel";
        Assert.Null(vm.NewItemError); // fixed as it is typed, without a second press
        Assert.False(vm.IsNewIssuedToMissing);
    }

    [Theory]
    [InlineData(nameof(IssuedItem.IssuedToPhone), IssuedItem.MaxPhoneLength, "Telefon")]
    [InlineData(nameof(IssuedItem.IssuedToDetails), IssuedItem.MaxDetailsLength, "Organisation / Kontakt")]
    [InlineData(nameof(IssuedItem.Remark), IssuedItem.MaxRemarkLength, "Bemerkung")]
    public void An_oversized_optional_field_is_explained_and_not_sent(string field, int max, string label)
    {
        var session = NewSession();
        var oversized = new string('1', max + 1);
        var vm = new IssuedItemsViewModel(session, () => { }, Devices)
        {
            NewDevice = "AED",
            NewIssuedTo = "Max Beispiel",
            NewIssuedToPhone = field == nameof(IssuedItem.IssuedToPhone) ? oversized : null,
            NewIssuedToDetails = field == nameof(IssuedItem.IssuedToDetails) ? oversized : null,
            NewRemark = field == nameof(IssuedItem.Remark) ? oversized : null,
        };

        vm.AddCommand.Execute(null);

        Assert.Equal($"{label} ist zu lang (höchstens {max} Zeichen)", vm.NewItemError);
        Assert.Empty(session.Incident.IssuedItems);
    }

    [Fact]
    public void Zurueck_records_the_return_and_moves_the_row_below_what_is_still_out()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock: clock);
        session.IssueItem("Mobilteil 1", "Erika Beispiel", null, null, null);
        session.IssueItem("AED", "Max Beispiel", null, null, null);
        var changes = 0;
        var vm = new IssuedItemsViewModel(session, () => changes++, Devices);
        Assert.Equal("2 Geräte ausgegeben", vm.Summary);
        clock.Now = T0.AddMinutes(45);

        vm.Items[0].ReturnCommand.Execute(null);

        Assert.Equal(["AED", "Mobilteil 1"], vm.Items.Select(r => r.Device));
        Assert.Equal(["2", "1"], vm.Items.Select(r => r.Number)); // the number stays the issue order
        var returned = vm.Items[1];
        Assert.False(returned.IsOut);
        Assert.Equal("Zurück 14:45", returned.ReturnedDisplay);
        Assert.False(returned.ReturnCommand.CanExecute(null));
        Assert.True(returned.UndoReturnCommand.CanExecute(null));
        Assert.Equal("Muster (FFB 12/1)", session.Incident.IssuedItems[0].ReturnedBy);
        Assert.Equal("1 Gerät ausgegeben", vm.Summary);
        Assert.True(vm.HasItemsOut);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Undoing_a_return_puts_the_geraet_back_out()
    {
        var session = NewSession();
        session.IssueItem("AED", "Max Beispiel", null, null, null);
        session.ReturnIssuedItem(session.Incident.IssuedItems[0].Id);
        var vm = new IssuedItemsViewModel(session, () => { }, Devices);
        Assert.Equal("Alle Geräte zurück", vm.Summary);
        Assert.False(vm.HasItemsOut);

        vm.Items[0].UndoReturnCommand.Execute(null);

        Assert.True(Assert.Single(vm.Items).IsOut);
        Assert.True(Assert.Single(session.Incident.IssuedItems).IsOut);
        Assert.Equal("1 Gerät ausgegeben", vm.Summary);
    }

    [Fact]
    public void An_empty_list_has_no_summary()
    {
        Assert.Null(new IssuedItemsViewModel(NewSession(), () => { }, Devices).Summary);
    }

    [Fact]
    public void Editing_a_row_writes_through_to_the_session()
    {
        var session = NewSession();
        session.IssueItem("AED", "Max Beispiel", null, null, null);
        var vm = new IssuedItemsViewModel(session, () => { }, Devices);

        vm.Items[0].IssuedToPhone = "0171 0000001";

        Assert.Equal("0171 0000001", Assert.Single(session.Incident.IssuedItems).IssuedToPhone);
        Assert.Equal("0171 0000001", Assert.Single(vm.Items).IssuedToPhone);
    }

    [Fact]
    public void Blanking_a_rows_recipient_is_refused_with_a_message_and_keeps_the_stored_one()
    {
        var session = NewSession();
        session.IssueItem("AED", "Max Beispiel", null, null, null);
        var vm = new IssuedItemsViewModel(session, () => { }, Devices);
        var row = vm.Items[0];

        row.IssuedTo = "   ";

        Assert.Equal(ValidationMessages.IssuedToRequired, row.Error);
        Assert.Equal("Max Beispiel", Assert.Single(session.Incident.IssuedItems).IssuedTo);
    }

    [Fact]
    public void Removing_a_row_asks_for_confirmation_before_touching_the_session()
    {
        var session = NewSession();
        session.IssueItem("AED", "Max Beispiel", null, null, null);
        string? confirmTitle = null;
        string? confirmMessage = null;
        Action? confirmAction = null;
        var vm = new IssuedItemsViewModel(session, () => { }, Devices, (title, message, onConfirmed) =>
        {
            confirmTitle = title;
            confirmMessage = message;
            confirmAction = onConfirmed;
        });

        vm.Items[0].RemoveCommand.Execute(null);

        Assert.Single(vm.Items); // nothing happened yet — the host only recorded the request
        Assert.Equal("Ausgabe entfernen?", confirmTitle);
        Assert.Equal("„AED“ an „Max Beispiel“ aus der Ausgabeliste entfernen. Fortfahren?", confirmMessage);

        confirmAction!();

        Assert.Empty(vm.Items);
        Assert.Empty(session.Incident.IssuedItems);
    }

    [Fact]
    public void A_change_made_elsewhere_rebuilds_the_list()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices);

        session.IssueItem("Weste EAL", "Erika Beispiel", null, null, null);

        Assert.Equal("Weste EAL", Assert.Single(vm.Items).Device);
        Assert.Equal("1 Gerät ausgegeben", vm.Summary);
    }

    [Fact]
    public void A_readonly_incident_shows_its_entries_but_offers_no_way_to_change_them()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = NewSession(store);
        seed.IssueItem("AED", "Max Beispiel", null, null, null);
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");
        var vm = new IssuedItemsViewModel(ro, () => { }, Devices);

        var row = Assert.Single(vm.Items);
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.False(vm.ShowComposer);
        Assert.False(row.ReturnCommand.CanExecute(null));
        Assert.False(row.RemoveCommand.CanExecute(null));
        row.ReturnCommand.Execute(null); // inert, not throwing
        row.RemoveCommand.Execute(null); // inert, not throwing
        row.Remark = "nachträglich"; // inert, not throwing
        Assert.True(Assert.Single(ro.Incident.IssuedItems).IsOut);
        Assert.Null(ro.Incident.IssuedItems[0].Remark);
    }

    [Fact]
    public void On_a_phone_the_form_opens_on_demand_and_closes_after_adding()
    {
        var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices) { IsNarrow = true };

        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);

        vm.OpenComposerCommand.Execute(null);
        Assert.True(vm.ShowComposer);

        vm.NewDevice = "AED";
        vm.NewIssuedTo = "Max Beispiel";
        vm.AddCommand.Execute(null);

        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);
    }

    [Fact]
    public void Closing_the_phone_form_drops_its_complaint()
    {
        var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices) { IsNarrow = true };
        vm.OpenComposerCommand.Execute(null);
        vm.AddCommand.Execute(null);
        Assert.NotNull(vm.NewItemError);

        vm.CloseComposerCommand.Execute(null);

        Assert.Null(vm.NewItemError);
        Assert.False(vm.IsNewDeviceMissing);
    }

    // #540: the view refocuses on EntrySubmitted -- GERÄT after an add and after a refusal alike.
    [Fact]
    public void Submitting_reports_whether_the_ausgabe_was_recorded()
    {
        using var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices);
        var outcomes = new List<bool>();
        vm.EntrySubmitted += (_, e) => outcomes.Add(e.Added);

        vm.AddCommand.Execute(null); // nothing typed: refused
        vm.NewDevice = "AED";
        vm.NewIssuedTo = "Max Beispiel";
        vm.AddCommand.Execute(null);

        Assert.Equal(new[] { false, true }, outcomes);
    }

    [Fact]
    public void Dispose_stops_listening_to_the_session()
    {
        var session = NewSession();
        var vm = new IssuedItemsViewModel(session, () => { }, Devices);
        vm.Dispose();

        session.IssueItem("AED", "Max Beispiel", null, null, null);

        Assert.Empty(vm.Items);
    }
}
