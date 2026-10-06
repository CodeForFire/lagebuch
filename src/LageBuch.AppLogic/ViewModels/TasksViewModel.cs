using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The AUFGABEN tab (#88). Unlike the journal this list mutates in place (completion reorders,
/// remote broadcasts replace), so Sync() reconciles the visible rows by id: kept rows are updated
/// and moved, never thrown away, so the grid keeps its selection, focus and scroll position (#294).
/// The ticker drives the countdown displays and the one-shot due alarm.
/// </summary>
public sealed partial class TasksViewModel : ObservableObject, INarrowAware, IEntryForm, IDisposable
{
    // +5 MIN, in the edit panel and on the Aufgabe-fällig bar alike (#246); the Rückmeldung bar's
    // snooze is the same five minutes.
    private const int ExtendMinutes = 5;

    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly IAlarmService _alarm;
    private readonly Action _onChanged;
    private readonly Action<string, Action> _offerUndo;

    // Null on a read-only workspace: rows are static history there and the due alarm is gated off
    // anyway, so holding a live ticker subscription would only keep the clock ticking for nothing
    // (ScbaViewModel precedent — keeps Closing_workspace_drops_reminder at zero subscribers).
    private readonly IDisposable? _subscription;
    private readonly HashSet<Guid> _dueAnnounced = new();

    public TasksViewModel(
        IIncidentSession session,
        IClock clock,
        ITicker ticker,
        IAlarmService alarm,
        MasterDataSet masterData,
        Action onChanged,
        Action<string, Action>? offerUndo = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _alarm = alarm;
        _onChanged = onChanged;
        _offerUndo = offerUndo ?? ((_, _) => { });
        IsReadOnly = session.IsReadOnly;

        AssigneeOptions = AssigneeSuggestions(masterData);
        Rows = new ObservableCollection<TaskRow>();
        _subscription = IsReadOnly ? null : ticker.Subscribe(OnTick);
        _session.Changed += Sync;
        Sync();
    }

    public bool IsReadOnly { get; }

    public ObservableCollection<TaskRow> Rows { get; }

    public IReadOnlyList<string> AssigneeOptions { get; }

    // Callsigns and personnel names suggest; anything else stays free text. Funktionen are not
    // who a task goes to (#468). Shared with TaskDialogViewModel so both offer the same list.
    internal static IReadOnlyList<string> AssigneeSuggestions(MasterDataSet masterData) =>
        masterData.RadioCallSigns
            .Concat(masterData.Personnel.Select(p => p.DisplayName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    // Shared with TaskDialogViewModel (same assembly) so picker wording matches everywhere. Low to
    // high, because the triage segments read as a scale left to right (#246).
    internal static IReadOnlyList<ImportanceOption> ImportanceLevels() =>
        Enum.GetValues<TaskImportance>()
            .OrderBy(v => (int)v)
            .Select(v => new ImportanceOption(v, Formatting.Level(v)))
            .ToArray();

    internal static IReadOnlyList<UrgencyOption> UrgencyLevels() =>
        Enum.GetValues<TaskUrgency>()
            .OrderBy(v => (int)v)
            .Select(v => new UrgencyOption(v, Formatting.Level(v)))
            .ToArray();

    public IReadOnlyList<ImportanceOption> ImportanceOptions { get; } = ImportanceLevels();

    public IReadOnlyList<UrgencyOption> UrgencyOptions { get; } = UrgencyLevels();

    // Display order (spec §4): open first, then urgency desc -> importance desc -> oldest first.
    // Overdue state deliberately does NOT reorder (sound + red chip draw attention instead).
    internal static IOrderedEnumerable<IncidentTask> SortForDisplay(IEnumerable<IncidentTask> tasks) =>
        tasks.OrderBy(t => t.IsCompleted ? 1 : 0)
             .ThenByDescending(t => t.Urgency)
             .ThenByDescending(t => t.Importance)
             .ThenBy(t => t.CreatedAt);

    // --- Filter: three-state radio group (ALLE/OFFEN/ERLEDIGT), default OFFEN. ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenFilter))]
    [NotifyPropertyChangedFor(nameof(IsDoneFilter))]
    [NotifyPropertyChangedFor(nameof(IsAllFilter))]
    private TaskFilterKind _filter = TaskFilterKind.Open;

    partial void OnFilterChanged(TaskFilterKind value) => Sync(); // rebuild the visible subset

    // TwoWay radio bindings: checking writes through to Filter; writing false (a binding engine
    // syncing the unchecked radios) must be a no-op, never flip the filter.
    public bool IsOpenFilter
    {
        get => Filter == TaskFilterKind.Open;
        set
        {
            if (value)
            {
                Filter = TaskFilterKind.Open;
            }
        }
    }

    public bool IsDoneFilter
    {
        get => Filter == TaskFilterKind.Done;
        set
        {
            if (value)
            {
                Filter = TaskFilterKind.Done;
            }
        }
    }

    public bool IsAllFilter
    {
        get => Filter == TaskFilterKind.All;
        set
        {
            if (value)
            {
                Filter = TaskFilterKind.All;
            }
        }
    }

    [RelayCommand]
    private void ShowOpen() => Filter = TaskFilterKind.Open;

    [RelayCommand]
    private void ShowDone() => Filter = TaskFilterKind.Done;

    [RelayCommand]
    private void ShowAll() => Filter = TaskFilterKind.All;

    private bool IsVisible(IncidentTask t) => Filter switch
    {
        TaskFilterKind.Open => !t.IsCompleted,
        TaskFilterKind.Done => t.IsCompleted,
        _ => true,
    };

    // --- Input dock ---

    // Quiet until the first press: a tab that opens already scolding teaches nothing.
    private bool _errorsShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTextError))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    private string _newText = string.Empty;

    [ObservableProperty]
    private string? _newAssignee;

    [ObservableProperty]
    private TaskImportance _newImportance = TaskImportance.Medium;

    [ObservableProperty]
    private TaskUrgency _newUrgency = TaskUrgency.Medium;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTimerMinutesError))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    private int? _newTimerMinutes = IncidentTask.DefaultTimerMinutes(TaskUrgency.Medium);

    // The minutes field follows the urgency's default; an explicit override survives until the
    // urgency selection itself changes again.
    partial void OnNewUrgencyChanged(TaskUrgency value) =>
        NewTimerMinutes = IncidentTask.DefaultTimerMinutes(value);

    /// <summary>Whether the Aufgabe is still missing, once the operator has asked (#412).</summary>
    /// <summary>Everything this form is still waiting on, on one line beneath its fields (#412).</summary>
    public string? ErrorSummary => ValidationMessages.Summarize(
        NewTextError, NewTimerMinutesError);

    public string? NewTextError =>
        _errorsShown && string.IsNullOrWhiteSpace(NewText) ? ValidationMessages.Required : null;

    /// <summary>Whether the timer box is empty or negative, once the operator has asked (#412).</summary>
    public string? NewTimerMinutesError =>
        _errorsShown && NewTimerMinutes is not >= 0 ? ValidationMessages.TimerMinutes : null;

    // Only the read-only rule gates the button; a missing field leaves it live and answers on the
    // press instead, because a grey button names nothing (#412).
    private bool CanAddTask => !IsReadOnly;

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isNarrow;

    /// <summary>
    /// Whether the narrow layout's add-task form is open. Five fields across ~810px; a phone
    /// stacks them and only while a task is actually being written.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isComposerOpen;

    /// <summary>
    /// Whether the dock is on screen: always when wide, only while composing on a phone, and never
    /// while the edit panel is open, so two forms are never stacked under the grid (#246).
    /// </summary>
    public bool ShowComposer => (!IsNarrow || IsComposerOpen) && !IsEditing;

    /// <summary>The phone's "add a task" affordance, shown exactly when no form is.</summary>
    public bool ShowComposerButton => IsNarrow && !IsComposerOpen && !IsEditing;

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void OpenComposer() => IsComposerOpen = true;

    [RelayCommand]
    private void CloseComposer()
    {
        IsComposerOpen = false;
        ShowErrors(false); // a dismissed form must not reopen still complaining
    }

    /// <inheritdoc />
    public event EventHandler<EntrySubmittedEventArgs>? EntrySubmitted;

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void AddTask()
    {
        if (!Validate())
        {
            EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Rejected);
            return;
        }

        _session.AddTask(NewText, NewAssignee, NewImportance, NewUrgency, NewTimerMinutes!.Value);
        NewText = string.Empty; // priorities stay sticky for rapid follow-up entries
        ShowErrors(false); // ...and the cleared text must not read as a fresh complaint

        // Reached only on success, so this is where the phone's form closes again.
        IsComposerOpen = false;
        _onChanged();
        EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Succeeded);
    }

    private bool Validate()
    {
        ShowErrors(true);
        return NewTextError is null && NewTimerMinutesError is null;
    }

    private void ShowErrors(bool shown)
    {
        _errorsShown = shown;
        OnPropertyChanged(nameof(NewTextError));
        OnPropertyChanged(nameof(ErrorSummary));
        OnPropertyChanged(nameof(NewTimerMinutesError));
        OnPropertyChanged(nameof(ErrorSummary));
    }

    // --- Edit an existing Aufgabe (#246): a panel below the grid, not inline cell editing, so the
    //     grid keeps its triage colours while a task is being corrected. ---

    // Quiet until SPEICHERN is pressed, like the dock.
    private bool _editErrorsShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtendEditingTimerCommand))]
    private TaskRow? _editingTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditTextError))]
    [NotifyPropertyChangedFor(nameof(EditErrorSummary))]
    private string _editText = string.Empty;

    [ObservableProperty]
    private string? _editAssignee;

    [ObservableProperty]
    private TaskImportance _editImportance = TaskImportance.Medium;

    [ObservableProperty]
    private TaskUrgency _editUrgency = TaskUrgency.Medium;

    public bool IsEditing => EditingTask is not null;

    /// <summary>Whether the edited Aufgabe was emptied, once the Lagebuchführer has asked (#412).</summary>
    public string? EditTextError =>
        _editErrorsShown && string.IsNullOrWhiteSpace(EditText) ? ValidationMessages.Required : null;

    /// <summary>Everything the edit panel is still waiting on, on one line beneath its fields (#412).</summary>
    public string? EditErrorSummary => ValidationMessages.Summarize(EditTextError);

    // A done task is history, as on the ETB: reopen it first.
    private bool CanEdit(TaskRow row) => !IsReadOnly && !row.IsDone;

    private void BeginEdit(TaskRow row)
    {
        EditText = row.Text;
        EditAssignee = string.IsNullOrEmpty(row.Assignee) ? null : row.Assignee;
        EditImportance = row.Importance;
        EditUrgency = row.Urgency;
        ShowEditErrors(false); // a freshly opened panel starts quiet
        EditingTask = row;
        SelectedTask = row; // the grid's selection marks the row the panel is about
    }

    private bool CanSaveEdit => IsEditing;

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private void SaveEdit()
    {
        ShowEditErrors(true);
        if (EditTextError is not null || EditingTask is not { } row)
        {
            return;
        }

        var text = EditText.Trim();
        var assignee = EditAssignee?.Trim() ?? string.Empty;
        var importance = EditImportance;
        var urgency = EditUrgency;
        CancelEdit(); // closed first: the session's Changed re-syncs the rows, which must not find a panel open

        // An unchanged SPEICHERN sends nothing: a silent edit that changes nothing is only traffic.
        if (!string.Equals(text, row.Text, StringComparison.Ordinal)
            || !string.Equals(assignee, row.Assignee, StringComparison.Ordinal)
            || importance != row.Importance
            || urgency != row.Urgency)
        {
            _session.UpdateTask(row.Id, text, assignee, importance, urgency);
            _onChanged();
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingTask = null;
        EditText = string.Empty;
        EditAssignee = null;
        ShowEditErrors(false);
    }

    private bool CanExtendEditingTimer => !IsReadOnly && EditingTask is { HasTimer: true, IsDone: false };

    /// <summary>
    /// The panel's +5 MIN: an action rather than a field, so it applies at once and leaves the
    /// panel open. An overdue task is put off from now, not from its passed due time.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExtendEditingTimer))]
    private void ExtendEditingTimer()
    {
        if (EditingTask is not { } row)
        {
            return;
        }

        _session.ExtendTaskTimer(row.Id, ExtendMinutes);
        _onChanged();
    }

    private void ShowEditErrors(bool shown)
    {
        _editErrorsShown = shown;
        OnPropertyChanged(nameof(EditTextError));
        OnPropertyChanged(nameof(EditErrorSummary));
    }

    // --- Live countdown + one-shot due alarm ---
    private void OnTick()
    {
        var now = _clock.Now;
        foreach (var row in Rows)
        {
            row.RefreshClock(now);
        }

        // Audible cue on the open->due crossing, exactly once per task per VM lifetime. Runs on
        // joined clients too — the sound is local feedback, not logging (IsRemote gates writes).
        // Read-only workspaces (closed/reopened incidents) stay silent: stale overdue tasks must
        // not beep forever at someone reading history.
        if (!IsReadOnly)
        {
            foreach (var task in _session.Incident.Tasks)
            {
                if (TaskRow.IsOverdueAt(task, now))
                {
                    if (_dueAnnounced.Add(task.Id))
                    {
                        _alarm.Play(AlarmSound.TaskDue);
                    }
                }
                else if (!task.IsCompleted)
                {
                    _dueAnnounced.Remove(task.Id); // put off with +5 MIN: it has to sound again (#246)
                }
            }
        }

        RefreshHeader();
    }

    // --- #460: the Aufgabe-fällig bar names the task and leads to it ---

    /// <summary>
    /// The open tasks past their due time, the longest overdue first. The bar names the first and
    /// counts the rest; the chip in the grid uses the same rule, so the two can never disagree.
    /// </summary>
    private List<IncidentTask> OverdueTasks()
    {
        var now = _clock.Now;
        return _session.Incident.Tasks
            .Where(t => TaskRow.IsOverdueAt(t, now))
            .OrderBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToList();
    }

    /// <summary>Whether the header shows the Aufgabe-fällig bar. Never on a read-only workspace.</summary>
    public bool HasDueTask => !IsReadOnly && OverdueTasks().Count > 0;

    /// <summary>"Aufgabe fällig: ‹Text› (zugeteilt an ‹Assignee›) · +N weitere", or "—".</summary>
    public string DueTaskDisplay
    {
        get
        {
            var overdue = OverdueTasks();
            if (overdue.Count == 0)
            {
                return "—";
            }

            var task = overdue[0];
            var text = string.IsNullOrWhiteSpace(task.Assignee)
                ? $"Aufgabe fällig: {task.Text}"
                : $"Aufgabe fällig: {task.Text} (zugeteilt an {task.Assignee})";
            return overdue.Count > 1 ? $"{text} · +{overdue.Count - 1} weitere" : text;
        }
    }

    /// <summary>The row the grid has selected; the bar points it at the task it names.</summary>
    [ObservableProperty]
    private TaskRow? _selectedTask;

    /// <summary>
    /// Raised after <see cref="SelectedTask"/> has been pointed at the task the bar names, so the
    /// workspace can bring the Aufgaben tab forward and the view can scroll the row into sight.
    /// Raised on every request, not only when the selection changes: tapping the bar again after
    /// scrolling away has to scroll back (#422 precedent).
    /// </summary>
    public event EventHandler? RevealRequested;

    /// <summary>The bar was tapped: go to the longest-overdue task, the one the bar names.</summary>
    [RelayCommand]
    private void ShowMostOverdueTask()
    {
        if (OverdueTasks().FirstOrDefault() is not { } task)
        {
            return;
        }

        // An overdue task is open by definition, so ERLEDIGT would hide the very row asked for.
        if (Filter == TaskFilterKind.Done)
        {
            Filter = TaskFilterKind.Open;
        }

        if (Rows.FirstOrDefault(r => r.Id == task.Id) is not { } row)
        {
            return;
        }

        SelectedTask = row;
        RevealRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The bar's ERLEDIGT: ticks off the task the bar names, as the grid's checkbox would.</summary>
    [RelayCommand]
    private void CompleteMostOverdueTask()
    {
        if (IsReadOnly || OverdueTasks().FirstOrDefault() is not { } task)
        {
            return;
        }

        _session.SetTaskCompleted(task.Id, true);
        _onChanged();
    }

    /// <summary>The bar's +5 MIN: puts the task it names off by five minutes from now (#246).</summary>
    [RelayCommand]
    private void ExtendMostOverdueTask()
    {
        if (IsReadOnly || OverdueTasks().FirstOrDefault() is not { } task)
        {
            return;
        }

        _session.ExtendTaskTimer(task.Id, ExtendMinutes);
        _onChanged();
    }

    private void RefreshHeader()
    {
        OnPropertyChanged(nameof(HasDueTask));
        OnPropertyChanged(nameof(DueTaskDisplay));
    }

    // --- Sync ---

    /// <summary>
    /// Brings Rows in line with the incident. Idempotent; runs on every incident change (this tab,
    /// the ETB dialog, another tab, another device) and on a filter change. Rows are reconciled by
    /// id rather than rebuilt with Clear()+re-add, whose Reset dropped the selection and focus on
    /// every change (#294): a completion re-sorts by Move, a filter change inserts and removes.
    /// Because rows now outlive a pull, <see cref="TaskRow.Update"/> carries an explicit echo
    /// guard so a pulled IsDone is never written back.
    /// </summary>
    public void Sync()
    {
        var now = _clock.Now;
        var visible = SortForDisplay(_session.Incident.Tasks)
            .Where(IsVisible)
            .ToList();

        var selectedId = SelectedTask?.Id;
        RowReconciler.Reconcile(
            Rows,
            visible,
            r => r.Id,
            t => t.Id,
            t => new TaskRow(_session, t, IsReadOnly, now, _onChanged, BeginEdit, CanEdit, _offerUndo),
            (row, t) => row.Update(t, now));

        // A backstop: a kept row stays selected on its own, but one filtered out must not linger.
        SelectedTask = Rows.FirstOrDefault(r => r.Id == selectedId);

        // The panel stays open across other changes and keeps what was typed; it closes only once
        // its task is gone or done, which another device can do at any moment.
        if (EditingTask is { } editing && Rows.FirstOrDefault(r => r.Id == editing.Id) is not { IsDone: false })
        {
            CancelEdit();
        }

        ExtendEditingTimerCommand.NotifyCanExecuteChanged();
        RefreshHeader();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _session.Changed -= Sync;
    }
}

/// <summary>
/// One rendered task row. Two-way IsDone mirrors ChecklistItemViewModel: the CheckBox binding is
/// the single source of truth, and the echo-guard keeps state pulls (<see cref="Update"/>, after a
/// change made anywhere) from writing back what was only just pulled. Everything else is display:
/// a correction happens in <see cref="TasksViewModel"/>'s edit panel (#246) and reaches the row
/// through <see cref="Update"/>, so the grid's cells never turn into controls.
/// </summary>
public sealed partial class TaskRow : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly Action<string, Action>? _offerUndo;
    private readonly RelayCommand _beginEditCommand;

    public TaskRow(
        IIncidentSession session,
        IncidentTask task,
        bool isReadOnly,
        DateTimeOffset now,
        Action onChanged,
        Action<TaskRow> beginEdit,
        Func<TaskRow, bool> canEdit,
        Action<string, Action>? offerUndo = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(beginEdit);
        ArgumentNullException.ThrowIfNull(canEdit);
        _session = session;
        Id = task.Id;
        _onChanged = onChanged;
        _offerUndo = offerUndo;
        IsReadOnly = isReadOnly;
        CreatedDisplay = $"{Formatting.Timestamp(task.CreatedAt)} · {task.CreatedBy}";
        HasTimer = task.DueAt != DateTimeOffset.MaxValue;
        _text = task.Text;
        _assignee = task.Assignee;
        _importance = task.Importance;
        _urgency = task.Urgency;
        _isDone = task.IsCompleted;

        _completedDisplay = CompletedDisplayOf(task);
        _remainingDisplay = ComputeRemaining(task, now);
        IsOverdue = IsOverdueAt(task, now);
        _beginEditCommand = new RelayCommand(() => beginEdit(this), () => canEdit(this));
    }

    // Set while Update() writes the incident's completion into the row, so OnIsDoneChanged does
    // not push what was only just pulled back to the session (#294).
    private bool _pulling;

    /// <summary>
    /// Brings a kept row in line with its task after a change anywhere in the incident (#294),
    /// instead of the row being thrown away and rebuilt: its completion, and since #246 its text,
    /// assignee and priorities, which the edit panel or another device may have corrected.
    /// </summary>
    public void Update(IncidentTask task, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        _pulling = true;
        try
        {
            IsDone = task.IsCompleted;
        }
        finally
        {
            _pulling = false;
        }

        Text = task.Text;
        Assignee = task.Assignee;
        Importance = task.Importance;
        Urgency = task.Urgency;
        CompletedDisplay = CompletedDisplayOf(task);
        IsOverdue = IsOverdueAt(task, now);
        RemainingDisplay = ComputeRemaining(task, now);
        OnPropertyChanged(nameof(IsOverdue));
        _beginEditCommand.NotifyCanExecuteChanged();
    }

    // German short stamp for completed rows. Empty while open — the view hides the label then.
    private static string CompletedDisplayOf(IncidentTask task) =>
        task.CompletedAt is { } completedAt ? $"ERLEDIGT · {completedAt:HH:mm}" : string.Empty;

    public Guid Id { get; }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private string _assignee;

    public string CreatedDisplay { get; }

    /// <summary>Whether the task runs on a timer at all; +5 MIN has nothing to extend otherwise.</summary>
    public bool HasTimer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportanceLabel))]
    [NotifyPropertyChangedFor(nameof(IsImportanceHigh))]
    [NotifyPropertyChangedFor(nameof(IsImportanceMedium))]
    [NotifyPropertyChangedFor(nameof(IsImportanceLow))]
    private TaskImportance _importance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrgencyLabel))]
    [NotifyPropertyChangedFor(nameof(IsUrgencyHigh))]
    [NotifyPropertyChangedFor(nameof(IsUrgencyMedium))]
    [NotifyPropertyChangedFor(nameof(IsUrgencyLow))]
    private TaskUrgency _urgency;

    public string ImportanceLabel => Formatting.Level(Importance);

    public string UrgencyLabel => Formatting.Level(Urgency);

    public bool IsUrgencyHigh => Urgency == TaskUrgency.High;

    public bool IsUrgencyMedium => Urgency == TaskUrgency.Medium;

    public bool IsUrgencyLow => Urgency == TaskUrgency.Low;

    public bool IsImportanceHigh => Importance == TaskImportance.High;

    public bool IsImportanceMedium => Importance == TaskImportance.Medium;

    public bool IsImportanceLow => Importance == TaskImportance.Low;

    public bool IsReadOnly { get; }

    /// <summary>Enter or F2 on the row, or its pencil: opens the edit panel below the grid (#246).</summary>
    public IRelayCommand BeginEditCommand => _beginEditCommand;

    /// <summary>"ERLEDIGT · HH:mm" once done, empty while open (completion time from the task).</summary>
    [ObservableProperty]
    private string _completedDisplay;

    [ObservableProperty]
    private bool _isDone;

    partial void OnIsDoneChanged(bool value)
    {
        if (IsReadOnly || _pulling)
            return;
        var task = _session.Incident.Tasks.FirstOrDefault(t => t.Id == Id);
        if (task is { } current && current.IsCompleted != value)
        {
            _session.SetTaskCompleted(Id, value);
            _onChanged();

            // Only a tick is offered back (#543): a stray Space closes an Aufgabe without anyone
            // noticing, while reopening one is never the silent mistake.
            if (value)
            {
                _offerUndo?.Invoke($"„{Text}“ erledigt.", Reopen);
            }
        }
    }

    // Undoes this row's tick, unless the task was reopened (or removed) meanwhile.
    private void Reopen()
    {
        if (_session.Incident.Tasks.FirstOrDefault(t => t.Id == Id) is { IsCompleted: true })
        {
            _session.SetTaskCompleted(Id, false);
            _onChanged();
        }
    }

    /// <summary>True while the task is open and past its due time. Order is unaffected by design.</summary>
    public bool IsOverdue { get; private set; }

    [ObservableProperty]
    private string _remainingDisplay;

    /// <summary>Called by the owning VM on every ticker tick — recomputes countdown + overdue.</summary>
    public void RefreshClock(DateTimeOffset now)
    {
        var task = _session.Incident.Tasks.FirstOrDefault(t => t.Id == Id);
        if (task is null)
        {
            return;
        }

        IsOverdue = IsOverdueAt(task, now);
        RemainingDisplay = ComputeRemaining(task, now);
        OnPropertyChanged(nameof(IsOverdue));
    }

    /// <summary>Open, on a timer, and past its due time — the one overdue rule (chip, bar and alarm).</summary>
    internal static bool IsOverdueAt(IncidentTask task, DateTimeOffset now) =>
        !task.IsCompleted && task.DueAt != DateTimeOffset.MaxValue && task.DueAt <= now;

    private static string ComputeRemaining(IncidentTask task, DateTimeOffset now)
    {
        if (task.IsCompleted)
        {
            return "–";
        }

        if (task.DueAt == DateTimeOffset.MaxValue)
        {
            return "–";
        }

        if (task.DueAt <= now)
        {
            return "FÄLLIG";
        }

        var remaining = task.DueAt - now;
        return $"noch {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }
}

public enum TaskFilterKind
{
    Open,
    Done,
    All,
}

/// <summary>An enum value paired with its German label, so a picker never falls back to the enum
/// identifier. Two closed records instead of a generic one, so Avalonia compiled-bind templates
/// stay simple. The Is* flags colour the triage segments (#246) the way the grid colours a level.</summary>
public readonly record struct ImportanceOption(TaskImportance Value, string Label)
{
    public bool IsHigh => Value == TaskImportance.High;

    public bool IsMedium => Value == TaskImportance.Medium;

    public bool IsLow => Value == TaskImportance.Low;

    // What a screen reader announces for the segment, instead of the record's member dump.
    public override string ToString() => Label;
}

/// <summary>See <see cref="ImportanceOption"/>.</summary>
public readonly record struct UrgencyOption(TaskUrgency Value, string Label)
{
    public bool IsHigh => Value == TaskUrgency.High;

    public bool IsMedium => Value == TaskUrgency.Medium;

    public bool IsLow => Value == TaskUrgency.Low;

    // What a screen reader announces for the segment, instead of the record's member dump.
    public override string ToString() => Label;
}
