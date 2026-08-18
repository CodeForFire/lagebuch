using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.AppLogic.Tests;

// Records Start/Stop (looping siren) and Play (one-shot spoken cues) so tests can assert the
// audible output fired without real audio.
internal sealed class FakeAlarmService : IAlarmService
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool IsSounding { get; private set; }
    public List<AlarmSound> Played { get; } = new();
    public void Start() { StartCount++; IsSounding = true; }
    public void Stop() { StopCount++; IsSounding = false; }
    public void Play(AlarmSound sound) => Played.Add(sound);
}

// Synchronous fake ticker — tests call Fire() to advance a "tick".
internal sealed class FakeTicker : ITicker
{
    private readonly List<Action> _subs = new();
    public int SubscriberCount => _subs.Count;
    public IDisposable Subscribe(Action onTick)
    {
        _subs.Add(onTick);
        return new Sub(() => _subs.Remove(onTick));
    }
    public void Fire() { foreach (var s in _subs.ToArray()) s(); }
    private sealed class Sub : IDisposable
    {
        private readonly Action _dispose;
        public Sub(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}

public class ReminderViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession session, FixedClock clock) NewSession()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());
        return (session, clock);
    }

    [Fact]
    public void Reminder_auto_starts_running_at_the_first_interval()
    {
        var (session, clock) = NewSession();
        var vm = new ReminderViewModel(session, clock, new FakeTicker(), new FakeAlarmService(), () => { }, firstIntervalMinutes: 15, recurringIntervalMinutes: 30);

        // The ILS report-back obligation runs for the whole incident — no manual start.
        Assert.True(vm.IsRunning);
        Assert.Equal("15:00", vm.RemainingDisplay);
        Assert.False(vm.IsDue);
        Assert.False(vm.AcknowledgeCommand.CanExecute(null));
    }

    [Fact]
    public void Tick_past_interval_makes_it_due()
    {
        var (session, clock) = NewSession();
        var ticker = new FakeTicker();
        var vm = new ReminderViewModel(session, clock, ticker, new FakeAlarmService(), () => { }, firstIntervalMinutes: 15, recurringIntervalMinutes: 30);

        clock.Now = T0.AddMinutes(15);
        ticker.Fire();

        Assert.True(vm.IsDue);
        Assert.Equal("fällig", vm.RemainingDisplay);
        Assert.True(vm.AcknowledgeCommand.CanExecute(null));
    }

    [Fact]
    public void Acknowledge_logs_one_outgoing_etb_entry_resets_and_saves()
    {
        var (session, clock) = NewSession();
        var changes = 0;
        var ticker = new FakeTicker();
        var vm = new ReminderViewModel(session, clock, ticker, new FakeAlarmService(), () => changes++, firstIntervalMinutes: 15, recurringIntervalMinutes: 30);
        clock.Now = T0.AddMinutes(16);
        ticker.Fire();

        vm.AcknowledgeCommand.Execute(null);

        // Journal[0] is the automatic "Einsatz begonnen" entry; acknowledging must add
        // exactly one more.
        var entry = Assert.Single(session.Incident.Journal, e => e.Text == "Rückmeldung an ILS");
        Assert.Equal(EtbDirection.Outgoing, entry.Direction);
        Assert.Equal("ILS", entry.To);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
        Assert.False(vm.IsDue);                 // re-anchored to now
        Assert.Equal(1, changes);               // save triggered once
    }

    [Fact]
    public void Dispose_unsubscribes_from_ticker()
    {
        var (session, clock) = NewSession();
        var ticker = new FakeTicker();
        var vm = new ReminderViewModel(session, clock, ticker, new FakeAlarmService(), () => { }, firstIntervalMinutes: 15, recurringIntervalMinutes: 30);
        Assert.Equal(1, ticker.SubscriberCount);

        vm.Dispose();

        Assert.Equal(0, ticker.SubscriberCount);
    }

    [Fact]
    public void Becoming_due_plays_the_spoken_cue_once_per_cycle()
    {
        var (session, clock) = NewSession();
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = new ReminderViewModel(session, clock, ticker, alarm, () => { }, firstIntervalMinutes: 15, recurringIntervalMinutes: 30);

        clock.Now = T0.AddMinutes(15);
        ticker.Fire();
        ticker.Fire(); // still due — must not re-announce within the same cycle

        Assert.Equal(new[] { AlarmSound.IlsReminderDue }, alarm.Played);

        // Acknowledging opens a fresh cycle; the next due re-announces (now on the 30-min cadence).
        vm.AcknowledgeCommand.Execute(null);
        clock.Now = T0.AddMinutes(15 + 30);
        ticker.Fire();

        Assert.Equal(new[] { AlarmSound.IlsReminderDue, AlarmSound.IlsReminderDue }, alarm.Played);
    }

    [Fact]
    public void Follow_up_cycle_uses_the_recurring_interval_after_acknowledge()
    {
        var (session, clock) = NewSession();
        var ticker = new FakeTicker();
        var vm = new ReminderViewModel(session, clock, ticker, new FakeAlarmService(), () => { }, firstIntervalMinutes: 15, recurringIntervalMinutes: 30);

        clock.Now = T0.AddMinutes(15);
        ticker.Fire();
        vm.AcknowledgeCommand.Execute(null); // switch to the recurring cadence

        // 29 min after ack: not yet due — the recurring interval is 30, not the first 15.
        clock.Now = T0.AddMinutes(15 + 29);
        ticker.Fire();
        Assert.False(vm.IsDue);

        clock.Now = T0.AddMinutes(15 + 30);
        ticker.Fire();
        Assert.True(vm.IsDue);
    }
}
