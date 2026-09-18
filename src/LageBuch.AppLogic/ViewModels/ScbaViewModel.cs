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
    private readonly Action<Guid?> _onAssignSafetyTrupp;

    public ScbaTruppRow(
        AtemschutzTrupp trupp,
        IClock clock,
        bool isReadOnly,
        Action onStart,
        Action<int> onRecordPressure,
        Action onWithdraw,
        Action onMarkRemoved,
        IReadOnlyList<SafetyTruppOption> safetyTruppOptions,
        SafetyTruppOption selectedSafetyTrupp,
        string? safetyTruppHint,
        Action<Guid?> onAssignSafetyTrupp)
    {
        ArgumentNullException.ThrowIfNull(trupp);
        _trupp = trupp;
        _clock = clock;
        _isReadOnly = isReadOnly;
        _onStart = onStart;
        _onRecordPressure = onRecordPressure;
        _onWithdraw = onWithdraw;
        _onMarkRemoved = onMarkRemoved;
        _onAssignSafetyTrupp = onAssignSafetyTrupp;
        _pressureInput = trupp.LatestPressure ?? 300;
        SafetyTruppOptions = safetyTruppOptions;
        SafetyTruppHint = safetyTruppHint;

        // Assign the backing field, not the property: going through the setter here would fire
        // OnSelectedSafetyTruppChanged during construction, and RefreshTrupps builds a fresh row
        // for every Trupp on every change -- so each rebuild would re-send the assignment it is
        // merely displaying. Same reason _pressureInput is seeded directly above.
        _selectedSafetyTrupp = selectedSafetyTrupp;
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
            {
                return "—";
            }

            var remaining = _trupp.Remaining(_clock.Now);
            return remaining <= TimeSpan.Zero ? "überzogen" : Clock(remaining);
        }
    }

    public string ControlRemainingDisplay
    {
        get
        {
            if (!(_trupp.IsActive || _trupp.IsWithdrawing))
            {
                return "—";
            }

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
        _ => "Im Einsatz",
    };

    /// <summary>The Trupps that may be picked as this one's Sicherheitstrupp, "— kein —" first.
    /// Carried on the row rather than the ViewModel because the cell template binds against the
    /// row, the same shape ForceRow uses for its status options.</summary>
    public IReadOnlyList<SafetyTruppOption> SafetyTruppOptions { get; }

    /// <summary>The single amber line under the picker, or null when there is nothing to say.
    /// Computed by the ViewModel at row-build time: every input to it is domain state, and any
    /// change to domain state rebuilds the rows, so it cannot go stale between rebuilds.</summary>
    public string? SafetyTruppHint { get; }

    public bool HasSafetyTruppHint => !string.IsNullOrEmpty(SafetyTruppHint);

    public bool CanAssignSafetyTrupp => !_isReadOnly && !_trupp.IsReturned;

    [ObservableProperty]
    private SafetyTruppOption _selectedSafetyTrupp;

    partial void OnSelectedSafetyTruppChanged(SafetyTruppOption value)
    {
        // Avalonia nulls SelectedItem while ItemsSource is being swapped, and RefreshTrupps swaps
        // it on every change, so a null here is always the ComboBox resetting itself and never a
        // user choice -- "kein Sicherheitstrupp" arrives as SafetyTruppOption.None instead. The
        // equality check makes a rebuild that re-selects the value already on the Trupp inert, so
        // there is no redundant command and no duplicate ETB line.
        if (_isReadOnly || value is null || value.Id == _trupp.SafetyTruppId)
        {
            return;
        }

        _onAssignSafetyTrupp(value.Id);
    }

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
        OnPropertyChanged(nameof(CanAssignSafetyTrupp));
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

    // True once the user has hand-edited the Einsatzzeit; after that a Trupp-type switch must not
    // overwrite it. Programmatic sets (default application, form reset) are fenced by _applyingDefault
    // so they do not count as a user edit.
    private readonly IncidentSettings _settings;

    /// <summary>
    /// The Trupp-Typen as the Stammdaten describe them -- crew size and Einsatzzeit included.
    /// <see cref="TruppTypeOptions"/> is only their names, for the picker; this is what the form
    /// reads the rules from since #398 took them off the hard-coded designations.
    /// </summary>
    private readonly IReadOnlyList<TruppType> _truppTypes;
    private DateTimeOffset? _lastAlarmAnnouncedAt;

    private bool _maxDurationUserEdited;

    // Same idea for the Abfrage-Intervall, which otherwise defaults to a third of the Einsatzzeit
    // (#78) -- a hand-typed interval must survive a later Einsatzzeit change.
    private bool _controlIntervalUserEdited;

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

        // Seed the add-Trupp form defaults. Direct field writes so no OnChanged fires and the
        // fields do not read as user-edited. No Trupp-Typ is selected yet, so the Einsatzzeit is
        // the same fallback an unlisted type gets -- "nothing selected" and "type the Stammdaten
        // do not describe" must read identically, or the number would depend on how you got here.
        _newMaxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes;
        _newReturnPressureBar = _settings.ReturnPressureBar;
        _newEntryPressure = 300;
        _newTruppNumber = session.Incident.NextFreeScbaTruppNumber();
        IsReadOnly = session.IsReadOnly;
        _truppTypes = masterData.TruppTypes;
        TruppTypeOptions = masterData.TruppTypes.Select(t => t.Name).ToArray();
        CallSignOptions = masterData.RadioCallSigns;
        PersonOptions = masterData.Personnel.Select(p => p.DisplayName).ToArray();
        Trupps = new ObservableCollection<ScbaTruppRow>(session.Incident.ScbaTrupps.Select(CreateRow));
        _session.Changed += RefreshTrupps;

        // The property setter path (below) is what marks an interval as user-edited, so the
        // initial derivation from _newMaxDurationMinutes must go through it once here too.
        ApplyDefaultControlInterval();

        // Suppress re-logging alarms for trupps already alarming when the incident is reopened.
        foreach (var t in session.Incident.ScbaTrupps)
        {
            if ((t.IsActive || t.IsWithdrawing) && t.IsAlarm(_clock.Now))
            {
                _alarmLogged.Add(t.Id);
            }
        }

        // A closed incident is historical: no live ticking, no auto-logging (it cannot mutate).
        _subscription = IsReadOnly ? null : ticker.Subscribe(OnTick);
    }

    public bool IsReadOnly { get; }

    public IReadOnlyList<string> TruppTypeOptions { get; }

    /// <summary>Largest Einsatzzeit this form's spinner offers. Shared with the Stammdaten editor's
    /// so a Trupp-Typ can always be registered at the Einsatzzeit its own row specifies.</summary>
    public static int MaxDurationLimitMinutes => AtemschutzTrupp.MaxEditableDurationMinutes;

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
    /// Whether the selected Trupp-Typ is crewed by three, according to the Stammdaten. Drives the
    /// visibility of the third name box and, through <c>CanAddTrupp</c>, whether the form may be
    /// submitted at all -- which is where the per-type crew rule is enforced now that the domain
    /// only checks what it can prove without the Stammdaten it cannot see (#398).
    /// <para>
    /// A designation the Stammdaten do not list is an ordinary two-person Trupp. An Einsatz in
    /// progress must never be blocked by a missing Stammdaten row.
    /// </para>
    /// </summary>
    public bool RequiresThirdMember =>
        StammdatenCatalogue.Find(NewDesignation, _truppTypes)?.MemberCount >= AtemschutzTrupp.MaxMemberCount;

    /// <summary>
    /// Truppführer. A Trupp always has one; the crew is never a single free-text field.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newTruppfuehrer = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newTruppmann = string.Empty;

    /// <summary>Only used -- and only required -- for a Trupp-Typ crewed by three.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTruppCommand))]
    private string _newZweiterTruppmann = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    /// <summary>
    /// Internally assigned, never user-edited (#217): a hand-typed duplicate crashed
    /// <see cref="Domain.Incident.AddScbaTrupp"/>, so the view shows this as read-only text and
    /// only the constructor, <see cref="AddTrupp"/>, and <see cref="RefreshTrupps"/> ever set it.
    /// </summary>
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

    partial void OnNewMaxDurationMinutesChanged(int value)
    {
        if (!_applyingDefault)
        {
            _maxDurationUserEdited = true;
        }

        // The Abfrage-Intervall tracks the Einsatzzeit (a third of it) unless separately overridden,
        // whether this change came from the user or from ApplyDefaultMaxDuration below.
        if (!_controlIntervalUserEdited)
        {
            ApplyDefaultControlInterval();
        }
    }

    partial void OnNewControlIntervalMinutesChanged(int value)
    {
        if (!_applyingDefault)
        {
            _controlIntervalUserEdited = true;
        }
    }

    // Switching the Trupp-Typ re-suggests the Einsatzzeit its Stammdaten row carries, but only
    // while the user has not overridden the field — a hand-typed value survives.
    partial void OnNewDesignationChanged(string value)
    {
        if (!_maxDurationUserEdited)
        {
            ApplyDefaultMaxDuration();
        }
    }

    private void ApplyDefaultMaxDuration()
    {
        var previous = _applyingDefault;
        _applyingDefault = true;
        NewMaxDurationMinutes = StammdatenCatalogue.Find(NewDesignation, _truppTypes)?.MaxDurationMinutes
            ?? AtemschutzTrupp.DefaultMaxDurationMinutes;
        _applyingDefault = previous;

        // Called explicitly rather than left to OnNewMaxDurationMinutesChanged's cascade: the
        // generated property setter is a no-op when the value doesn't actually change (e.g. the
        // AGT default reapplied on form reset), which would otherwise leave a stale user-edited
        // Abfrage-Intervall in place.
        if (!_controlIntervalUserEdited)
        {
            ApplyDefaultControlInterval();
        }
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
            {
                return "—";
            }

            return IsAnyControlDue
                ? $"Druckabfrage fällig: {urgent.DisplayName}"
                : $"Nächste Druckabfrage: {urgent.DisplayName} in {urgent.ControlRemainingDisplay}";
        }
    }

    private IEnumerable<AtemschutzTrupp> AlarmingTrupps =>
        _session.Incident.ScbaTrupps.Where(t => (t.IsActive || t.IsWithdrawing) && t.IsAlarm(_clock.Now));

    public bool IsAnyAlarm => AlarmingTrupps.Any();

    public string AlarmDisplay
    {
        get
        {
            var trupps = AlarmingTrupps.ToList();
            if (trupps.Count == 0)
            {
                return "—";
            }

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
        {
            IsAlarmAcknowledged = false;
        }

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

        // The per-type crew rule (#398): the domain cannot see the Stammdaten, so this is where
        // "this Trupp-Typ needs three people" is enforced. An incomplete three-person Trupp
        // disables the button rather than throwing on click.
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
        _session.AddScbaTrupp(
            designation,
            crew,
            entryPressure,
            truppNumber,
            callSign,
            task: null,
            maxDurationMinutes: NewMaxDurationMinutes,
            returnPressureBar: NewReturnPressureBar,
            pressureControlIntervalMinutes: NewControlIntervalMinutes);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"{displayName} bereitgestellt: {membersDisplay}, Einstiegsdruck {entryPressure} bar",
            from: callSign,
            to: null);

        _maxDurationUserEdited = false;
        _controlIntervalUserEdited = false;
        NewDesignation = string.Empty;
        NewTruppfuehrer = string.Empty;
        NewTruppmann = string.Empty;
        NewZweiterTruppmann = string.Empty;
        NewCallSign = null;
        NewEntryPressure = 300;
        NewReturnPressureBar = _settings.ReturnPressureBar;

        NewTruppNumber = _session.Incident.NextFreeScbaTruppNumber(); // sets up the *next* Trupp's number
        ApplyDefaultMaxDuration(); // empty designation => AGT default; also re-derives the interval
        RefreshHeader();
        _onChanged();
    }

    private ScbaTruppRow CreateRow(AtemschutzTrupp trupp)
    {
        var safetyOptions = SafetyOptionsFor(trupp);
        return new ScbaTruppRow(
            trupp,
            _clock,
            IsReadOnly,
            () => Start(trupp.Id),
            bar => RecordPressure(trupp.Id, bar),
            () => Withdraw(trupp.Id),
            () => MarkRemoved(trupp.Id),
            safetyOptions,
            safetyOptions.FirstOrDefault(o => o.Id == trupp.SafetyTruppId) ?? SafetyTruppOption.None,
            SafetyTruppHintFor(trupp),
            safetyTruppId => AssignSafetyTrupp(trupp.Id, safetyTruppId));
    }

    /// <summary>The picker contents for one row: "— kein —", then every other Trupp still
    /// bereitgestellt, ordered by Truppnummer.</summary>
    private List<SafetyTruppOption> SafetyOptionsFor(AtemschutzTrupp trupp)
    {
        var options = new List<SafetyTruppOption> { SafetyTruppOption.None };
        options.AddRange(
            _session.Incident.ScbaTrupps
                .Where(t => t.Id != trupp.Id && t.IsWaiting)
                .OrderBy(t => t.TruppNumber)
                .Select(Option));

        // Keep a Sicherheitstrupp that has since gone under air in the list. A ComboBox whose
        // SelectedItem matches nothing in its ItemsSource renders blank, which would wipe the
        // recorded assignment off the screen at exactly the moment it matters most -- the same
        // reasoning as StammdatenCatalogue.Including for a value the Stammdaten no longer offer.
        if (trupp.SafetyTruppId is { } assignedId
            && options.TrueForAll(o => o.Id != assignedId)
            && _session.Incident.FindScbaTruppOrDefault(assignedId) is { } assigned)
        {
            options.Add(Option(assigned));
        }

        return options;

        static SafetyTruppOption Option(AtemschutzTrupp t) =>
            new(t.Id, $"Trupp {t.TruppNumber}", t.Designation);
    }

    /// <summary>The amber line under one row's picker. Only one fits in the column, so the most
    /// urgent wins: no cover at all beats a cover that has left Bereitstellung, which beats a cover
    /// that is being shared. Never a block — the Einsatzleiter decides, the app records.</summary>
    private string? SafetyTruppHintFor(AtemschutzTrupp trupp)
    {
        if (trupp.SafetyTruppId is not { } assignedId)
        {
            return trupp.IsActive || trupp.IsWithdrawing ? "kein Sicherheitstrupp" : null;
        }

        // A dangling id says nothing useful to the Einsatzleiter, so it says nothing at all.
        if (_session.Incident.FindScbaTruppOrDefault(assignedId) is not { } assigned)
        {
            return null;
        }

        if (!assigned.IsWaiting)
        {
            return "nicht mehr bereitgestellt";
        }

        var alsoCovered = _session.Incident.ScbaTrupps
            .Where(t => t.Id != trupp.Id && t.SafetyTruppId == assignedId && !t.IsReturned)
            .OrderBy(t => t.TruppNumber)
            .Select(t => $"Trupp {t.TruppNumber}")
            .ToList();

        return alsoCovered.Count > 0 ? $"deckt auch {string.Join(", ", alsoCovered)}" : null;
    }

    private void AssignSafetyTrupp(Guid truppId, Guid? safetyTruppId)
    {
        var incident = _session.Incident;
        if (incident.FindScbaTruppOrDefault(truppId) is not { } trupp
            || trupp.SafetyTruppId == safetyTruppId)
        {
            return;
        }

        // Read all three labels before mutating: the mutation raises Changed, which rebuilds every
        // row, and on a joined device the local snapshot is replaced wholesale.
        var covered = trupp.DisplayName;
        var coveredCallSign = trupp.CallSign;
        var previous = trupp.SafetyTruppId is { } previousId
            ? incident.FindScbaTruppOrDefault(previousId)
            : null;
        var next = safetyTruppId is { } nextId ? incident.FindScbaTruppOrDefault(nextId) : null;

        var text = previous is null
            ? $"Sicherheitstrupp für {covered} festgelegt: {Label(next)}"
            : next is null
                ? $"Sicherheitstrupp für {covered} aufgehoben: bisher {Label(previous)}"
                : $"Sicherheitstrupp für {covered} gewechselt: bisher {Label(previous)}, jetzt {Label(next)}";

        _session.SetScbaSafetyTrupp(truppId, safetyTruppId);
        _session.AddJournalEntry(
            EtbDirection.System, text, from: (next ?? previous)?.CallSign, to: coveredCallSign);
        _onChanged();

        static string Label(AtemschutzTrupp? t) => t?.DisplayName ?? "unbekannt";
    }

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
        var incident = _session.Incident;
        var (displayName, callSign) = TruppLabel(truppId);
        var safety = incident.FindScbaTruppOrDefault(truppId)?.SafetyTruppId is { } safetyId
            ? incident.FindScbaTruppOrDefault(safetyId)
            : null;

        // The Trupps this one was standing by for, read before the mutation rebuilds the rows.
        // Start is the only transition that needs this hook: a Trupp stands by while it is
        // bereitgestellt, and Start is the sole way out of that state -- Withdraw requires
        // IsActive and MarkRemoved requires IsWithdrawing, so both are strictly downstream and
        // would only repeat a loss already recorded here.
        var uncovered = incident.ScbaTrupps
            .Where(t => t.Id != truppId && t.SafetyTruppId == truppId && !t.IsReturned)
            .OrderBy(t => t.TruppNumber)
            .Select(t => (t.DisplayName, t.CallSign))
            .ToList();

        _session.StartScbaTrupp(truppId);

        // Naming the Sicherheitstrupp on the "im Einsatz" line, and naming its absence, is the
        // point of #399: this is the line the Einsatzbericht is read for afterwards.
        var startText = safety is null
            ? $"{displayName} im Einsatz, ohne Sicherheitstrupp"
            : $"{displayName} im Einsatz, Sicherheitstrupp: {safety.DisplayName}";
        _session.AddJournalEntry(EtbDirection.System, startText, from: callSign, to: null);

        foreach (var (uncoveredName, uncoveredCallSign) in uncovered)
        {
            _session.AddJournalEntry(
                EtbDirection.System,
                $"{displayName} geht selbst unter Atemschutz — {uncoveredName} ist ohne Sicherheitstrupp",
                from: callSign,
                to: uncoveredCallSign);
        }

        RefreshHeader();
        _onChanged();
    }

    private void RecordPressure(Guid truppId, int bar)
    {
        var (displayName, callSign) = TruppLabel(truppId);
        _session.RecordScbaPressure(truppId, bar);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"Druckkontrolle {displayName}: {bar} bar",
            from: callSign,
            to: null);
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
            $"{displayName} Rückzug",
            from: callSign,
            to: null);
        RefreshHeader();
        _onChanged();
    }

    private void MarkRemoved(Guid truppId)
    {
        var (displayName, callSign) = TruppLabel(truppId);
        _session.MarkScbaRemoved(truppId);
        _session.AddJournalEntry(
            EtbDirection.System,
            $"{displayName} abgenommen",
            from: callSign,
            to: null);
        UpdateAlarm(newAlarmTripped: false); // a removed trupp may clear the last alarm
        RefreshHeader();
        _onChanged();
    }

    // Rebuild the trupp rows from the incident on any change — this device's edit, or another's.
    private void RefreshTrupps()
    {
        Trupps.Clear();
        foreach (var trupp in _session.Incident.ScbaTrupps)
        {
            Trupps.Add(CreateRow(trupp));
        }

        // Another device may have just taken the suggested number -- re-suggest (#217: the number
        // is never user-edited, so there is nothing to clobber).
        NewTruppNumber = _session.Incident.NextFreeScbaTruppNumber();

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
        {
            row.Refresh();
        }

        RefreshHeader();
        var tripped = LogNewAlarms();
        UpdateAlarm(tripped);
        AnnounceControlDue();
        if (tripped)
        {
            _onChanged();
        }
    }

    /// <summary>Plays a cue once per Druckabfrage due-crossing per Trupp. Unlike <see cref="LogNewAlarms"/>
    /// this is local feedback, not a journal write, so it runs on joined clients too (not gated on
    /// IsRemote) — only a closed/read-only workspace stays silent.</summary>
    private void AnnounceControlDue()
    {
        if (IsReadOnly)
        {
            return;
        }

        foreach (var trupp in _session.Incident.ScbaTrupps)
        {
            if ((trupp.IsActive || trupp.IsWithdrawing) && trupp.IsControlDue(_clock.Now))
            {
                if (_controlDueAnnounced.Add(trupp.Id))
                {
                    _alarm.Play(AlarmSound.PressureCheckDue);
                }
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
        {
            return false;
        }

        var logged = false;
        foreach (var trupp in _session.Incident.ScbaTrupps)
        {
            if (!(trupp.IsActive || trupp.IsWithdrawing) || !trupp.IsAlarm(_clock.Now) || !_alarmLogged.Add(trupp.Id))
            {
                continue;
            }

            var reason = AlarmReason(trupp);
            _session.AddJournalEntry(
                EtbDirection.System,
                $"Rückzugsalarm {trupp.DisplayName}: {reason}",
                from: null,
                to: trupp.CallSign);
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
