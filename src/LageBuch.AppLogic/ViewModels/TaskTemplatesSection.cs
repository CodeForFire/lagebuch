using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Standardaufgaben: ordered rows of Aufgabe text plus its usual priorities. The
/// order here is the order the chips appear in on the Aufgaben tab.
/// </summary>
public sealed partial class TaskTemplatesSection : EditorSection
{
    private readonly Action _onChanged;

    public TaskTemplatesSection(
        string title, IReadOnlyList<TaskTemplate> templates, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _onChanged = onChanged;
        Rows = new ObservableCollection<TaskTemplateRow>(
            templates.Select(t => new TaskTemplateRow(t.Text, t.Importance, t.Urgency, onChanged)));
    }

    /// <summary>Shared by every row: one list, not a copy each.</summary>
    internal static IReadOnlyList<ImportanceOption> ImportanceChoices { get; } = TasksViewModel.ImportanceLevels();

    /// <inheritdoc cref="ImportanceChoices"/>
    internal static IReadOnlyList<UrgencyOption> UrgencyChoices { get; } = TasksViewModel.UrgencyLevels();

    public ObservableCollection<TaskTemplateRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        var row = new TaskTemplateRow(string.Empty, TaskImportance.Medium, TaskUrgency.Medium, _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(TaskTemplateRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Text, AllBlank(row.Text), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    [RelayCommand]
    private void MoveUp(TaskTemplateRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(TaskTemplateRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }

    /// <summary>
    /// Rows with a non-blank text, trimmed and capped at an Aufgabe's own length, in current
    /// order. A text a chip could not turn into an Aufgabe would only fail later, mid-Einsatz.
    /// </summary>
    public IReadOnlyList<TaskTemplate> ToValues()
    {
        var result = new List<TaskTemplate>();
        foreach (var row in Rows)
        {
            var text = TaskTemplate.CapText(row.Text ?? string.Empty);
            if (text.Length > 0)
            {
                result.Add(new TaskTemplate(text, row.Importance, row.Urgency));
            }
        }

        return result;
    }
}
