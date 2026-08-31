using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Overlay dialog behind each ETB row's "create task" button (#88): the entry's text rides along
/// pre-filled, the operator adds priority/timer/assignee. "Speichern &amp; weiteren Task" keeps the
/// dialog open with cleared text but sticky priorities — the common case is several tasks from
/// one situation report. Closed fires on Speichern AND Abbrechen so the host clears the overlay
/// regardless of outcome (ConfirmDialogViewModel contract).
/// </summary>
public sealed partial class TaskDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;

    public TaskDialogViewModel(
        IIncidentSession session, MasterDataSet masterData, string prefilledText, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _onChanged = onChanged;
        Text = prefilledText;
        AssigneeOptions = masterData.RadioCallSigns
            .Concat(masterData.Roles)
            .Concat(masterData.Personnel.Select(p => $"{p.LastName} {p.FirstName}"))
            .Distinct()
            .ToArray();
        ImportanceOptions = TasksViewModel.ImportanceLevels();
        UrgencyOptions = TasksViewModel.UrgencyLevels();
        _timerMinutes = IncidentTask.DefaultTimerMinutes(Urgency);
    }

    public event EventHandler? Closed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndCreateAnotherCommand))]
    private string _text;

    [ObservableProperty]
    private string? _assignee;

    public IReadOnlyList<string> AssigneeOptions { get; }

    [ObservableProperty]
    private TaskImportance _importance = TaskImportance.Medium;

    [ObservableProperty]
    private TaskUrgency _urgency = TaskUrgency.Medium;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndCreateAnotherCommand))]
    private int? _timerMinutes;

    partial void OnUrgencyChanged(TaskUrgency value) =>
        TimerMinutes = IncidentTask.DefaultTimerMinutes(value);

    public IReadOnlyList<ImportanceOption> ImportanceOptions { get; }

    public IReadOnlyList<UrgencyOption> UrgencyOptions { get; }

    private bool CanSave =>
        !_session.IsReadOnly && !string.IsNullOrWhiteSpace(Text) && TimerMinutes is >= 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _session.AddTask(Text, Assignee, Importance, Urgency, TimerMinutes!.Value);
        _onChanged();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAndCreateAnother()
    {
        _session.AddTask(Text, Assignee, Importance, Urgency, TimerMinutes!.Value);
        _onChanged();
        Text = string.Empty; // fields besides the text stay sticky for the next entry
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);
}
