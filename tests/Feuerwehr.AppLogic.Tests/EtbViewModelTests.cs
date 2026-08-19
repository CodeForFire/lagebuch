using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.AppLogic.Tests;

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
        var vm = new EtbViewModel(session, clock, () => changes++)
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
        var vm = new EtbViewModel(session, clock, () => { }) { NewText = "  " };

        Assert.False(vm.AddEntryCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_session_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        session.Close();
        var vm = new EtbViewModel(session, clock, () => { }) { NewText = "x" };

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
        var vm = new EtbViewModel(session, clock, () => { }) { NewText = "Lagemeldung" };
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
        var vm = new EtbViewModel(session, clock, () => { });
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

    private static EtbViewModel NewVm()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        return new EtbViewModel(session, clock, () => { });
    }
}
