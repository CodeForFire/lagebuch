using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain.Tasks;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Standardaufgabe: the Aufgabe's text and the Wichtigkeit and Dringlichkeit it
/// usually carries. The pickers offer the same German labels as the Aufgaben tab.
/// </summary>
public sealed partial class TaskTemplateRow : ObservableObject
{
    private readonly Action _onChanged;

    public TaskTemplateRow(string text, TaskImportance importance, TaskUrgency urgency, Action onChanged)
    {
        _onChanged = onChanged;
        _text = text;
        _importance = importance;
        _urgency = urgency;
    }

    /// <summary>The levels as the Wichtigkeit picker offers them (instance for compiled bindings).</summary>
    public IReadOnlyList<ImportanceOption> ImportanceOptions { get; } = TaskTemplatesSection.ImportanceChoices;

    /// <summary>The levels as the Dringlichkeit picker offers them.</summary>
    public IReadOnlyList<UrgencyOption> UrgencyOptions { get; } = TaskTemplatesSection.UrgencyChoices;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private TaskImportance _importance;

    [ObservableProperty]
    private TaskUrgency _urgency;

    partial void OnTextChanged(string value) => _onChanged();

    partial void OnImportanceChanged(TaskImportance value) => _onChanged();

    partial void OnUrgencyChanged(TaskUrgency value) => _onChanged();
}
