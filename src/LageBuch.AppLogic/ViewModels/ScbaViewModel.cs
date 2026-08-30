using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

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
    private readonly Action _onStart;
    private readonly Action<int> _onRecordPressure;
    private readonly Action _onWithdraw;
    private readonly Action _onMarkRemoved;

    public ScbaTruppRow(
        AtemschutzTrupp trupp, IClock clock, bool isReadOnly,
        Action onStart, Action<int> onRecordPressure, Action onWithdraw, Action onMarkRemoved)
    {
        ArgumentNullException.ThrowIfNull(trupp);
        _trupp = trupp;
        _clock = clock;
        _isReadOnly = isReadOnly;
        _onStart = onStart;
        _onRecordPressure = onRecordPressure;
        _onWithdraw = onWithdraw;
        _onMarkRemoved = onMarkRemoved;
        _pressureInput = trupp.LatestPressure ?? 300;
    }

    public Guid Id => _trupp.Id;
    public int TruppNumber => _trupp.TruppNumber;
    public string Designation => _trupp.Designation;
    public string DisplayName => _trupp.DisplayName;
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
    public bool IsWithdrawing => _trupp.IsWithdrawing;
    public bool IsReturned => _trupp.IsReturned;
    public bool IsAlarm => _trupp.IsAlarm(_clock.Now);
    public bool IsControlDue => _trupp.IsControlDue(_clock.Now);

    public string StartTimeDisplay => _trupp.StartTime is { } s ? s.ToString("HH:mm", CultureInfo.InvariantCulture) : "—";
    public string? PressureDisplay => _trupp.LatestPressure is { } p ? $"{p} bar" : null;

    public string ElapsedDisplay => _trupp.HasStarted ? Clock(_trupp.Elapsed(_clock.Now)) : "—";

    public string RemainingDisplay
    {
        get
        {
            if (!(_trupp.IsActive || _trupp.IsWithdrawing))
                return "—";
            var remaining = _trupp.Remaining(_clock.Now);
            return remaining <= TimeSpan.Zero ? "überzogen" : Clock(remaining);
        }
    }

    public string ControlRemainingDisplay
    {
        get
        {
            if (!(_trupp.IsActive || _trupp.IsWithdrawing))
                return "—";
            var remaining = _trupp.ControlRemaining(_clock.Now);
            return remaining <= TimeSpan.Zero ? "fällig" : Clock(remaining);
        }
    }

    public string StatusDisplay => _trupp switch
    {
        { IsReturned: true } => "Abgenommen",
        { IsWaiting: true } => "Bereitgestellt",
        _ when IsAlarm => "ALARM",
        { IsWithdrawing: true } => "Rückzug",
        _ when IsControlDue => "Druckabfrage",
        _ => "Im Einsatz"
    };

    [ObservableProperty]
    private int _pressureInput;

    private bool CanStart => !_isReadOnly && _trupp.IsWaiting;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => _onStart();

    private bool CanRecordPressure => !_isReadOnly && (_trupp.IsActive || _trupp.IsWithdrawing);

    [RelayCommand(CanExecute = nameof(CanRecordPressure))]
    private void RecordPressure() => _onRecordPressure(PressureInput);

    private bool CanWithdraw => !_isReadOnly && _trupp.IsActive;

    [RelayCommand(CanExecute = nameof(CanWithdraw))]
    private void Withdraw() => _onWithdraw();

    private bool CanMarkRemoved => !_isReadOnly && _trupp.IsWithdrawing;

    [RelayCommand(CanExecute = nameof(CanMarkRemoved))]
    private void MarkRemoved() => _onMarkRemoved();

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsWithdrawing));
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
        WithdrawCommand.NotifyCanExecuteChanged();
        MarkRemovedCommand.NotifyCanExecuteChanged();
    }

    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
}

public sealed partial class ScbaViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly IAlarmService _alarm;
    private readonly Action _onChanged;
    private readonly IDisposable? _subscription;
    private readonly HashSet<Guid> _alarmLogged = new();

    // Druckabfrage due-crossings that have already sounded. Unlike _alarmLogged (a one-way state
    // that only ever grows until the Trupp returns), control-due toggles on and off every Abfrage-
    // Intervall, so an id is removed once no longer due, letting the next crossing sound again.
    private readonly HashSet<Guid> _controlDueAnnounced = new();

    // Rückzugsalarm speaks instead of sounding a looping siren (#81), repeating on this cadence
    // while unacknowledged so it stays insistent -- mirrors ReminderViewModel's ILS cue exactly,
    // just at 15s instead of 60s given the life-safety stakes. Reset to null by AcknowledgeAlarm
    // (so the next cycle announces immediately) and by a newly-tripped alarm (so a second Trupp
    // alarming after an ack is heard right away rather than waiting out the window).
    private static readonly TimeSpan RetreatRepeatInterval = TimeSpan.FromSeconds(15);
    private DateTimeOffset? _lastAlarmAnnouncedAt;

    private readonly IncidentSettings _settings;

    // True once the user has hand-edited the Einsatzzeit; after that a Trupp-type switch must not
    // overwrite it. Programmatic sets (default application, form reset) are fenced by _applyingDefault
    // so they do not count as a user edit.
    private bool _maxDurationUserEdited;

    // Same idea for the Abfrage-Intervall, which otherwise defaults to a third of the Einsatzzeit
    // (#78) -- a hand-typed interval must survive a later Einsatzzeit change.
    private bool _controlIntervalUserEdited;

    // Same idea for the Truppnummer, which otherwise auto-suggests the next free number: a hand
    // edit must not be clobbered when another device's registration refreshes this device's rows.
    private bool _truppNumberUserEdited;

    private bool _applyingDefault;

    public ScbaViewModel(IIncidentSession session, MasterDataSet masterData, IClock clock, ITicker ticker, IAlarmService alarm, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(ticker);
        _session = session;
        _clock = clock;
        _alarm = alarm;
        _onChanged = onChanged;
        _settings = masterData.Settings;
        // Seed the add-Trupp form defaults from the configured settings (empty designation => AGT).
        // Direct field writes so no OnChanged fires and the fields do not read as user-edited.
        _newMaxDurationMinutes = _settings.AgtMaxDurationMinutes;
        _newReturnPressureBar = _settings.ReturnPressureBar;
        _newEntryPressure = 300;
        _newTruppNumber = session.Incident.NextFreeScbaTruppNumber();
        IsReadOnly = session.IsReadOnly;
        TruppTypeOptions = masterData.TruppTypes;
        CallSignOptions = masterData.RadioCallSigns;
        PersonOptions = masterData.Personnel.Select(p => p.DisplayName).ToArray();
        Trupps = new ObservableCollection<ScbaTruppRow>(session.Incident.ScbaTrupps.Select(CreateRow));
        _session.Changed += RefreshTrupps;
        // The property setter path (below) is what marks an interval as user-edited, so the
        // initial derivation from _newMaxDurationMinutes must go through it once here too.
        ApplyDefaultControlInterval();

        // Suppress re-logging alarms for trupps already alarming when the incident is reopened.
        foreach (var t in session.Incident.ScbaTrupps)
            if ((t.IsActive || t.IsWithdrawing) && t.IsAlarm(_clock.Now))
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
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private int _newTruppNumber;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private int _newEntryPressure;

    [ObservableProperty]
    private int _newMaxDurationMinutes;

    [ObservableProperty]
    private int _newReturnPressureBar;

    [ObservableProperty]
    private int _newControlIntervalMinutes;

    partial void OnNewTruppNumberChanged(int value)
    {
        if (!_applyingDefault)
            _truppNumberUserEdited = true;
    }

    partial void OnNewMaxDurationMinutesChanged(int value)
    {
        if (!_applyingDefault)
            _maxDurationUserEdited = true;
        // The Abfrage-Intervall tracks the Einsatzzeit (a third of it) unless separately overridden,
        // whether this change came from the user or from ApplyDefaultMaxDuration below.
        if (!_controlIntervalUserEdited)
            ApplyDefaultControlInterval();
    }

    partial void OnNewControlIntervalMinutesChanged(int value)
    {
        if (!_applyingDefault)
            _controlIntervalUserEdited = true;
    }

    // Switching the Trupp type re-suggests its Einsatzzeit (CSA is shorter, LPA is longer than an
    // AGT), but only while the user has not overridden the field — a hand-typed value survives.
    partial void OnNewDesignationChanged(string value)
    {
        if (!_maxDurationUserEdited)
            ApplyDefaultMaxDuration();
    }

    private void ApplyDefaultMaxDuration()
    {
        var previous = _applyingDefault;
        _applyingDefault = true;
        NewMaxDurationMinutes =
            AtemschutzTrupp.IsChemicalTrupp(NewDesignation) ? _settings.CsaMaxDurationMinutes
            : AtemschutzTrupp.IsLpaTrupp(NewDesignation) ? _settings.LpaMaxDurationMinutes
            : _settings.AgtMaxDurationMinutes;
        _applyingDefault = previous;
        // Called explicitly rather than left to OnNewMaxDurationMinutesChanged's cascade: the
        // generated property setter is a no-op when the value doesn't actually change (e.g. the
        // AGT default reapplied on form reset), which would otherwise leave a stale user-edited
        // Abfrage-Intervall in place.
        if (!_controlIntervalUserEdited)
            ApplyDefaultControlInterval();
    }

    // Abfrage-Intervall defaults to a third of the Einsatzzeit -- frequent enough to catch a fast
    // drop while not paging the operator every minute. At least 1 so a very short Einsatzzeit
    // never derives a zero/negative interval.
    private void ApplyDefaultControlInterval()
    {
        var previous = _applyingDefault;
        _applyingDefault = true;
        NewControlIntervalMinutes = Math.Max(1, NewMaxDurationMinutes / 3);
        _applyingDefault = previous;
    }

    // ----- Header reminder: the most urgent next pressure-control across all active trupps -----

    // A Rückzug crew is still under air and still needs pressure checks, so the header reminder
    // and Rückzugsalarm both stay live through Rückzug, not just Im Einsatz.
    public bool HasControlReminder => !IsReadOnly && Trupps.Any(r => r.IsActive || r.IsWithdrawing);

    private ScbaTruppRow? MostUrgentActive =>
        Trupps.Where(r => r.IsActive || r.IsWithdrawing)
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
                ? $"Druckabfrage fällig: {urgent.DisplayName}"
                : $"Nächste Druckabfrage: {urgent.DisplayName} in {urgent.ControlRemainingDisplay}";
        }
    }

    // ----- Rückzugsalarm: a trupp has hit its time limit or return pressure (life-safety) -----

    private IEnumerable<AtemschutzTrupp> AlarmingTrupps =>
        _session.Incident.ScbaTrupps.Where(t => (t.IsActive || t.IsWithdrawing) && t.IsAlarm(_clock.Now));

    public bool IsAnyAlarm => AlarmingTrupps.Any();

    public string AlarmDisplay
    {
        get
        {
            var trupps = AlarmingTrupps.ToList();
            if (trupps.Count == 0)
                return "—";
            var first = $"RÜCKZUGSALARM {trupps[0].DisplayName}: {AlarmReason(trupps[0])}";
            return trupps.Count == 1 ? first : $"{first}  (+{trupps.Count - 1})";
        }
    }

    [ObservableProperty]
    private bool _isAlarmAcknowledged;

    private bool CanAcknowledgeAlarm => IsAnyAlarm;

    [RelayCommand(CanExecute = nameof(CanAcknowledgeAlarm))]
    private void AcknowledgeAlarm()
    {
        // Silences the repeat cadence; the visual banner stays until the trupp is back.
        _lastAlarmAnnouncedAt = null;
        IsAlarmAcknowledged = true;
    }

    private string AlarmReason(AtemschutzTrupp trupp) => trupp.IsTimeAlarm(_clock.Now)
        ? "Einsatzzeit erreicht"
        : $"Rückzugsdruck erreicht ({trupp.LatestPressure} bar)";

    /// <summary>Speaks the Rückzugsalarm cue on its repeat cadence while unacknowledged (#81), and
    /// keeps the banner bindings fresh. A newly-alarming trupp re-arms the cue even after an
    /// earlier ack.</summary>
    private void UpdateAlarm(bool newAlarmTripped)
    {
        if (newAlarmTripped)
            IsAlarmAcknowledged = false;

        if (IsAnyAlarm && !IsAlarmAcknowledged &&
            (_lastAlarmAnnouncedAt is null || _clock.Now - _lastAlarmAnnouncedAt >= RetreatRepeatInterval))
        {
            _alarm.Play(AlarmSound.RetreatAlarm);
            _lastAlarmAnnouncedAt = _clock.Now;
        }
        else if (!IsAnyAlarm)
        {
            _lastAlarmAnnouncedAt = null;
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
        && (!RequiresThirdMember || !string.IsNullOrWhiteSpace(NewZweiterTruppmann))
        && NewTruppNumber > 0 && NewEntryPressure > 0;

    private IReadOnlyList<TruppMember> BuildCrew() =>
        TruppMember.Crew(NewTruppfuehrer, NewTruppmann, RequiresThirdMember ? NewZweiterTruppmann : null);

    [RelayCommand(CanExecute = nameof(CanAddTrupp))]
    private void AddTrupp()
    {
        // Compose the ETB line from the inputs, not from a return value — the mutation is
        // fire-and-forget, and the row itself is rendered by RefreshTrupps on the Changed event.
        var crew = BuildCrew();
        var membersDisplay = string.Join(" / ", crew.Select(m => m.Name));
        var designation = NewDesignation;
        var callSign = NewCallSign;
        var truppNumber = NewTruppNumber;
        var entryPressure = NewEntryPressure;
        var displayName = AtemschutzTrupp.FormatDisplayName(truppNumber, designation);
        _session.AddScbaTrupp(designation, crew, entryPressure, truppNumber, callSign,
            task: null, maxDurationMinutes: NewMaxDurationMinutes, returnPressureBar: NewReturnPressureBar,
            pressureControlIntervalMinutes: NewControlIntervalMinutes);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"{displayName} bereitgestellt: {membersDisplay}, Einstiegsdruck {entryPressure} bar",
            from: callSign, to: null);

        _maxDurationUserEdited = false;
        _controlIntervalUserEdited = false;
        NewDesignation = string.Empty;
        NewTruppfuehrer = string.Empty;
        NewTruppmann = string.Empty;
        NewZweiterTruppmann = string.Empty;
        NewCallSign = null;
        NewEntryPressure = 300;
        NewReturnPressureBar = _settings.ReturnPressureBar;
        // Guarded like RefreshTrupps' own re-suggestion below: this sets up the *next* Trupp's
        // auto-suggested number and must not itself read back as a user edit.
        _truppNumberUserEdited = false;
        var previousApplyingDefault = _applyingDefault;
        _applyingDefault = true;
        NewTruppNumber = _session.Incident.NextFreeScbaTruppNumber();
        _applyingDefault = previousApplyingDefault;
        ApplyDefaultMaxDuration(); // empty designation => AGT default; also re-derives the interval
        RefreshHeader();
        _onChanged();
    }

    private ScbaTruppRow CreateRow(AtemschutzTrupp trupp) =>
        new(trupp, _clock, IsReadOnly,
            () => Start(trupp.Id),
            bar => RecordPressure(trupp.Id, bar),
            () => Withdraw(trupp.Id),
            () => MarkRemoved(trupp.Id));

    // Display name/call-sign for the ETB line are read from the current snapshot before mutating
    // (they don't change once registered) — so this works whether the trupp lives in a local
    // aggregate or a host snapshot.
    private (string DisplayName, string? CallSign) TruppLabel(Guid truppId)
    {
        var trupp = _session.Incident.ScbaTrupps.First(t => t.Id == truppId);
        return (trupp.DisplayName, trupp.CallSign);
    }

    private void Start(Guid truppId)
    {
        var (displayName, callSign) = TruppLabel(truppId);
        _session.StartScbaTrupp(truppId);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"{displayName} im Einsatz", from: callSign, to: null);
        RefreshHeader();
        _onChanged();
    }

    private void RecordPressure(Guid truppId, int bar)
    {
        var (displayName, callSign) = TruppLabel(truppId);
        _session.RecordScbaPressure(truppId, bar);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"Druckkontrolle {displayName}: {bar} bar", from: callSign, to: null);
        var tripped = LogNewAlarms(); // a low reading may immediately trip the Rückzugsdruck alarm
        UpdateAlarm(tripped);
        RefreshHeader();
        _onChanged();
    }

    private void Withdraw(Guid truppId)
    {
        var (displayName, callSign) = TruppLabel(truppId);
        _session.WithdrawScbaTrupp(truppId);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"{displayName} Rückzug", from: callSign, to: null);
        RefreshHeader();
        _onChanged();
    }

    private void MarkRemoved(Guid truppId)
    {
        var (displayName, callSign) = TruppLabel(truppId);
        _session.MarkScbaRemoved(truppId);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"{displayName} abgenommen", from: callSign, to: null);
        UpdateAlarm(newAlarmTripped: false); // a removed trupp may clear the last alarm
        RefreshHeader();
        _onChanged();
    }

    // Rebuild the trupp rows from the incident on any change — this device's edit, or another's.
    private void RefreshTrupps()
    {
        Trupps.Clear();
        foreach (var trupp in _session.Incident.ScbaTrupps)
            Trupps.Add(CreateRow(trupp));
        // Another device may have just taken the suggested number -- re-suggest, but never
        // clobber a number this device's operator already hand-typed into the form.
        if (!_truppNumberUserEdited)
        {
            var previous = _applyingDefault;
            _applyingDefault = true;
            NewTruppNumber = _session.Incident.NextFreeScbaTruppNumber();
            _applyingDefault = previous;
        }
        RefreshHeader();
    }

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
        AnnounceControlDue();
        if (tripped)
            _onChanged();
    }

    /// <summary>Plays a cue once per Druckabfrage due-crossing per Trupp. Unlike <see cref="LogNewAlarms"/>
    /// this is local feedback, not a journal write, so it runs on joined clients too (not gated on
    /// IsRemote) — only a closed/read-only workspace stays silent.</summary>
    private void AnnounceControlDue()
    {
        if (IsReadOnly)
            return;
        foreach (var trupp in _session.Incident.ScbaTrupps)
        {
            if ((trupp.IsActive || trupp.IsWithdrawing) && trupp.IsControlDue(_clock.Now))
            {
                if (_controlDueAnnounced.Add(trupp.Id))
                    _alarm.Play(AlarmSound.PressureCheckDue);
            }
            else
            {
                _controlDueAnnounced.Remove(trupp.Id);
            }
        }
    }

    /// <summary>Appends one ETB entry per Trupp that has newly entered the alarm state.
    /// Returns whether anything was logged (so callers can persist). No-op when read-only.</summary>
    private bool LogNewAlarms()
    {
        // Only the authoritative device auto-logs alarms; a joined client would double-log (§ IsRemote).
        if (IsReadOnly || _session.IsRemote)
            return false;
        var logged = false;
        foreach (var trupp in _session.Incident.ScbaTrupps)
        {
            if (!(trupp.IsActive || trupp.IsWithdrawing) || !trupp.IsAlarm(_clock.Now) || !_alarmLogged.Add(trupp.Id))
                continue;
            var reason = AlarmReason(trupp);
            _session.AddJournalEntry(
                EtbDirection.System,
                $"Rückzugsalarm {trupp.DisplayName}: {reason}", from: null, to: trupp.CallSign);
            logged = true;
        }
        return logged;
    }

    public void Dispose()
    {
        _session.Changed -= RefreshTrupps;
        _subscription?.Dispose();
    }
}
