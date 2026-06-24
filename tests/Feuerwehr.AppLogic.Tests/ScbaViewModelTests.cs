using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class ScbaViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => new(
        Roles: Array.Empty<string>(), Status: Array.Empty<string>(), Equipment: Array.Empty<string>(),
        Districts: Array.Empty<string>(), RadioCallSigns: new[] { "FFB 1/40/1" },
        Streets: Array.Empty<Street>(), ChecklistTemplate: Array.Empty<string>(),
        TruppTypes: new[] { "Angriffstrupp", "Wassertrupp" });

    private static IncidentSession NewSession(FixedClock clock) =>
        IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());

    [Fact]
    public void AddTrupp_adds_row_writes_etb_and_saves()
    {
        var clock = new FixedClock(T0);
        var changes = 0;
        var session = NewSession(clock);
        var vm = new ScbaViewModel(session, Md(), clock, new FakeTicker(), () => changes++)
        {
            NewDesignation = "Angriffstrupp",
            NewMembers = "Müller / Schmidt",
            NewEntryPressure = 300
        };

        Assert.True(vm.AddTruppCommand.CanExecute(null));
        vm.AddTruppCommand.Execute(null);

        Assert.Single(vm.Trupps);
        Assert.Single(session.Incident.ScbaTrupps);
        var entry = Assert.Single(session.Incident.Journal);
        Assert.Equal(EtbDirection.Internal, entry.Direction);
        Assert.Contains("Angriffstrupp", entry.Text);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddTrupp_disabled_when_required_fields_blank()
    {
        var clock = new FixedClock(T0);
        var vm = new ScbaViewModel(NewSession(clock), Md(), clock, new FakeTicker(), () => { })
        {
            NewDesignation = "Angriffstrupp",
            NewMembers = "  ",
            NewEntryPressure = 300
        };
        Assert.False(vm.AddTruppCommand.CanExecute(null));
    }

    [Fact]
    public void AddTrupp_disabled_when_readonly()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        seed.Close(clock);
        var ro = IncidentSession.OpenReadOnly(store, "/x.fwincident");

        var vm = new ScbaViewModel(ro, Md(), clock, new FakeTicker(), () => { })
        {
            NewDesignation = "Angriffstrupp",
            NewMembers = "Müller / Schmidt",
            NewEntryPressure = 300
        };
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddTruppCommand.CanExecute(null));
    }

    [Fact]
    public void Tick_past_max_duration_logs_exactly_one_alarm_entry()
    {
        var clock = new FixedClock(T0);
        var changes = 0;
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        var vm = new ScbaViewModel(session, Md(), clock, ticker, () => changes++)
        {
            NewDesignation = "Angriffstrupp",
            NewMembers = "Müller / Schmidt",
            NewEntryPressure = 300,
            NewMaxDurationMinutes = 30
        };
        vm.AddTruppCommand.Execute(null);
        var changesAfterAdd = changes;

        // Not yet due.
        clock.Now = T0.AddMinutes(20);
        ticker.Fire();
        Assert.Equal(changesAfterAdd, changes);

        // Past the limit: one alarm ETB entry written and persisted.
        clock.Now = T0.AddMinutes(31);
        ticker.Fire();
        // Firing again must NOT log a second alarm for the same trupp.
        ticker.Fire();

        var alarmEntries = session.Incident.Journal.Where(e => e.Text.Contains("Rückzugsalarm")).ToList();
        Assert.Single(alarmEntries);
        Assert.Equal(changesAfterAdd + 1, changes);
    }

    [Fact]
    public void Recording_low_pressure_trips_alarm_and_logs_entry()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = new ScbaViewModel(session, Md(), clock, new FakeTicker(), () => { })
        {
            NewDesignation = "Angriffstrupp",
            NewMembers = "Müller / Schmidt",
            NewEntryPressure = 300,
            NewReturnPressureBar = 60
        };
        vm.AddTruppCommand.Execute(null);

        var row = Assert.Single(vm.Trupps);
        row.PressureInput = 55;
        row.RecordPressureCommand.Execute(null);

        Assert.True(row.IsAlarm);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Rückzugsalarm"));
    }

    [Fact]
    public void MarkReturned_logs_entry_and_stops_countdown()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = new ScbaViewModel(session, Md(), clock, new FakeTicker(), () => { })
        {
            NewDesignation = "Angriffstrupp",
            NewMembers = "Müller / Schmidt",
            NewEntryPressure = 300
        };
        vm.AddTruppCommand.Execute(null);
        var row = Assert.Single(vm.Trupps);

        clock.Now = T0.AddMinutes(12);
        row.MarkReturnedCommand.Execute(null);

        Assert.False(row.IsActive);
        Assert.Equal("—", row.RemainingDisplay);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("zurück"));
        Assert.False(row.RecordPressureCommand.CanExecute(null));
    }

    [Fact]
    public void Dispose_unsubscribes_from_ticker()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var vm = new ScbaViewModel(NewSession(clock), Md(), clock, ticker, () => { });
        Assert.Equal(1, ticker.SubscriberCount);

        vm.Dispose();

        Assert.Equal(0, ticker.SubscriberCount);
    }
}
