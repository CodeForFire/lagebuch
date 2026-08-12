using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;

using Feuerwehr.Sync;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class ReminderViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly ReminderTimer _timer = new();
    private readonly IDisposable _subscription;

    public ReminderViewModel(IIncidentSession session, IClock clock, ITicker ticker, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _subscription = ticker.Subscribe(OnTick);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int _intervalMinutes = 15;

    public bool IsRunning => _timer.IsRunning;
    public bool IsDue => _timer.IsDue(_clock.Now);

    public string RemainingDisplay
    {
        get
        {
            if (!_timer.IsRunning)
                return "—";
            if (_timer.IsDue(_clock.Now))
                return "fällig";
            var remaining = _timer.Remaining(_clock.Now);
            return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
    }

    private void OnTick()
    {
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(IsDue));
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDue));
        OnPropertyChanged(nameof(RemainingDisplay));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart => !_timer.IsRunning && IntervalMinutes > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        _timer.Start(_clock, IntervalMinutes);
        RefreshState();
    }

    private bool CanStop => _timer.IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _timer.Stop();
        RefreshState();
    }

    private bool CanAcknowledge => _timer.IsDue(_clock.Now);

    [RelayCommand(CanExecute = nameof(CanAcknowledge))]
    private void Acknowledge()
    {
        _timer.Acknowledge(_clock);
        _session.AddJournalEntry(
            EtbDirection.Outgoing, "Rückmeldung an ILS", from: null, to: "ILS");
        _onChanged();
        RefreshState();
    }

    public void Dispose() => _subscription.Dispose();
}
