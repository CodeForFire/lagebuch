using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Equipment;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class IssuedItemsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static readonly Device[] Devices = [new("Mobilteil 1", 1), new("AED", 1), new("Weste EAL", 2), new("Sonstiges", 0)];

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

    [Fact]
    public void After_a_refused_press_a_successful_add_leaves_an_empty_form_without_complaint()
    {
        var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices);
        vm.AddCommand.Execute(null);
        Assert.NotNull(vm.NewItemError);

        vm.NewDevice = "AED";
        vm.NewIssuedTo = "Max Beispiel";
        vm.AddCommand.Execute(null);

        Assert.Null(vm.NewItemError);
        Assert.False(vm.IsNewDeviceMissing);
        Assert.False(vm.IsNewIssuedToMissing);
    }

    [Fact]
    public void A_refused_press_announces_the_complaint_and_the_marked_fields()
    {
        var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.AddCommand.Execute(null);

        Assert.Contains(nameof(IssuedItemsViewModel.NewItemError), changed);
        Assert.Contains(nameof(IssuedItemsViewModel.IsNewDeviceMissing), changed);
        Assert.Contains(nameof(IssuedItemsViewModel.IsNewIssuedToMissing), changed);
    }

    [Fact]
    public void Without_stammdaten_geraete_the_suggestions_are_empty()
    {
        Assert.Empty(new IssuedItemsViewModel(NewSession(), () => { }).DeviceOptions);
    }

    // The first field in form order that is too long is the one named, so the message points at
    // the box to fix first.
    [Theory]
    [InlineData(true, true, false, false, false, "Gerät")]
    [InlineData(false, true, true, false, false, "Ausgegeben an")]
    [InlineData(false, false, true, true, false, "Telefon")]
    [InlineData(false, false, false, true, true, "Organisation / Kontakt")]
    public void With_two_fields_too_long_the_first_is_named(
        bool device, bool issuedTo, bool phone, bool details, bool remark, string named)
    {
        static string? Long(bool on, string? fallback) => on ? new string('1', IssuedItem.MaxRemarkLength + 1) : fallback;

        var error = IssuedItemsViewModel.Validate(
            Long(device, "AED"), Long(issuedTo, "Max"), Long(phone, null), Long(details, null), Long(remark, null));

        Assert.StartsWith($"{named} ist zu lang", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(IssuedItem.Device))]
    [InlineData(nameof(IssuedItem.IssuedTo))]
    public void A_required_field_too_long_is_explained(string field)
    {
        var device = field == nameof(IssuedItem.Device) ? new string('x', IssuedItem.MaxDeviceLength + 1) : "AED";
        var issuedTo = field == nameof(IssuedItem.IssuedTo) ? new string('x', IssuedItem.MaxIssuedToLength + 1) : "Max";

        Assert.EndsWith("ist zu lang (höchstens 200 Zeichen)", IssuedItemsViewModel.Validate(device, issuedTo, null, null, null), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_field_exactly_at_its_cap_is_accepted()
    {
        Assert.Null(IssuedItemsViewModel.Validate(
            new string('x', IssuedItem.MaxDeviceLength),
            new string('x', IssuedItem.MaxIssuedToLength),
            new string('1', IssuedItem.MaxPhoneLength),
            new string('x', IssuedItem.MaxDetailsLength),
            new string('x', IssuedItem.MaxRemarkLength)));
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
        var changes = 0;
        var vm = new IssuedItemsViewModel(session, () => changes++, Devices);
        Assert.Equal("Alle Geräte zurück", vm.Summary);
        Assert.False(vm.HasItemsOut);

        vm.Items[0].UndoReturnCommand.Execute(null);

        Assert.Equal(1, changes);

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

        var changes = 0;
        var tracked = new IssuedItemsViewModel(session, () => changes++, Devices);
        tracked.Items[0].Remark = "Gehäuse beschädigt";
        Assert.Equal(1, changes);

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
    public void The_stock_hint_counts_what_is_still_out_against_the_stammdaten()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices) { NewDevice = "weste eal " };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("2 von 2 frei", vm.NewDeviceStock);
        Assert.False(vm.IsNewDeviceExhausted);

        session.IssueItem("Weste EAL", "Erika Beispiel", null, null, null);
        Assert.Equal("1 von 2 frei", vm.NewDeviceStock);
        Assert.Contains(nameof(IssuedItemsViewModel.NewDeviceStock), changed);
        Assert.Contains(nameof(IssuedItemsViewModel.IsNewDeviceExhausted), changed);

        // Another spelling of the same Gerät is the same Weste.
        session.IssueItem("WESTE EAL", "Max Beispiel", null, null, null);
        Assert.Equal("Kein Gerät frei: 2 von 2 ausgegeben", vm.NewDeviceStock);
        Assert.True(vm.IsNewDeviceExhausted);

        session.ReturnIssuedItem(session.Incident.IssuedItems[0].Id);
        Assert.Equal("1 von 2 frei", vm.NewDeviceStock);
        Assert.False(vm.IsNewDeviceExhausted);
    }

    [Fact]
    public void Typing_a_geraet_updates_the_stock_hint()
    {
        using var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.NewDevice = "AED";

        Assert.Equal("1 von 1 frei", vm.NewDeviceStock);
        Assert.Contains(nameof(IssuedItemsViewModel.NewDeviceStock), changed);
        Assert.Contains(nameof(IssuedItemsViewModel.IsNewDeviceExhausted), changed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Sonstiges")] // in the Stammdaten, but not counted
    [InlineData("Wärmebildkamera")] // not in the Stammdaten at all
    public void A_geraet_nobody_counted_has_no_stock_hint_and_is_never_exhausted(string? device)
    {
        var session = NewSession();
        session.IssueItem("Sonstiges", "Max Beispiel", null, null, null);
        session.IssueItem("Wärmebildkamera", "Max Beispiel", null, null, null);
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices) { NewDevice = device };

        Assert.Null(vm.NewDeviceStock);
        Assert.False(vm.IsNewDeviceExhausted);
    }

    // A Gerät whose every counted one is out is no longer offered, and comes back with a ZURÜCK (#838).
    [Fact]
    public void The_suggestions_leave_out_a_geraet_whose_every_one_is_ausgegeben()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices);

        session.IssueItem("Weste EAL", "Erika Beispiel", null, null, null);
        Assert.Contains("Weste EAL", vm.DeviceOptions);

        // Another spelling of the same Gerät is the same Weste.
        session.IssueItem("weste eal", "Max Beispiel", null, null, null);
        Assert.Equal(["Mobilteil 1", "AED", "Sonstiges"], vm.DeviceOptions);
    }

    [Fact]
    public void A_returned_geraet_is_offered_again()
    {
        var session = NewSession();
        session.IssueItem("AED", "Erika Beispiel", null, null, null);
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices);
        Assert.DoesNotContain("AED", vm.DeviceOptions);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Items[0].ReturnCommand.Execute(null);

        Assert.Contains(nameof(IssuedItemsViewModel.DeviceOptions), changed);
        Assert.Equal(["Mobilteil 1", "AED", "Weste EAL", "Sonstiges"], vm.DeviceOptions);
    }

    [Fact]
    public void A_geraet_nobody_counted_stays_in_the_suggestions()
    {
        var session = NewSession();
        session.IssueItem("Sonstiges", "Erika Beispiel", null, null, null);
        session.IssueItem("Sonstiges", "Max Beispiel", null, null, null);

        using var vm = new IssuedItemsViewModel(session, () => { }, Devices);

        Assert.Contains("Sonstiges", vm.DeviceOptions);
    }

    [Fact]
    public void Handing_out_a_geraet_with_none_left_asks_first_and_cancelling_keeps_the_form()
    {
        var session = NewSession();
        session.IssueItem("Mobilteil 1", "Erika Beispiel", null, null, null);
        string? title = null;
        string? message = null;
        var changes = 0;
        using var vm = new IssuedItemsViewModel(session, () => changes++, Devices, (t, m, _) =>
        {
            title = t;
            message = m;
        })
        {
            NewDevice = "mobilteil 1",
            NewIssuedTo = "Max Beispiel",
            NewRemark = "Ersatzakku",
        };
        var submitted = new List<EntrySubmittedEventArgs>();
        vm.EntrySubmitted += (_, e) => submitted.Add(e);

        vm.AddCommand.Execute(null);

        Assert.Equal("Kein Gerät mehr frei", title);
        Assert.Equal("Laut Stammdaten gibt es 1 × „Mobilteil 1“, ausgegeben sind 1. Trotzdem ausgeben?", message);
        Assert.Single(session.Incident.IssuedItems);
        Assert.Equal("mobilteil 1", vm.NewDevice);
        Assert.Equal("Max Beispiel", vm.NewIssuedTo);
        Assert.Equal("Ersatzakku", vm.NewRemark);
        Assert.Equal(0, changes);
        Assert.Empty(submitted);
    }

    [Fact]
    public void Confirming_hands_the_geraet_out_anyway()
    {
        var session = NewSession();
        session.IssueItem("AED", "Erika Beispiel", null, null, null);
        var changes = 0;
        using var vm = new IssuedItemsViewModel(session, () => changes++, Devices, (_, _, onConfirmed) => onConfirmed())
        {
            NewDevice = "AED",
            NewIssuedTo = "Max Beispiel",
            NewIssuedToPhone = "0171 0000002",
        };

        vm.AddCommand.Execute(null);

        Assert.Equal(2, session.Incident.ItemsOut);
        var item = session.Incident.IssuedItems[1];
        Assert.Equal("AED", item.Device);
        Assert.Equal("Max Beispiel", item.IssuedTo);
        Assert.Equal("0171 0000002", item.IssuedToPhone);
        Assert.Null(vm.NewDevice);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_geraet_still_in_stock_is_handed_out_without_asking()
    {
        var session = NewSession();
        session.IssueItem("Weste EAL", "Erika Beispiel", null, null, null);
        var asked = false;
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices, (_, _, _) => asked = true)
        {
            NewDevice = "Weste EAL",
            NewIssuedTo = "Max Beispiel",
        };

        vm.AddCommand.Execute(null);

        Assert.False(asked);
        Assert.Equal(2, session.Incident.ItemsOut);
    }

    [Fact]
    public void Removing_a_row_asks_for_confirmation_before_touching_the_session()
    {
        var session = NewSession();
        session.IssueItem("AED", "Max Beispiel", null, null, null);
        string? confirmTitle = null;
        string? confirmMessage = null;
        Action? confirmAction = null;
        var changes = 0;
        var vm = new IssuedItemsViewModel(session, () => changes++, Devices, (title, message, onConfirmed) =>
        {
            confirmTitle = title;
            confirmMessage = message;
            confirmAction = onConfirmed;
        });

        vm.Items[0].RemoveCommand.Execute(null);

        Assert.Single(vm.Items); // nothing happened yet — the host only recorded the request
        Assert.Equal(0, changes);
        Assert.Equal("Ausgabe entfernen?", confirmTitle);
        Assert.Equal("„AED“ an „Max Beispiel“ aus der Ausgabeliste entfernen. Fortfahren?", confirmMessage);

        confirmAction!();

        Assert.Empty(vm.Items);
        Assert.Empty(session.Incident.IssuedItems);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_change_made_elsewhere_rebuilds_the_list()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        session.IssueItem("Weste EAL", "Erika Beispiel", null, null, null);

        Assert.Contains(nameof(IssuedItemsViewModel.Summary), changed);
        Assert.Contains(nameof(IssuedItemsViewModel.HasItemsOut), changed);
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
        seed.IssueItem("Fernglas", "Erika Beispiel", null, null, null);
        seed.ReturnIssuedItem(seed.Incident.IssuedItems[1].Id);
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");
        var vm = new IssuedItemsViewModel(ro, () => { }, Devices);

        var row = vm.Items[0];
        var returned = vm.Items[1];
        Assert.False(returned.UndoReturnCommand.CanExecute(null));
        returned.UndoReturnCommand.Execute(null); // inert, not throwing
        Assert.False(ro.Incident.IssuedItems[1].IsOut);
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.False(vm.ShowComposer);
        vm.IsNarrow = true;
        Assert.False(vm.ShowComposerButton);
        Assert.False(row.ReturnCommand.CanExecute(null));
        Assert.False(row.RemoveCommand.CanExecute(null));
        row.ReturnCommand.Execute(null); // inert, not throwing
        row.RemoveCommand.Execute(null); // inert, not throwing
        row.Remark = "nachträglich"; // inert, not throwing
        Assert.True(ro.Incident.IssuedItems[0].IsOut);
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
        Assert.False(vm.IsComposerOpen);
        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);
    }

    [Fact]
    public void On_a_wide_window_the_form_is_always_there_and_needs_no_button()
    {
        var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices);

        Assert.True(vm.ShowComposer);
        Assert.False(vm.ShowComposerButton);

        vm.IsComposerOpen = true;
        Assert.False(vm.ShowComposerButton);
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

    private static readonly IReadOnlyList<ContactEntry> Roster = ContactEntry.Roster(
        [
            new Person("Beispiel", "Max", "Gerätewart", null, "0171 0000002"),
            new Person("Muster", "Erika", null, null, null),
        ],
        [
            new Vehicle("FF Musterstadt", "Florian Musterstadt 11/1", 4, Phone: "0171 0000003"),
            new Vehicle("FF Musterstadt", "Florian Musterstadt 40/1", 9),
        ]);

    // #837: the Einsatz's own Beteiligte first, then everyone the Kontakte tab lists, each name once.
    [Fact]
    public void Issued_to_options_list_beteiligte_then_kontakte_without_duplicates()
    {
        var session = NewSession();
        session.AddInvolvedParty("Erika Beispiel", "0171 0000001", null);
        session.AddInvolvedParty("muster, erika", null, null);

        using var vm = new IssuedItemsViewModel(session, () => { }, Devices, contacts: Roster);

        Assert.Equal(
            new[] { "Erika Beispiel", "muster, erika", "Beispiel, Max", "Florian Musterstadt 11/1" },
            vm.IssuedToOptions);
    }

    [Fact]
    public void A_beteiligter_added_later_is_offered_without_reopening()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices, contacts: Roster);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        session.AddInvolvedParty("POK Mustermann", null, null);

        Assert.Equal("POK Mustermann", vm.IssuedToOptions[0]);
        Assert.Contains(nameof(IssuedItemsViewModel.IssuedToOptions), changed);
    }

    [Fact]
    public void An_ausgabe_leaves_the_issued_to_suggestions_in_place()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices, contacts: Roster);
        var before = vm.IssuedToOptions;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        session.IssueItem("AED", "Beispiel, Max", null, null, null);

        Assert.Same(before, vm.IssuedToOptions);
        Assert.DoesNotContain(nameof(IssuedItemsViewModel.IssuedToOptions), changed);
    }

    [Theory]
    [InlineData("Beispiel, Max", "0171 0000002")]
    [InlineData(" florian musterstadt 11/1 ", "0171 0000003")]
    [InlineData("Erika Beispiel", "0171 0000001")]
    public void Picking_a_known_name_fills_an_empty_phone(string picked, string phone)
    {
        var session = NewSession();
        session.AddInvolvedParty("Erika Beispiel", "0171 0000001", null);
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices, contacts: Roster);

        vm.NewIssuedTo = picked;

        Assert.Equal(phone, vm.NewIssuedToPhone);
    }

    [Fact]
    public void Picking_a_known_name_never_overwrites_a_typed_phone()
    {
        using var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices, contacts: Roster)
        {
            NewIssuedToPhone = "0171 0000009",
        };

        vm.NewIssuedTo = "Beispiel, Max";

        Assert.Equal("0171 0000009", vm.NewIssuedToPhone);
    }

    [Fact]
    public void Changing_the_name_away_clears_the_phone_it_filled()
    {
        using var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices, contacts: Roster);
        vm.NewIssuedTo = "Beispiel, Max";

        vm.NewIssuedTo = "Beispiel, Ma";

        Assert.Null(vm.NewIssuedToPhone);
    }

    [Fact]
    public void A_phone_corrected_after_the_pick_stays_when_the_name_changes()
    {
        using var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices, contacts: Roster);
        vm.NewIssuedTo = "Beispiel, Max";
        vm.NewIssuedToPhone = "0171 0000009";

        vm.NewIssuedTo = "Max Beispiel";

        Assert.Equal("0171 0000009", vm.NewIssuedToPhone);
    }

    [Fact]
    public void A_name_nobody_recorded_is_issued_as_typed_without_a_phone()
    {
        var session = NewSession();
        using var vm = new IssuedItemsViewModel(session, () => { }, Devices, contacts: Roster)
        {
            NewDevice = "AED",
            NewIssuedTo = "Nachbar Hausnummer 3",
        };

        vm.AddCommand.Execute(null);

        var item = Assert.Single(session.Incident.IssuedItems);
        Assert.Equal("Nachbar Hausnummer 3", item.IssuedTo);
        Assert.Null(item.IssuedToPhone);
    }

    [Fact]
    public void After_an_ausgabe_the_next_pick_fills_the_phone_again()
    {
        using var vm = new IssuedItemsViewModel(NewSession(), () => { }, Devices, contacts: Roster)
        {
            NewDevice = "AED",
            NewIssuedTo = "Beispiel, Max",
        };
        vm.AddCommand.Execute(null);

        vm.NewIssuedTo = "Florian Musterstadt 11/1";

        Assert.Equal("0171 0000003", vm.NewIssuedToPhone);
    }

    [Fact]
    public void Without_contacts_or_beteiligte_there_are_no_issued_to_suggestions()
    {
        Assert.Empty(new IssuedItemsViewModel(NewSession(), () => { }).IssuedToOptions);
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
