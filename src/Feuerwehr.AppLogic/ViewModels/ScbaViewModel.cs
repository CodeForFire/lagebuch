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
/// One row of the Atemschutzüberwachung table. A Trupp moves through three states —
/// waiting (registered, not under air), active (clock running), returned. Live values
/// (elapsed/remaining/alarm/next-control) are recomputed on each ticker tick via <see cref="Refresh"/>.
/// Per-row actions are supplied as callbacks by the owning <see cref="ScbaViewModel"/> so XAML binds
/// simple parameterless commands.
/// </summary>
public sealed partial class ScbaTruppRow : ObservableObject
{
    private readonly AtemschutzTrupp _trupp;
    private readonly IClock _clock;
    private readonly bool _isReadOnly;
    private readonly Action<int> _onStart;
    private readonly Action<int> _onRecordPressure;
    private readonly Action _onMarkReturned;

    public ScbaTruppRow(
        AtemschutzTrupp trupp, IClock clock, bool isReadOnly,
        Action<int> onStart, Action<int> onRecordPressure, Action onMarkReturned)
    {
        _trupp = trupp;
        _clock = clock;
        _isReadOnly = isReadOnly;
        _onStart = onStart;
        _onRecordPressure = onRecordPressure;
        _onMarkReturned = onMarkReturned;
        _pressureInput = trupp.LatestPressure ?? 300;
    }

    public Guid Id => _trupp.Id;
    public string Designation => _trupp.Designation;
    public string Members => _trupp.MembersDisplay;

    /// <summary>
    /// The crew one name per line. Roster names are "Lastname, Firstname", so two of them joined
    /// on a single line overflow the column and a CSA crew of three has no chance — the grid
    /// clips rather than wraps, which silently hides who is under air.
    /// </summary>
    public IReadOnlyList<string> MemberLines =>
        _trupp.Members.Select(m => m.Name).ToArray();

    /// <summary>The crew with their positions, for the row tooltip.</summary>
    public string MembersDetail =>
        string.Join("\n", _trupp.Members.Select(m => $"{m.RoleDisplay}: {m.Name}"));

    public string? CallSign => _trupp.CallSign;

    public bool IsWaiting => _trupp.IsWaiting;
    public bool IsActive => _trupp.IsActive;
    public bool IsReturned => _trupp.IsReturned;
    public bool IsAlarm => _trupp.IsAlarm(_clock.Now);
    public bool IsControlDue => _trupp.IsControlDue(_clock.Now);

    public string StartTimeDisplay => _trupp.StartTime is { } s ? s.ToString("HH:mm") : "—";
    public string? PressureDisplay => _trupp.LatestPressure is { } p ? $"{p} bar" : null;

    public string ElapsedDisplay => _trupp.HasStarted ? Clock(_trupp.Elapsed(_clock.Now)) : "—";

    public string RemainingDisplay
    {
        get
        {
            if (!_trupp.IsActive)
                return "—";
            var remaining = _trupp.Remaining(_clock.Now);
            return remaining <= TimeSpan.Zero ? "überzogen" : Clock(remaining);
        }
    }

    public string ControlRemainingDisplay
    {
        get
        {
            if (!_trupp.IsActive)
                return "—";
            var remaining = _trupp.ControlRemaining(_clock.Now);
            return remaining <= TimeSpan.Zero ? "fällig" : Clock(remaining);
        }
    }

    public string StatusDisplay => _trupp switch
    {
        { IsReturned: true } => "Zurück",
        { IsWaiting: true } => "Bereitgestellt",
        _ when IsAlarm => "ALARM",
        _ when IsControlDue => "Druckabfrage",
        _ => "Unter PA"
    };

    [ObservableProperty]
    private int _pressureInput;

    private bool CanStart => !_isReadOnly && _trupp.IsWaiting && PressureInput > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => _onStart(PressureInput);

    private bool CanRecordPressure => !_isReadOnly && _trupp.IsActive;

    [RelayCommand(CanExecute = nameof(CanRecordPressure))]
    private void RecordPressure() => _onRecordPressure(PressureInput);

    private bool CanMarkReturned => !_isReadOnly && _trupp.IsActive;

    [RelayCommand(CanExecute = nameof(CanMarkReturned))]
    private void MarkReturned() => _onMarkReturned();

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsReturned));
        OnPropertyChanged(nameof(IsAlarm));
        OnPropertyChanged(nameof(IsControlDue));
        OnPropertyChanged(nameof(StartTimeDisplay));
        OnPropertyChanged(nameof(PressureDisplay));
        OnPropertyChanged(nameof(ElapsedDisplay));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(ControlRemainingDisplay));
        OnPropertyChanged(nameof(StatusDisplay));
        StartCommand.NotifyCanExecuteChanged();
        RecordPressureCommand.NotifyCanExecuteChanged();
        MarkReturnedCommand.NotifyCanExecuteChanged();
    }

    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
}

public sealed partial class ScbaViewModel : ObservableObject, IDisposable
{
    private readonly IncidentSession _session;
    private readonly IClock _clock;
    private readonly IAlarmService _alarm;
    private readonly Action _onChanged;
    private readonly IDisposable? _subscription;
    private readonly HashSet<Guid> _alarmLogged = new();

    public ScbaViewModel(IncidentSession session, MasterDataSet masterData, IClock clock, ITicker ticker, IAlarmService alarm, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _alarm = alarm;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        TruppTypeOptions = masterData.TruppTypes;
        CallSignOptions = masterData.RadioCallSigns;
        PersonOptions = masterData.Personnel.Select(p => p.DisplayName).ToArray();
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

    /// <summary>
    /// Name suggestions for the crew boxes. Empty when no personnel roster is installed, which is
    /// the normal state on a fresh clone — the boxes stay free text either way.
    /// </summary>
    public IReadOnlyList<string> PersonOptions { get; }
    public ObservableCollection<ScbaTruppRow> Trupps { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    [NotifyPropertyChangedFor(nameof(RequiresThirdMember))]
    private string _newDesignation = string.Empty;

    /// <summary>
    /// Whether the selected Trupp type is crewed by three. Drives the visibility of the third
    /// name box, so the form matches the rule the domain enforces.
    /// </summary>
    public bool RequiresThirdMember => AtemschutzTrupp.IsChemicalTrupp(NewDesignation);

    /// <summary>
    /// Truppführer. A Trupp always has one; the crew is never a single free-text field.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newTruppfuehrer = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newTruppmann = string.Empty;

    /// <summary>Only used -- and only required -- for a CSA-Trupp.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newZweiterTruppmann = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    [ObservableProperty]
    private int _newMaxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes;

    [ObservableProperty]
    private int _newReturnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar;

    [ObservableProperty]
    private int _newControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes;

    // ----- Header reminder: the most urgent next pressure-control across all active trupps -----

    public bool HasControlReminder => !IsReadOnly && Trupps.Any(r => r.IsActive);

    private ScbaTruppRow? MostUrgentActive =>
        Trupps.Where(r => r.IsActive)
              .OrderBy(r => _session.Incident.ScbaTrupps.First(t => t.Id == r.Id).ControlRemaining(_clock.Now))
              .FirstOrDefault();

    public bool IsAnyControlDue => MostUrgentActive?.IsControlDue ?? false;

    public string NextControlDisplay
    {
        get
        {
            var urgent = MostUrgentActive;
            if (urgent is null)
                return "—";
            return IsAnyControlDue
                ? $"Druckabfrage fällig: {urgent.Designation}"
                : $"Nächste Druckabfrage: {urgent.Designation} in {urgent.ControlRemainingDisplay}";
        }
    }

    // ----- Rückzugsalarm: a trupp has hit its time limit or return pressure (life-safety) -----

    private IEnumerable<AtemschutzTrupp> AlarmingTrupps =>
        _session.Incident.ScbaTrupps.Where(t => t.IsActive && t.IsAlarm(_clock.Now));

    public bool IsAnyAlarm => AlarmingTrupps.Any();

    public string AlarmDisplay
    {
        get
        {
            var trupps = AlarmingTrupps.ToList();
            if (trupps.Count == 0)
                return "—";
            var first = $"RÜCKZUGSALARM {trupps[0].Designation}: {AlarmReason(trupps[0])}";
            return trupps.Count == 1 ? first : $"{first}  (+{trupps.Count - 1})";
        }
    }

    [ObservableProperty]
    private bool _isAlarmAcknowledged;

    private bool CanAcknowledgeAlarm => IsAnyAlarm;

    [RelayCommand(CanExecute = nameof(CanAcknowledgeAlarm))]
    private void AcknowledgeAlarm()
    {
        // Silence the sound; the visual banner stays until the trupp is back.
        _alarm.Stop();
        IsAlarmAcknowledged = true;
    }

    private string AlarmReason(AtemschutzTrupp trupp) => trupp.IsTimeAlarm(_clock.Now)
        ? "Einsatzzeit erreicht"
        : $"Rückzugsdruck erreicht ({trupp.LatestPressure} bar)";

    /// <summary>Sounds or silences the audible alarm from current state, and keeps the banner
    /// bindings fresh. A newly-alarming trupp re-arms the sound even after an earlier ack.</summary>
    private void UpdateAlarm(bool newAlarmTripped)
    {
        if (newAlarmTripped)
            IsAlarmAcknowledged = false;

        if (IsAnyAlarm && !IsAlarmAcknowledged)
            _alarm.Start();
        else if (!IsAnyAlarm)
        {
            _alarm.Stop();
            IsAlarmAcknowledged = false;
        }

        OnPropertyChanged(nameof(IsAnyAlarm));
        OnPropertyChanged(nameof(AlarmDisplay));
        AcknowledgeAlarmCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddTrupp =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewDesignation)
        && !string.IsNullOrWhiteSpace(NewTruppfuehrer) && !string.IsNullOrWhiteSpace(NewTruppmann)
        // Mirrors the domain cardinality rule so an incomplete CSA-Trupp disables the button
        // rather than throwing on click.
        && (!RequiresThirdMember || !string.IsNullOrWhiteSpace(NewZweiterTruppmann));

    private IReadOnlyList<TruppMember> BuildCrew() =>
        TruppMember.Crew(NewTruppfuehrer, NewTruppmann, RequiresThirdMember ? NewZweiterTruppmann : null);

    [RelayCommand(CanExecute = nameof(CanAddTrupp))]
    private void AddTrupp()
    {
        var trupp = _session.Incident.AddScbaTrupp(
            _clock, NewDesignation, BuildCrew(), NewCallSign,
            task: null, maxDurationMinutes: NewMaxDurationMinutes, returnPressureBar: NewReturnPressureBar,
            pressureControlIntervalMinutes: NewControlIntervalMinutes);
        Trupps.Add(CreateRow(trupp));
        _session.Incident.AddJournalEntry(
            _clock, _session.Operator!, EtbDirection.System,
            $"Atemschutztrupp {trupp.Designation} bereitgestellt: {trupp.MembersDisplay}",
            from: null, to: trupp.CallSign);

        NewDesignation = string.Empty;
        NewTruppfuehrer = string.Empty;
        NewTruppmann = string.Empty;
        NewZweiterTruppmann = string.Empty;
        NewCallSign = null;
        NewMaxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes;
        NewReturnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar;
        NewControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes;
        RefreshHeader();
        _onChanged();
    }

    private ScbaTruppRow CreateRow(AtemschutzTrupp trupp) =>
        new(trupp, _clock, IsReadOnly,
            pressure => Start(trupp.Id, pressure),
            bar => RecordPressure(trupp.Id, bar),
            () => MarkReturned(trupp.Id));

    private void Start(Guid truppId, int startPressure)
    {
        var trupp = _session.Incident.StartScbaTrupp(_clock, truppId, startPressure);
        _session.Incident.AddJournalEntry(
            _clock, _session.Operator!, EtbDirection.System,
            $"Atemschutztrupp {trupp.Designation} unter PA: Einstiegsdruck {startPressure} bar",
            from: null, to: trupp.CallSign);
        RefreshRow(truppId);
        RefreshHeader();
        _onChanged();
    }

    private void RecordPressure(Guid truppId, int bar)
    {
        var trupp = _session.Incident.RecordScbaPressure(_clock, truppId, bar);
        _session.Incident.AddJournalEntry(
            _clock, _session.Operator!, EtbDirection.System,
            $"Druckkontrolle Atemschutz {trupp.Designation}: {bar} bar", from: trupp.CallSign, to: null);
        RefreshRow(truppId);
        var tripped = LogNewAlarms(); // a low reading may immediately trip the Rückzugsdruck alarm
        UpdateAlarm(tripped);
        RefreshHeader();
        _onChanged();
    }

    private void MarkReturned(Guid truppId)
    {
        var trupp = _session.Incident.MarkScbaReturned(_clock, truppId);
        _session.Incident.AddJournalEntry(
            _clock, _session.Operator!, EtbDirection.System,
            $"Atemschutztrupp {trupp.Designation} zurück", from: trupp.CallSign, to: null);
        RefreshRow(truppId);
        UpdateAlarm(newAlarmTripped: false); // a returned trupp may clear the last alarm
        RefreshHeader();
        _onChanged();
    }

    private void RefreshRow(Guid truppId) =>
        Trupps.FirstOrDefault(r => r.Id == truppId)?.Refresh();

    private void RefreshHeader()
    {
        OnPropertyChanged(nameof(HasControlReminder));
        OnPropertyChanged(nameof(IsAnyControlDue));
        OnPropertyChanged(nameof(NextControlDisplay));
    }

    private void OnTick()
    {
        foreach (var row in Trupps)
            row.Refresh();
        RefreshHeader();
        var tripped = LogNewAlarms();
        UpdateAlarm(tripped);
        if (tripped)
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
            var reason = AlarmReason(trupp);
            _session.Incident.AddJournalEntry(
                _clock, _session.Operator!, EtbDirection.System,
                $"Rückzugsalarm Atemschutz {trupp.Designation}: {reason}", from: null, to: trupp.CallSign);
            logged = true;
        }
        return logged;
    }

    public void Dispose()
    {
        _alarm.Stop();
        _subscription?.Dispose();
    }
}
