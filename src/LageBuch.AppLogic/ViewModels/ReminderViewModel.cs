using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;

using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

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

    // The key under which this reminder's state is persisted on the incident, so it survives a
    // close+reopen or a crash instead of restarting a fresh cycle.
    private const string TimerKey = "ils-reminder";

    // The spoken cue repeats while a cycle sits unacknowledged, so it stays insistent instead of
    // being said once and forgotten; this tracks when it last played so OnTick only re-plays once
    // RepeatInterval has passed, and Acknowledge clears it to announce immediately on the next cycle.
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(60);
    private DateTimeOffset? _lastAnnouncedAt;

    public ReminderViewModel(
        IIncidentSession session, IClock clock, ITicker ticker, IAlarmService alarm, Action onChanged,
        int firstIntervalMinutes, int recurringIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        _session = session;
        _clock = clock;
        _alarm = alarm;
        _onChanged = onChanged;

        // Autonomous: the reminder runs for the whole incident, no manual start required. Recover the
        // running cycle from persisted state after a reopen/crash; otherwise start fresh and persist
        // the anchor so a later reopen resumes where this left off rather than restarting at 15:00.
        var saved = _session.Incident.FindTimer(TimerKey);
        if (saved is { IsRunning: true })
        {
            _timer.Resume(saved.CycleAnchor, saved.IntervalMinutes, saved.RecurringIntervalMinutes);
        }
        else
        {
            _timer.Start(_clock, firstIntervalMinutes, recurringIntervalMinutes);
            PersistTimer();
        }

        _subscription = ticker.Subscribe(OnTick);
    }

    private void PersistTimer() =>
        _session.UpsertTimer(TimerKey, _timer.CycleAnchor, _timer.IntervalMinutes, _timer.RecurringIntervalMinutes, _timer.IsRunning);

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
        // Announce the moment the cycle crosses due, then keep repeating on RepeatInterval until
        // acknowledged, so the reminder stays insistent instead of being said once and forgotten.
        if (_timer.IsDue(_clock.Now) &&
            (_lastAnnouncedAt is null || _clock.Now - _lastAnnouncedAt >= RepeatInterval))
        {
            _alarm.Play(AlarmSound.IlsReminderDue);
            _lastAnnouncedAt = _clock.Now;
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
        _lastAnnouncedAt = null;
        PersistTimer(); // durable anchor for the new (recurring) cycle
        // "Von" is us — the logged-in operator's call sign (e.g. the ELW's Funkrufname).
        _session.AddJournalEntry(
            EtbDirection.Outgoing, "Rückmeldung an ILS", from: _session.Operator?.CallSign, to: "ILS");
        _onChanged();
        OnPropertyChanged(nameof(IsDue));
        OnPropertyChanged(nameof(RemainingDisplay));
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _subscription.Dispose();
}
