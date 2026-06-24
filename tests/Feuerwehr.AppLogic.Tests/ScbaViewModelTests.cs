using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
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

    private static ScbaViewModel Vm(FixedClock clock, IncidentSession session, Action? onChanged = null, FakeTicker? ticker = null) =>
        new(session, Md(), clock, ticker ?? new FakeTicker(), onChanged ?? (() => { }));

    private static ScbaTruppRow Register(ScbaViewModel vm, string designation = "Angriffstrupp", string members = "Müller / Schmidt")
    {
        vm.NewDesignation = designation;
        vm.NewMembers = members;
        vm.AddTruppCommand.Execute(null);
        return vm.Trupps[^1];
    }

    [Fact]
    public void AddTrupp_registers_a_waiting_trupp_and_does_not_start_the_clock()
    {
        var clock = new FixedClock(T0);
        var changes = 0;
        var session = NewSession(clock);
        var vm = Vm(clock, session, () => changes++);

        var row = Register(vm);

        Assert.Single(session.Incident.ScbaTrupps);
        Assert.True(row.IsWaiting);
        Assert.False(row.IsActive);
        Assert.Equal("Bereitgestellt", row.StatusDisplay);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("bereitgestellt"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddTrupp_disabled_when_required_fields_blank()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = "Angriffstrupp";
        vm.NewMembers = "  ";
        Assert.False(vm.AddTruppCommand.CanExecute(null));
    }

    [Fact]
    public void Start_sends_trupp_under_air_with_pressure_and_logs_etb()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm);

        Assert.False(row.RecordPressureCommand.CanExecute(null)); // can't record before start
        Assert.True(row.StartCommand.CanExecute(null));

        clock.Now = T0.AddMinutes(6);
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        Assert.True(row.IsActive);
        Assert.False(row.IsWaiting);
        Assert.False(row.StartCommand.CanExecute(null));
        Assert.True(row.RecordPressureCommand.CanExecute(null));
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("unter PA") && e.Text.Contains("300 bar"));
    }

    [Fact]
    public void Recording_pressure_logs_entry_and_low_reading_trips_alarm()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm);
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        row.PressureInput = 55;
        row.RecordPressureCommand.Execute(null);

        Assert.True(row.IsAlarm);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Druckkontrolle") && e.Text.Contains("55 bar"));
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Rückzugsalarm"));
    }

    [Fact]
    public void Header_reminder_tracks_the_soonest_due_active_trupp()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);

        Assert.False(vm.HasControlReminder); // nothing under air yet

        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        Assert.True(vm.HasControlReminder);
        Assert.False(vm.IsAnyControlDue);
        Assert.Contains("Nächste Druckabfrage", vm.NextControlDisplay);
    }

    [Fact]
    public void Tick_when_control_is_due_marks_due_in_header()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        var vm = Vm(clock, session, ticker: ticker);
        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(6); // past the 5-minute control interval
        ticker.Fire();

        Assert.True(row.IsControlDue);
        Assert.True(vm.IsAnyControlDue);
        Assert.Contains("fällig", vm.NextControlDisplay);
    }

    [Fact]
    public void Tick_past_max_duration_logs_exactly_one_alarm_entry()
    {
        var clock = new FixedClock(T0);
        var changes = 0;
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        var vm = Vm(clock, session, () => changes++, ticker);
        vm.NewMaxDurationMinutes = 30;
        var row = Register(vm);
        row.PressureInput = 300;
        row.StartCommand.Execute(null);
        var baseline = changes;

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();
        ticker.Fire(); // must not log a second alarm for the same trupp

        var alarms = session.Incident.Journal.Where(e => e.Text.Contains("Rückzugsalarm")).ToList();
        Assert.Single(alarms);
        Assert.Equal(baseline + 1, changes);
    }

    [Fact]
    public void MarkReturned_logs_entry_and_stops_clock()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm);
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(12);
        row.MarkReturnedCommand.Execute(null);

        Assert.True(row.IsReturned);
        Assert.Equal("—", row.RemainingDisplay);
        Assert.False(vm.HasControlReminder);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("zurück"));
    }

    [Fact]
    public void Readonly_session_disables_actions()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        seed.Close(clock);
        var ro = IncidentSession.OpenReadOnly(store, "/x.fwincident");

        var vm = Vm(clock, ro);
        vm.NewDesignation = "Angriffstrupp";
        vm.NewMembers = "Müller / Schmidt";
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddTruppCommand.CanExecute(null));
        Assert.False(vm.HasControlReminder);
    }

    [Fact]
    public void Dispose_unsubscribes_from_ticker()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var vm = Vm(clock, NewSession(clock), ticker: ticker);
        Assert.Equal(1, ticker.SubscriberCount);

        vm.Dispose();

        Assert.Equal(0, ticker.SubscriberCount);
    }
}
