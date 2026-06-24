using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// One row of the Atemschutzüberwachung table. Live values (elapsed/remaining/alarm) are
/// recomputed on each ticker tick via <see cref="Refresh"/>. Per-row actions are supplied as
/// callbacks by the owning <see cref="ScbaViewModel"/> so XAML binds simple parameterless commands.
/// </summary>
public sealed partial class ScbaTruppRow : ObservableObject
{
    private readonly AtemschutzTrupp _trupp;
    private readonly IClock _clock;
    private readonly bool _isReadOnly;
    private readonly Action<int> _onRecordPressure;
    private readonly Action _onMarkReturned;

    public ScbaTruppRow(
        AtemschutzTrupp trupp, IClock clock, bool isReadOnly,
        Action<int> onRecordPressure, Action onMarkReturned)
    {
        _trupp = trupp;
        _clock = clock;
        _isReadOnly = isReadOnly;
        _onRecordPressure = onRecordPressure;
        _onMarkReturned = onMarkReturned;
        _pressureInput = trupp.LatestPressure;
    }

    public Guid Id => _trupp.Id;
    public string Designation => _trupp.Designation;
    public string Members => _trupp.Members;
    public string? CallSign => _trupp.CallSign;
    public string EntryTimeDisplay => _trupp.EntryTime.ToString("HH:mm");
    public int EntryPressure => _trupp.EntryPressure;
    public int LatestPressure => _trupp.LatestPressure;
    public bool IsActive => _trupp.IsActive;
    public bool IsAlarm => _trupp.IsAlarm(_clock.Now);

    public string ElapsedDisplay
    {
        get
        {
            var end = _trupp.ExitTime ?? _clock.Now;
            return Clock(end - _trupp.EntryTime);
        }
    }

    public string RemainingDisplay
    {
        get
        {
            if (!IsActive)
                return "—";
            var remaining = _trupp.Remaining(_clock.Now);
            return remaining <= TimeSpan.Zero ? "überzogen" : Clock(remaining);
        }
    }

    public string StatusDisplay => !IsActive ? "Zurück" : IsAlarm ? "ALARM" : "Im Einsatz";

    [ObservableProperty]
    private int _pressureInput;

    private bool CanAct => !_isReadOnly && IsActive;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void RecordPressure() => _onRecordPressure(PressureInput);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void MarkReturned() => _onMarkReturned();

    public void Refresh()
    {
        OnPropertyChanged(nameof(ElapsedDisplay));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(LatestPressure));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsAlarm));
        OnPropertyChanged(nameof(StatusDisplay));
        RecordPressureCommand.NotifyCanExecuteChanged();
        MarkReturnedCommand.NotifyCanExecuteChanged();
    }

    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
}

public sealed partial class ScbaViewModel : ObservableObject, IDisposable
{
    private readonly IncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IDisposable? _subscription;
    private readonly HashSet<Guid> _alarmLogged = new();

    public ScbaViewModel(IncidentSession session, MasterDataSet masterData, IClock clock, ITicker ticker, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        TruppTypeOptions = masterData.TruppTypes;
        CallSignOptions = masterData.RadioCallSigns;
        Trupps = new ObservableCollection<ScbaTruppRow>(session.Incident.ScbaTrupps.Select(CreateRow));

        // Suppress re-logging alarms for trupps already alarming when the incident is reopened.
        foreach (var t in session.Incident.ScbaTrupps)
            if (t.IsActive && t.IsAlarm(_clock.Now))
                _alarmLogged.Add(t.Id);

        // A closed incident is historical: no live ticking, no auto-logging (it cannot mutate).
        _subscription = IsReadOnly ? null : ticker.Subscribe(OnTick);
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<string> TruppTypeOptions { get; }
    public IReadOnlyList<string> CallSignOptions { get; }
    public ObservableCollection<ScbaTruppRow> Trupps { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newDesignation = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newMembers = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private int _newEntryPressure = 300;

    [ObservableProperty]
    private int _newMaxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes;

    [ObservableProperty]
    private int _newReturnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar;

    private bool CanAddTrupp =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewDesignation)
        && !string.IsNullOrWhiteSpace(NewMembers) && NewEntryPressure > 0;

    [RelayCommand(CanExecute = nameof(CanAddTrupp))]
    private void AddTrupp()
    {
        var trupp = _session.Incident.AddScbaTrupp(
            _clock, NewDesignation, NewMembers, NewEntryPressure, NewCallSign,
            task: null, maxDurationMinutes: NewMaxDurationMinutes, returnPressureBar: NewReturnPressureBar);
        Trupps.Add(CreateRow(trupp));
        _session.Incident.AddJournalEntry(
            _clock, _session.Operator!, EtbDirection.Internal,
            $"Atemschutztrupp {trupp.Designation} eingesetzt: {trupp.Members}, Einstiegsdruck {trupp.EntryPressure} bar",
            from: null, to: trupp.CallSign);

        NewDesignation = string.Empty;
        NewMembers = string.Empty;
        NewCallSign = null;
        NewEntryPressure = 300;
        NewMaxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes;
        NewReturnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar;
        _onChanged();
    }

    private ScbaTruppRow CreateRow(AtemschutzTrupp trupp) =>
        new(trupp, _clock, IsReadOnly,
            bar => RecordPressure(trupp.Id, bar),
            () => MarkReturned(trupp.Id));

    private void RecordPressure(Guid truppId, int bar)
    {
        _session.Incident.RecordScbaPressure(_clock, truppId, bar);
        RefreshRow(truppId);
        LogNewAlarms(); // a low reading may immediately trip the Rückzugsdruck alarm
        _onChanged();
    }

    private void MarkReturned(Guid truppId)
    {
        var trupp = _session.Incident.MarkScbaReturned(_clock, truppId);
        _session.Incident.AddJournalEntry(
            _clock, _session.Operator!, EtbDirection.Internal,
            $"Atemschutztrupp {trupp.Designation} zurück", from: trupp.CallSign, to: null);
        RefreshRow(truppId);
        _onChanged();
    }

    private void RefreshRow(Guid truppId) =>
        Trupps.FirstOrDefault(r => r.Id == truppId)?.Refresh();

    private void OnTick()
    {
        foreach (var row in Trupps)
            row.Refresh();
        if (LogNewAlarms())
            _onChanged();
    }

    /// <summary>Appends one ETB entry per Trupp that has newly entered the alarm state.
    /// Returns whether anything was logged (so callers can persist). No-op when read-only.</summary>
    private bool LogNewAlarms()
    {
        if (IsReadOnly)
            return false;
        var logged = false;
        foreach (var trupp in _session.Incident.ScbaTrupps)
        {
            if (!trupp.IsActive || !trupp.IsAlarm(_clock.Now) || !_alarmLogged.Add(trupp.Id))
                continue;
            var reason = trupp.IsTimeAlarm(_clock.Now)
                ? "Einsatzzeit erreicht"
                : $"Rückzugsdruck erreicht ({trupp.LatestPressure} bar)";
            _session.Incident.AddJournalEntry(
                _clock, _session.Operator!, EtbDirection.Internal,
                $"Rückzugsalarm Atemschutz {trupp.Designation}: {reason}", from: null, to: trupp.CallSign);
            logged = true;
        }
        return logged;
    }

    public void Dispose() => _subscription?.Dispose();
}
