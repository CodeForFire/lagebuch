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
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());
        var vm = new EtbViewModel(session, clock, () => changes++)
        {
            NewText = "Lagemeldung",
            NewDirection = EtbDirection.Incoming,
            NewFrom = "ILS"
        };

        Assert.True(vm.AddEntryCommand.CanExecute(null));
        vm.AddEntryCommand.Execute(null);

        Assert.Single(session.Incident.Journal);
        Assert.Single(vm.Entries);
        Assert.Equal("", vm.NewText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddEntry_disabled_when_text_blank()
    {
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new EtbViewModel(session, clock, () => { }) { NewText = "  " };

        Assert.False(vm.AddEntryCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_session_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        session.Close(clock);
        var vm = new EtbViewModel(session, clock, () => { }) { NewText = "x" };

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddEntryCommand.CanExecute(null));
    }
}
