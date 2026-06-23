using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.AppLogic.Tests;

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

    private static (IncidentSession session, FixedClock clock) NewSession()
    {
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());
        return (session, clock);
    }

    [Fact]
    public void Default_interval_is_15_and_not_running()
    {
        var (session, clock) = NewSession();
        var vm = new ReminderViewModel(session, clock, new FakeTicker(), () => { });

        Assert.Equal(15, vm.IntervalMinutes);
        Assert.False(vm.IsRunning);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.False(vm.AcknowledgeCommand.CanExecute(null));
    }

    [Fact]
    public void Start_runs_and_shows_countdown()
    {
        var (session, clock) = NewSession();
        var vm = new ReminderViewModel(session, clock, new FakeTicker(), () => { }) { IntervalMinutes = 15 };

        vm.StartCommand.Execute(null);

        Assert.True(vm.IsRunning);
        Assert.False(vm.IsDue);
        Assert.Equal("15:00", vm.RemainingDisplay);
    }

    [Fact]
    public void Tick_past_interval_makes_it_due()
    {
        var (session, clock) = NewSession();
        var ticker = new FakeTicker();
        var vm = new ReminderViewModel(session, clock, ticker, () => { }) { IntervalMinutes = 15 };
        vm.StartCommand.Execute(null);

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
        var vm = new ReminderViewModel(session, clock, ticker, () => changes++) { IntervalMinutes = 15 };
        vm.StartCommand.Execute(null);
        clock.Now = T0.AddMinutes(16);
        ticker.Fire();

        vm.AcknowledgeCommand.Execute(null);

        var entry = Assert.Single(session.Incident.Journal);
        Assert.Equal(EtbDirection.Outgoing, entry.Direction);
        Assert.Equal("Rückmeldung an ILS", entry.Text);
        Assert.Equal("ILS", entry.To);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
        Assert.False(vm.IsDue);                 // re-anchored to now
        Assert.Equal(1, changes);               // save triggered once
    }

    [Fact]
    public void Stop_disables_running_state()
    {
        var (session, clock) = NewSession();
        var vm = new ReminderViewModel(session, clock, new FakeTicker(), () => { });
        vm.StartCommand.Execute(null);

        vm.StopCommand.Execute(null);

        Assert.False(vm.IsRunning);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void Dispose_unsubscribes_from_ticker()
    {
        var (session, clock) = NewSession();
        var ticker = new FakeTicker();
        var vm = new ReminderViewModel(session, clock, ticker, () => { });
        Assert.Equal(1, ticker.SubscriberCount);

        vm.Dispose();

        Assert.Equal(0, ticker.SubscriberCount);
    }
}
