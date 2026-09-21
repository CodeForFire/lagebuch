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

    // Quiet until the first press: a dialog that opens already scolding teaches nothing.
    private bool _errorsShown;

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
    [NotifyPropertyChangedFor(nameof(TextError))]
    private string _text;

    [ObservableProperty]
    private string? _assignee;

    public IReadOnlyList<string> AssigneeOptions { get; }

    [ObservableProperty]
    private TaskImportance _importance = TaskImportance.Medium;

    [ObservableProperty]
    private TaskUrgency _urgency = TaskUrgency.Medium;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimerMinutesError))]
    private int? _timerMinutes;

    partial void OnUrgencyChanged(TaskUrgency value) =>
        TimerMinutes = IncidentTask.DefaultTimerMinutes(value);

    public IReadOnlyList<ImportanceOption> ImportanceOptions { get; }

    public IReadOnlyList<UrgencyOption> UrgencyOptions { get; }

    /// <summary>Whether the Aufgabe is still missing, once the operator has asked (#412).</summary>
    public string? TextError =>
        _errorsShown && string.IsNullOrWhiteSpace(Text) ? ValidationMessages.Required : null;

    /// <summary>Whether the timer box is empty or negative, once the operator has asked (#412).</summary>
    public string? TimerMinutesError =>
        _errorsShown && TimerMinutes is not >= 0 ? ValidationMessages.TimerMinutes : null;

    // Only the read-only rule gates the buttons. A missing field leaves them live on purpose: the
    // press is how the operator asks what is still needed, and a grey button answers nothing (#412).
    private bool CanSave => !_session.IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!Validate())
        {
            return;
        }

        _session.AddTask(Text, Assignee, Importance, Urgency, TimerMinutes!.Value);
        _onChanged();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAndCreateAnother()
    {
        if (!Validate())
        {
            return;
        }

        _session.AddTask(Text, Assignee, Importance, Urgency, TimerMinutes!.Value);
        _onChanged();
        Text = string.Empty; // fields besides the text stay sticky for the next entry
        ShowErrors(false); // ...and the cleared text must not read as a fresh complaint
    }

    /// <summary>
    /// Turns the field messages on and reports whether the form may be saved. The save commands
    /// call this instead of being gated on it, so a press that cannot succeed still says why.
    /// </summary>
    private bool Validate()
    {
        ShowErrors(true);
        return TextError is null && TimerMinutesError is null;
    }

    private void ShowErrors(bool shown)
    {
        _errorsShown = shown;
        OnPropertyChanged(nameof(TextError));
        OnPropertyChanged(nameof(TimerMinutesError));
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);
}
