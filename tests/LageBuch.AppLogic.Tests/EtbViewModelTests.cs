using System.Collections.Specialized;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class EtbViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void AddEntry_appends_to_journal_and_clears_input_and_fires_onchanged()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => changes++)
        {
            NewText = "Lagemeldung",
            NewFrom = "ILS",
        };

        Assert.True(vm.AddEntryCommand.CanExecute(null));
        vm.AddEntryCommand.Execute(null);

        // Journal[0] / Entries[^1] is the automatic "Einsatz begonnen" entry from StartNew;
        // the grid is newest-first, so the manual entry lands at the top.
        Assert.Equal(2, session.Incident.Journal.Count);
        Assert.Equal("Lagemeldung", session.Incident.Journal[^1].Text);
        Assert.Equal("Lagemeldung", vm.Entries[0].Text);
        Assert.Equal(string.Empty, vm.NewText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddEntry_disabled_when_text_blank()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "  " };

        Assert.True(vm.AddEntryCommand.CanExecute(null)); // the press is the question (#412)
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.NewTextError);
        Assert.Single(session.Incident.Journal); // only the automatic "Einsatz begonnen" entry

        vm.NewText = "Lagemeldung";
        Assert.Null(vm.NewTextError); // fixed as it is typed, without a second press
    }

    [Fact]
    public void An_added_entry_leaves_the_dock_quiet_for_the_next_one()
    {
        var vm = NewVm();
        vm.AddEntryCommand.Execute(null); // provokes the message
        vm.NewText = "Lagemeldung";

        vm.AddEntryCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewText);
        Assert.Null(vm.NewTextError);
    }

    [Fact]
    public void ReadOnly_session_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Close();
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "x" };

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddEntryCommand.CanExecute(null));
    }

    // A real Einsatz showed the RICHTUNG picker was never worth the keystroke, so the dock no
    // longer asks. The domain still wants a direction, and Internal is the neutral one: it is
    // never shown, and it keeps the entry editable and visible under the hide-system filter.
    [Fact]
    public void Manually_added_entries_are_stored_as_internal()
    {
        var created = new List<string>();
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }, created.Add);

        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        vm.NewText = "Wasserversorgung prüfen";
        vm.AddEntryAndCreateTaskCommand.Execute(null);

        Assert.Equal(
            new[] { EtbDirection.Internal, EtbDirection.Internal },
            session.Incident.Journal.Skip(1).Select(e => e.Direction));
        Assert.All(vm.Entries, e => Assert.True(e.IsEditable));
        Assert.Equal(new[] { "Wasserversorgung prüfen" }, created);
    }

    // #424, the user-visible half: a CO reading must survive the default filter. Practitioners
    // reported the change as "not documented" precisely because it did not.
    [Fact]
    public void Measurement_entries_survive_the_default_system_filter()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
        Assert.True(vm.HideSystemEntries);

        session.AddCoBuilding("Haus A", 2, 3);
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 1, 45);

        var texts = vm.Entries.Select(e => e.Text).ToList();
        Assert.Contains(texts, t => t.Contains("45 ppm", StringComparison.Ordinal));

        // ... while the bookkeeping around it stays hidden, which is what #223 asked for.
        Assert.DoesNotContain(texts, t => t.Contains("Einsatz begonnen", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("CO-Messprotokoll eröffnet", StringComparison.Ordinal));
    }

    [Fact]
    public void Measurement_entries_are_not_editable()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });

        session.AddCoBuilding("Haus A", 2, 3);
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 1, 45);

        var row = vm.Entries.First(e => e.DirectionValue == EtbDirection.Measurement);
        Assert.False(row.IsEditable);
    }

    [Fact]
    public void HideSystemEntries_defaults_to_true()
    {
        // A fresh incident's journal should not open showing the "Einsatz begonnen" trace (#223).
        Assert.True(NewVm().HideSystemEntries);
    }

    [Fact]
    public void HideSystemEntries_hides_system_rows_and_keeps_human_rows()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        // StartNew logs "Einsatz begonnen" (System); add one human entry.
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "Lagemeldung" };
        vm.AddEntryCommand.Execute(null);

        // Hidden by default (#223): only the human entry shows until toggled off.
        var only = Assert.Single(vm.Entries);
        Assert.Equal("Lagemeldung", only.Text);
        Assert.Equal(EtbDirection.Internal, only.DirectionValue);

        vm.HideSystemEntries = false;
        Assert.Equal(2, vm.Entries.Count);

        // Toggling back hides the System row again.
        vm.HideSystemEntries = true;
        only = Assert.Single(vm.Entries);
        Assert.Equal("Lagemeldung", only.Text);
    }

    [Fact]
    public void System_entry_added_while_filtering_stays_hidden_but_human_entry_appears()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
        vm.HideSystemEntries = true;
        Assert.Empty(vm.Entries); // the "Einsatz begonnen" System row is hidden

        // A unit is added elsewhere -> a System entry reaches the journal.
        session.Incident.AddForceUnit(clock, session.Operator!, "FFB", 6);
        vm.Sync();
        Assert.Empty(vm.Entries); // still hidden

        // A human entry, by contrast, shows immediately.
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        var only = Assert.Single(vm.Entries);
        Assert.Equal("Lagemeldung", only.Text);
    }

    [Fact]
    public void CallSignOptions_reflects_the_Funkrufnamen_master_data()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        // Callsign suggestions derive from the vehicles and the roster's callsigns.
        var masterData = MasterDataSet.Empty with
        {
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", null) },
        };

        var vm = new EtbViewModel(session, clock, masterData, () => { });

        Assert.Equal(new[] { "FFB 1/40/1", "Land 1" }, vm.CallSignOptions);
    }

    [Fact]
    public void BeginEdit_populates_EditText_and_EditingEntry()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        var row = vm.Entries[0];

        row.BeginEditCommand.Execute(null);

        Assert.Same(row, vm.EditingEntry);
        Assert.Equal("Lagemeldung", vm.EditText);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void Opening_and_closing_the_edit_panel_tells_the_Save_button_to_recheck()
    {
        // A bound button reads CanExecute once and then only on CanExecuteChanged, so a live
        // CanExecute alone would not have caught Save staying grey in the field (#467).
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        var raised = 0;
        vm.SaveEditCommand.CanExecuteChanged += (_, _) => raised++;

        vm.Entries[0].BeginEditCommand.Execute(null);

        Assert.True(raised > 0);
        Assert.True(vm.SaveEditCommand.CanExecute(null));

        raised = 0;
        vm.CancelEditCommand.Execute(null);

        Assert.True(raised > 0);
        Assert.False(vm.SaveEditCommand.CanExecute(null));
    }

    [Fact]
    public void SaveEdit_writes_through_and_clears_edit_state()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        vm.Entries[0].BeginEditCommand.Execute(null);
        vm.EditText = "Lagemeldung korrigiert";

        Assert.True(vm.SaveEditCommand.CanExecute(null));
        vm.SaveEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal(string.Empty, vm.EditText);

        // A save also appends a System trace of the correction (security review, #73), so the
        // edited row is no longer necessarily Entries[0] -- find it by its new text instead.
        var row = Assert.Single(vm.Entries, e => e.Text == "Lagemeldung korrigiert");
        Assert.True(row.WasEdited);
    }

    [Fact]
    public void SaveEdit_names_the_emptied_entry_rather_than_going_grey()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        vm.Entries[0].BeginEditCommand.Execute(null);

        vm.EditText = "   ";
        Assert.True(vm.SaveEditCommand.CanExecute(null));
        vm.SaveEditCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.EditTextError);
        Assert.True(vm.IsEditing); // the panel stays open on the emptied field
        Assert.Null(vm.NewTextError); // the dock above it is a different form and stays quiet
    }

    [Fact]
    public void SaveEdit_is_still_impossible_with_no_entry_being_edited()
    {
        var vm = NewVm();

        // Not an input rule: with no panel open there is no field to name, so the command stays
        // genuinely unavailable rather than becoming a press that explains itself.
        Assert.False(vm.IsEditing);
        Assert.False(vm.SaveEditCommand.CanExecute(null));
    }

    [Fact]
    public void CancelEdit_clears_edit_state_without_writing_through()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        vm.Entries[0].BeginEditCommand.Execute(null);
        vm.EditText = "Verworfen";

        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("Lagemeldung", vm.Entries[0].Text);
        Assert.False(vm.Entries[0].WasEdited);
    }

    [Fact]
    public void CanEdit_is_false_for_System_entries()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { HideSystemEntries = false };

        // The automatic "Einsatz begonnen" entry from StartNew is the only row at this point.
        var systemRow = Assert.Single(vm.Entries);
        Assert.Equal(EtbDirection.System, systemRow.DirectionValue);

        Assert.False(systemRow.IsEditable);
        Assert.False(systemRow.BeginEditCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_session_disables_editing()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung");
        session.Close();
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });

        var row = Assert.Single(vm.Entries, r => r.Text == "Lagemeldung");
        Assert.False(row.BeginEditCommand.CanExecute(null));
    }

    /// <summary>
    /// Viewing an edited entry's history must not require edit permission -- a closed incident's
    /// history is exactly the case where it matters most (security review, #73).
    /// </summary>
    [Fact]
    public void History_stays_viewable_on_a_read_only_session()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var entry = session.Incident.AddJournalEntry(clock, session.Operator!, EtbDirection.Incoming, "Lagemeldung");
        session.Incident.EditJournalEntry(clock, session.Operator!, entry.Id, "Lagemeldung korrigiert");
        session.Close();
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });

        var row = Assert.Single(vm.Entries, r => r.Text == "Lagemeldung korrigiert");
        Assert.True(row.WasEdited);
        Assert.False(row.BeginEditCommand.CanExecute(null)); // still can't edit
        Assert.True(row.ShowHistoryCommand.CanExecute(null)); // but can still view the history

        row.ShowHistoryCommand.Execute(null);

        Assert.Same(row, vm.HistoryEntry);
        Assert.Equal("Lagemeldung", Assert.Single(row.Edits).PreviousText);
    }

    [Fact]
    public void ShowHistoryCommand_is_disabled_for_a_never_edited_entry()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);

        var row = Assert.Single(vm.Entries, e => e.Text == "Lagemeldung");

        Assert.False(row.ShowHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void CloseHistory_clears_the_history_selection()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        var row = vm.Entries[0];
        row.BeginEditCommand.Execute(null);
        vm.EditText = "Korrigiert";
        vm.SaveEditCommand.Execute(null);
        var edited = Assert.Single(vm.Entries, e => e.Text == "Korrigiert");

        edited.ShowHistoryCommand.Execute(null);
        Assert.NotNull(vm.HistoryEntry);

        vm.CloseHistoryCommand.Execute(null);
        Assert.Null(vm.HistoryEntry);
    }

    [Fact]
    public void CreateTaskCommand_is_hidden_when_the_host_offers_no_task_feature()
    {
        var vm = NewVm(); // NewVm passes no createTaskFromEntry delegate
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);

        var row = Assert.Single(vm.Entries, e => e.Text == "Lagemeldung");

        Assert.False(row.CanCreateTask);
        Assert.False(row.CreateTaskCommand.CanExecute(null));
    }

    [Fact]
    public void CreateTaskCommand_invokes_the_delegate_with_the_row_text()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        string? capturedText = null;
        void CaptureCreateTask(string text) => capturedText = text;

        var vm = new EtbViewModel(
            session,
            clock,
            MasterDataSet.Empty,
            () => { },
            CaptureCreateTask)
        { NewText = "Lagemeldung" };
        vm.AddEntryCommand.Execute(null);
        var row = Assert.Single(vm.Entries, e => e.Text == "Lagemeldung");

        Assert.True(row.CanCreateTask);
        row.CreateTaskCommand.Execute(null);

        Assert.Equal("Lagemeldung", capturedText);
    }

    [Fact]
    public void CreateTaskCommand_is_hidden_on_a_readonly_session_even_when_the_host_offers_tasks()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung");
        session.Close();
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }, _ => { });

        var row = Assert.Single(vm.Entries, r => r.Text == "Lagemeldung");
        Assert.False(row.CanCreateTask);
        Assert.False(row.CreateTaskCommand.CanExecute(null));
    }

    // #415: an entry to the Leitstelle is usually the Rückmeldung itself, so the Lagebuchführer is
    // offered the reset rather than left with a header still shouting FÄLLIG.
    [Theory]
    [InlineData("ILS")]
    [InlineData(" ils ")]
    public void An_entry_to_the_dispatch_centre_offers_the_reminder_reset(string to)
    {
        var offers = 0;
        var vm = NewVm(MasterDataSet.Empty, () => offers++);
        vm.NewText = "Lagemeldung: Brand unter Kontrolle";
        vm.NewTo = to;

        vm.AddEntryCommand.Execute(null);

        Assert.Equal(1, offers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EL")]
    [InlineData("ILS Ffb")]
    public void An_entry_to_anyone_else_offers_nothing(string to)
    {
        var offers = 0;
        var vm = NewVm(MasterDataSet.Empty, () => offers++);
        vm.NewText = "Lagemeldung";
        vm.NewTo = to;

        vm.AddEntryCommand.Execute(null);

        Assert.Equal(0, offers);
    }

    // #400: the match follows the configured name, so an install that says "Kreisleitstelle" gets
    // the offer for that name and not for the Bavarian default.
    [Fact]
    public void The_offer_follows_the_configured_dispatch_centre_name()
    {
        var offers = 0;
        var md = MasterDataSet.Empty with
        {
            Settings = IncidentSettings.Defaults with { DispatchCentreName = "Kreisleitstelle" },
        };
        var vm = NewVm(md, () => offers++);

        vm.NewText = "Lagemeldung";
        vm.NewTo = "ILS";
        vm.AddEntryCommand.Execute(null);
        Assert.Equal(0, offers);

        vm.NewText = "Lagemeldung";
        vm.NewTo = "Kreisleitstelle";
        vm.AddEntryCommand.Execute(null);
        Assert.Equal(1, offers);
    }

    [Fact]
    public void Adding_an_entry_to_the_dispatch_centre_with_a_task_also_offers_the_reset()
    {
        var offers = 0;
        var tasks = new List<string>();
        var vm = NewVm(MasterDataSet.Empty, () => offers++, tasks.Add);
        vm.NewText = "Lagemeldung";
        vm.NewTo = "ILS";

        vm.AddEntryAndCreateTaskCommand.Execute(null);

        Assert.Equal(1, offers);
        Assert.Single(tasks);
    }

    // The offer comes after the entry is in the journal, so confirming it never races the entry.
    [Fact]
    public void The_offer_is_made_after_the_entry_is_written()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var journalCountAtOffer = -1;
        var vm = new EtbViewModel(
            session, clock, MasterDataSet.Empty, () => { }, offerReminderReset: () => journalCountAtOffer = session.Incident.Journal.Count)
        {
            NewText = "Lagemeldung",
            NewTo = "ILS",
        };

        vm.AddEntryCommand.Execute(null);

        Assert.Equal(2, journalCountAtOffer); // "Einsatz begonnen" + the new entry
    }

    [Fact]
    public void An_edit_updates_the_row_in_place_so_the_grid_keeps_its_selection()
    {
        // #529: replacing the row instance is a Replace on Entries, and the DataGrid drops the
        // selection of an item it no longer sees. The edit must reach the same instance instead.
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "Lagemeldung" };
        vm.AddEntryCommand.Execute(null);
        var row = vm.Entries[0];
        var changedProperties = new List<string?>();
        row.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
        var nonAdds = new List<NotifyCollectionChangedAction>();
        vm.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
            {
                nonAdds.Add(e.Action);
            }
        };

        // Straight through the session, as an edit from another device arrives: Changed -> Sync.
        session.EditJournalEntry(row.Id, "Lagemeldung korrigiert");

        Assert.Empty(nonAdds);
        Assert.Same(row, Assert.Single(vm.Entries, e => e.Id == row.Id));
        Assert.Equal("Lagemeldung korrigiert", row.Text);
        Assert.True(row.WasEdited);
        Assert.Single(row.Edits);
        Assert.Contains(nameof(EtbEntryRow.Text), changedProperties);
        Assert.True(row.ShowHistoryCommand.CanExecute(null)); // the first edit enables the history
    }

    [Fact]
    public void An_open_history_follows_a_later_edit_of_its_entry()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "Lagemeldung" };
        vm.AddEntryCommand.Execute(null);
        var row = vm.Entries[0];
        session.EditJournalEntry(row.Id, "Lagemeldung korrigiert");
        row.ShowHistoryCommand.Execute(null);

        session.EditJournalEntry(row.Id, "Lagemeldung nochmals korrigiert");

        Assert.Same(row, vm.HistoryEntry);
        Assert.Equal(2, vm.HistoryEntry!.Edits.Count);
    }

    [Fact]
    public void An_edit_from_elsewhere_closes_the_editor_open_on_that_entry()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "Lagemeldung" };
        vm.AddEntryCommand.Execute(null);
        var row = vm.Entries[0];
        row.BeginEditCommand.Execute(null);
        vm.EditText = "halb getippt";

        session.EditJournalEntry(row.Id, "Lagemeldung korrigiert"); // another device saved first

        Assert.False(vm.IsEditing);
        Assert.Equal(string.Empty, vm.EditText);
    }

    [Fact]
    public void A_batch_of_new_entries_lands_newest_first_and_in_order()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        for (var i = 1; i <= 5; i++)
        {
            session.AddJournalEntry(EtbDirection.Internal, $"vorher {i}");
        }

        // The first Sync renders a five-entry tail at once, as joining mid-Einsatz does.
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
        Assert.Equal(["vorher 5", "vorher 4", "vorher 3", "vorher 2", "vorher 1"], vm.Entries.Select(e => e.Text));

        // A second batch reaching the journal between two Syncs, as a snapshot does: the domain
        // call bypasses the session, so Changed does not fire per entry.
        for (var i = 1; i <= 3; i++)
        {
            session.Incident.AddJournalEntry(clock, session.Operator!, EtbDirection.Internal, $"danach {i}", null, null);
        }

        vm.Sync();

        Assert.Equal(
            ["danach 3", "danach 2", "danach 1", "vorher 5", "vorher 4", "vorher 3", "vorher 2", "vorher 1"],
            vm.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Toggling_the_System_filter_keeps_newest_first()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddJournalEntry(EtbDirection.Internal, "erste");
        session.AddJournalEntry(EtbDirection.Internal, "zweite");
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });

        vm.HideSystemEntries = false;
        Assert.Equal(["zweite", "erste", "Einsatz begonnen"], vm.Entries.Select(e => e.Text));

        vm.HideSystemEntries = true;
        Assert.Equal(["zweite", "erste"], vm.Entries.Select(e => e.Text));
    }

    // #540: the view refocuses on EntrySubmitted -- EINTRAG after an add, the invalid field after a
    // refusal. "Hinzufügen & Aufgabe" hands over to the task dialog, which owns focus from there.
    [Fact]
    public void Submitting_reports_whether_the_entry_was_added()
    {
        var vm = NewVm();
        var outcomes = new List<bool>();
        vm.EntrySubmitted += (_, e) => outcomes.Add(e.Added);

        vm.AddEntryCommand.Execute(null); // no text: refused
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(new[] { false, true }, outcomes);
    }

    [Fact]
    public void Adding_with_a_task_reports_only_a_refusal()
    {
        var vm = NewVm(MasterDataSet.Empty, () => { }, _ => { });
        var outcomes = new List<bool>();
        vm.EntrySubmitted += (_, e) => outcomes.Add(e.Added);

        vm.AddEntryAndCreateTaskCommand.Execute(null); // no text: refused, focus goes to EINTRAG
        vm.NewText = "Wasserversorgung prüfen";
        vm.AddEntryAndCreateTaskCommand.Execute(null); // the task dialog takes focus

        Assert.Equal(new[] { false }, outcomes);
    }

    private static LocalIncidentSession NewSession(FixedClock clock) => TestSession.StartNew(
        new FakeStore(),
        clock,
        new SessionOperator("Müller", "FFB 12/1"),
        "/x.fwincident",
        Array.Empty<(string, bool)>(),
        Array.Empty<(string, bool)>());

    private static EtbViewModel NewVm(MasterDataSet masterData, Action offerReminderReset, Action<string>? createTask = null)
    {
        var clock = new FixedClock(T0);
        return new EtbViewModel(NewSession(clock), clock, masterData, () => { }, createTask, offerReminderReset);
    }

    private static EtbViewModel NewVm()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
    }
}
