using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;

using Feuerwehr.Sync;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// The "Rückmeldung an ILS" reminder. It is autonomous: it starts running the moment the workspace
/// is built for a live incident (there is no manual start/stop — reporting back to the ILS is an
/// ongoing obligation for the whole incident), alerts first after the configured "Erstmeldung nach"
/// interval and then cyclically on the follow-up "Intervall", speaking a cue and offering ERLEDIGT
/// each time it falls due. The intervals come from the Stammdaten settings.
/// </summary>
public sealed partial class ReminderViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly IAlarmService _alarm;
    private readonly Action _onChanged;
    private readonly ReminderTimer _timer = new();
    private readonly IDisposable _subscription;

    // The spoken cue fires once when a cycle falls due; this guards against re-announcing it on
    // every subsequent tick until the next Acknowledge opens a fresh cycle.
    private bool _dueAnnounced;

    public ReminderViewModel(
        IIncidentSession session, IClock clock, ITicker ticker, IAlarmService alarm, Action onChanged,
        int firstIntervalMinutes, int recurringIntervalMinutes)
    {
        _session = session;
        _clock = clock;
        _alarm = alarm;
        _onChanged = onChanged;
        // Autonomous: the reminder runs for the whole incident, no manual start required.
        _timer.Start(_clock, firstIntervalMinutes, recurringIntervalMinutes);
        _subscription = ticker.Subscribe(OnTick);
    }

    public bool IsRunning => _timer.IsRunning;
    public bool IsDue => _timer.IsDue(_clock.Now);

    public string RemainingDisplay
    {
        get
        {
            if (_timer.IsDue(_clock.Now))
                return "fällig";
            var remaining = _timer.Remaining(_clock.Now);
            return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
    }

    private void OnTick()
    {
        // Announce the moment the cycle crosses due, exactly once per cycle.
        if (_timer.IsDue(_clock.Now) && !_dueAnnounced)
        {
            _alarm.Play(AlarmSound.IlsReminderDue);
            _dueAnnounced = true;
        }

        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(IsDue));
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }

    private bool CanAcknowledge => _timer.IsDue(_clock.Now);

    [RelayCommand(CanExecute = nameof(CanAcknowledge))]
    private void Acknowledge()
    {
        _timer.Acknowledge(_clock);
        _dueAnnounced = false;
        _session.AddJournalEntry(
            EtbDirection.Outgoing, "Rückmeldung an ILS", from: null, to: "ILS");
        _onChanged();
        OnPropertyChanged(nameof(IsDue));
        OnPropertyChanged(nameof(RemainingDisplay));
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _subscription.Dispose();
}
