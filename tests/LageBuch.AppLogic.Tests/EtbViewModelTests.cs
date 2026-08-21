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
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => changes++)
        {
            NewText = "Lagemeldung",
            NewDirection = EtbDirection.Incoming,
            NewFrom = "ILS"
        };

        Assert.True(vm.AddEntryCommand.CanExecute(null));
        vm.AddEntryCommand.Execute(null);

        // Journal[0] / Entries[^1] is the automatic "Einsatz begonnen" entry from StartNew;
        // the grid is newest-first, so the manual entry lands at the top.
        Assert.Equal(2, session.Incident.Journal.Count);
        Assert.Equal("Lagemeldung", session.Incident.Journal[^1].Text);
        Assert.Equal("Lagemeldung", vm.Entries[0].Text);
        Assert.Equal("", vm.NewText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddEntry_disabled_when_text_blank()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "  " };

        Assert.False(vm.AddEntryCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_session_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        session.Close();
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "x" };

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddEntryCommand.CanExecute(null));
    }

    [Fact]
    public void DirectionOptions_offers_the_human_directions_only()
    {
        var vm = NewVm();
        Assert.Contains(EtbDirection.Incoming, vm.DirectionOptions.Select(o => o.Value));
        Assert.Contains(EtbDirection.Outgoing, vm.DirectionOptions.Select(o => o.Value));
        Assert.Contains(EtbDirection.Internal, vm.DirectionOptions.Select(o => o.Value));
        // System is written only by the app, never picked by a human.
        Assert.DoesNotContain(EtbDirection.System, vm.DirectionOptions.Select(o => o.Value));
    }

    [Fact]
    public void HideSystemEntries_hides_system_rows_and_keeps_human_rows()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        // StartNew logs "Einsatz begonnen" (System); add one human entry.
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }) { NewText = "Lagemeldung" };
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(2, vm.Entries.Count);

        vm.HideSystemEntries = true;

        var only = Assert.Single(vm.Entries);
        Assert.Equal("Lagemeldung", only.Text);
        Assert.Equal(EtbDirection.Incoming, only.DirectionValue);

        // Toggling back restores the hidden System row.
        vm.HideSystemEntries = false;
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void System_entry_added_while_filtering_stays_hidden_but_human_entry_appears()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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

    // The picker used to bind the bare enum, so Avalonia rendered "Incoming"/"Outgoing"/
    // "Internal" right next to a grid saying "Eingang"/"Ausgang"/"Intern". The old version of
    // this test asserted membership only, which is exactly why that slipped through.
    [Theory]
    [InlineData(EtbDirection.Incoming, "Eingang")]
    [InlineData(EtbDirection.Outgoing, "Ausgang")]
    [InlineData(EtbDirection.Internal, "Intern")]
    public void DirectionOptions_are_labelled_in_german(EtbDirection direction, string expected)
    {
        var option = Assert.Single(NewVm().DirectionOptions, o => o.Value == direction);
        Assert.Equal(expected, option.Label);
    }

    [Fact]
    public void DirectionOption_labels_match_the_grid_and_the_pdf()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.NewDirection = EtbDirection.Outgoing;
        vm.AddEntryCommand.Execute(null);

        var selected = Assert.Single(vm.DirectionOptions, o => o.Value == vm.NewDirection);
        Assert.Equal(vm.Entries[0].Direction, selected.Label);
    }

    [Fact]
    public void CallSignOptions_reflects_the_Funkrufnamen_master_data()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var masterData = MasterDataSet.Empty with { RadioCallSigns = new[] { "Leitstelle", "Land 1" } };

        var vm = new EtbViewModel(session, clock, masterData, () => { });

        Assert.Equal(new[] { "Leitstelle", "Land 1" }, vm.CallSignOptions);
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
    public void SaveEdit_is_disabled_when_edit_text_is_blank()
    {
        var vm = NewVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);
        vm.Entries[0].BeginEditCommand.Execute(null);

        vm.EditText = "   ";

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
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });

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
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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

    private static EtbViewModel NewVm()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        return new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
    }
}
