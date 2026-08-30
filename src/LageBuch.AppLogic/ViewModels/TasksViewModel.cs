using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Documents;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

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
        IIncidentSession session, IClock clock, ITicker ticker, IAlarmService alarm,
        MasterDataSet masterData, Action onChanged)
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
            .OrderByDescending(v => Convert.ToInt32(v))
            .Select(v => new ImportanceOption(v, Formatting.Level(v)))
            .ToArray();

    internal static IReadOnlyList<UrgencyOption> UrgencyLevels() =>
        Enum.GetValues<TaskUrgency>()
            .OrderByDescending(v => Convert.ToInt32(v))
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
        set { if (value) Filter = TaskFilterKind.Open; }
    }

    public bool IsDoneFilter
    {
        get => Filter == TaskFilterKind.Done;
        set { if (value) Filter = TaskFilterKind.Done; }
    }

    public bool IsAllFilter
    {
        get => Filter == TaskFilterKind.All;
        set { if (value) Filter = TaskFilterKind.All; }
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private string _newText = string.Empty;

    [ObservableProperty]
    private string? _newAssignee;

    [ObservableProperty]
    private TaskImportance _newImportance = TaskImportance.Medium;

    [ObservableProperty]
    private TaskUrgency _newUrgency = TaskUrgency.Medium;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private int? _newTimerMinutes = IncidentTask.DefaultTimerMinutes(TaskUrgency.Medium);

    // The minutes field follows the urgency's default; an explicit override survives until the
    // urgency selection itself changes again.
    partial void OnNewUrgencyChanged(TaskUrgency value) =>
        NewTimerMinutes = IncidentTask.DefaultTimerMinutes(value);

    private bool CanAddTask =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewText) && NewTimerMinutes is >= 0;

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void AddTask()
    {
        _session.AddTask(NewText, NewAssignee, NewImportance, NewUrgency, NewTimerMinutes!.Value);
        NewText = string.Empty; // priorities stay sticky for rapid follow-up entries
        _onChanged();
    }

    // --- Live countdown + one-shot due alarm ---

    private void OnTick()
    {
        var now = _clock.Now;
        foreach (var row in Rows)
            row.RefreshClock(now);

        // Audible cue on the open->due crossing, exactly once per task per VM lifetime. Runs on
        // joined clients too — the sound is local feedback, not logging (IsRemote gates writes).
        // Read-only workspaces (closed/reopened incidents) stay silent: stale overdue tasks must
        // not beep forever at someone reading history.
        if (!IsReadOnly)
        {
            foreach (var task in _session.Incident.Tasks)
                if (!task.IsCompleted && task.DueAt <= now && _dueAnnounced.Add(task.Id))
                    _alarm.Play(AlarmSound.TaskDue);
        }
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

        Rows.Clear();
        foreach (var row in visible)
            Rows.Add(row);
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
    private readonly Guid _id;
    private readonly Action _onChanged;

    public TaskRow(IIncidentSession session, IncidentTask task, bool isReadOnly, DateTimeOffset now, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(task);
        _session = session;
        _id = task.Id;
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
        IsOverdue = ComputeIsOverdue(task, now);
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
        var task = _session.Incident.Tasks.FirstOrDefault(t => t.Id == _id);
        if (task is { } current && current.IsCompleted != value)
            _session.SetTaskCompleted(_id, value);
        _onChanged();
    }

    /// <summary>True while the task is open and past its due time. Order is unaffected by design.</summary>
    public bool IsOverdue { get; private set; }

    [ObservableProperty]
    private string _remainingDisplay;

    /// <summary>Called by the owning VM on every ticker tick — recomputes countdown + overdue.</summary>
    public void RefreshClock(DateTimeOffset now)
    {
        var task = _session.Incident.Tasks.FirstOrDefault(t => t.Id == _id);
        if (task is null)
            return;
        IsOverdue = ComputeIsOverdue(task, now);
        RemainingDisplay = ComputeRemaining(task, now);
        OnPropertyChanged(nameof(IsOverdue));
    }

    private static bool ComputeIsOverdue(IncidentTask task, DateTimeOffset now) =>
        !task.IsCompleted && task.DueAt != DateTimeOffset.MaxValue && task.DueAt <= now;

    private static string ComputeRemaining(IncidentTask task, DateTimeOffset now)
    {
        if (task.IsCompleted)
            return "–";
        if (task.DueAt == DateTimeOffset.MaxValue)
            return "–";
        if (task.DueAt <= now)
            return "FÄLLIG";
        var remaining = task.DueAt - now;
        return $"noch {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }
}
