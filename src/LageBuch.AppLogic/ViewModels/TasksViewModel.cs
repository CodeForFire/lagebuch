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
/// remote broadcasts replace), so Sync() rebuilds the visible rows wholesale — cheap at task
/// counts, and because the input dock's state lives here rather than on rows, a rebuild never
/// eats half-finished input. The ticker drives the countdown displays and the one-shot due alarm.
/// </summary>
public sealed partial class TasksViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly IAlarmService _alarm;
    private readonly Action _onChanged;

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
        Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _alarm = alarm;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;

        // Callsigns, Funktionen and personnel names suggest; anything else stays free text.
        AssigneeOptions = masterData.RadioCallSigns
            .Concat(masterData.Roles)
            .Concat(masterData.Personnel.Select(p => $"{p.LastName} {p.FirstName}"))
            .Distinct()
            .ToArray();
        Rows = new ObservableCollection<TaskRow>();
        _subscription = IsReadOnly ? null : ticker.Subscribe(OnTick);
        _session.Changed += Sync;
        Sync();
    }

    public bool IsReadOnly { get; }

    public ObservableCollection<TaskRow> Rows { get; }

    public IReadOnlyList<string> AssigneeOptions { get; }

    // Shared with TaskDialogViewModel (same assembly) so picker wording matches everywhere.
    internal static IReadOnlyList<ImportanceOption> ImportanceLevels() =>
        Enum.GetValues<TaskImportance>()
            .OrderByDescending(v => (int)v)
            .Select(v => new ImportanceOption(v, Formatting.Level(v)))
            .ToArray();

    internal static IReadOnlyList<UrgencyOption> UrgencyLevels() =>
        Enum.GetValues<TaskUrgency>()
            .OrderByDescending(v => (int)v)
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

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void AddTask()
    {
        if (!Validate())
        {
            return;
        }

        _session.AddTask(NewText, NewAssignee, NewImportance, NewUrgency, NewTimerMinutes!.Value);
        NewText = string.Empty; // priorities stay sticky for rapid follow-up entries
        ShowErrors(false); // ...and the cleared text must not read as a fresh complaint
        _onChanged();
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
                if (TaskRow.IsOverdueAt(task, now) && _dueAnnounced.Add(task.Id))
                {
                    _alarm.Play(AlarmSound.TaskDue);
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

    private void RefreshHeader()
    {
        OnPropertyChanged(nameof(HasDueTask));
        OnPropertyChanged(nameof(DueTaskDisplay));
    }

    // --- Sync ---

    /// <summary>
    /// Rebuilds Rows from the incident. Idempotent; runs on every incident change (this tab, the
    /// ETB dialog, another tab, another device). Rows are recreated fresh each time, which also
    /// makes checkbox echo-guards trivially safe: a pull can never race a half-finished write-back.
    /// </summary>
    public void Sync()
    {
        var now = _clock.Now;
        var visible = SortForDisplay(_session.Incident.Tasks)
            .Where(IsVisible)
            .Select(t => new TaskRow(_session, t, IsReadOnly, now, _onChanged))
            .ToList();

        // Rows are recreated, so the selection is carried over by task id rather than by row.
        var selectedId = SelectedTask?.Id;
        Rows.Clear();
        foreach (var row in visible)
        {
            Rows.Add(row);
        }

        SelectedTask = visible.FirstOrDefault(r => r.Id == selectedId);
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
/// the single source of truth, and the echo-guard keeps state pulls (remote broadcast) from
/// writing back what was only just pulled.
/// </summary>
public sealed partial class TaskRow : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;

    public TaskRow(IIncidentSession session, IncidentTask task, bool isReadOnly, DateTimeOffset now, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(task);
        _session = session;
        Id = task.Id;
        _onChanged = onChanged;
        IsReadOnly = isReadOnly;
        Text = task.Text;
        Assignee = task.Assignee;
        CreatedDisplay = $"{Formatting.Timestamp(task.CreatedAt)} · {task.CreatedBy}";
        ImportanceLabel = Formatting.Level(task.Importance);
        UrgencyLabel = Formatting.Level(task.Urgency);
        IsUrgencyHigh = task.Urgency == TaskUrgency.High;
        IsUrgencyMedium = task.Urgency == TaskUrgency.Medium;
        IsUrgencyLow = task.Urgency == TaskUrgency.Low;
        IsImportanceHigh = task.Importance == TaskImportance.High;
        IsImportanceMedium = task.Importance == TaskImportance.Medium;
        IsImportanceLow = task.Importance == TaskImportance.Low;
        _isDone = task.IsCompleted;

        // German short stamp for completed rows; Sync() recreates the row on completion, so a
        // static snapshot is enough. Empty while open — the view hides the label then.
        CompletedDisplay = task.CompletedAt is { } completedAt
            ? $"ERLEDIGT · {completedAt:HH:mm}"
            : string.Empty;
        RemainingDisplay = ComputeRemaining(task, now);
        IsOverdue = IsOverdueAt(task, now);
    }

    public Guid Id { get; }

    public string Text { get; }

    public string Assignee { get; }

    public string CreatedDisplay { get; }

    public string ImportanceLabel { get; }

    public string UrgencyLabel { get; }

    public bool IsUrgencyHigh { get; }

    public bool IsUrgencyMedium { get; }

    public bool IsUrgencyLow { get; }

    public bool IsImportanceHigh { get; }

    public bool IsImportanceMedium { get; }

    public bool IsImportanceLow { get; }

    public bool IsReadOnly { get; }

    /// <summary>"ERLEDIGT · HH:mm" once done, empty while open (completion time from the task).</summary>
    public string CompletedDisplay { get; }

    [ObservableProperty]
    private bool _isDone;

    partial void OnIsDoneChanged(bool value)
    {
        if (IsReadOnly)
            return;
        var task = _session.Incident.Tasks.FirstOrDefault(t => t.Id == Id);
        if (task is { } current && current.IsCompleted != value)
            _session.SetTaskCompleted(Id, value);
        _onChanged();
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

/// <summary>An enum value paired with its German label (EtbDirectionOption precedent). Two
/// closed records instead of a generic one, so Avalonia compiled-bind templates stay simple.</summary>
public readonly record struct ImportanceOption(TaskImportance Value, string Label);

public readonly record struct UrgencyOption(TaskUrgency Value, string Label);
