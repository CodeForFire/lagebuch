using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for one Checkliste template list (Aufbau or Abbau) — ordered rows of text + mandatory,
/// unlike <see cref="EditableListSection"/>'s single string per row.
/// </summary>
public sealed partial class ChecklistTemplateSection : EditorSection
{
    private readonly Action _onChanged;

    public ChecklistTemplateSection(string title, IEnumerable<ChecklistTemplateItem> items, Action onChanged)
        : base(title)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<ChecklistTemplateRow>(
            items.Select(i => new ChecklistTemplateRow(i.Text, i.IsMandatory, onChanged)));
    }

    public ObservableCollection<ChecklistTemplateRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        Rows.Add(new ChecklistTemplateRow(string.Empty, false, _onChanged));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(ChecklistTemplateRow row)
    {
        if (Rows.Remove(row))
        {
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveUp(ChecklistTemplateRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(ChecklistTemplateRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }

    /// <summary>
    /// Rows with non-blank text, trimmed, in current order. Unlike the flat lists' ToValues, this
    /// does not de-duplicate — repeating a checklist step under a different mandatory flag is
    /// plausible, and ordinal position (not identity) is what a checklist means.
    /// </summary>
    public IReadOnlyList<ChecklistTemplateItem> ToValues()
    {
        var result = new List<ChecklistTemplateItem>();
        foreach (var row in Rows)
        {
            var text = row.Text?.Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                result.Add(new ChecklistTemplateItem(text, row.IsMandatory));
            }
        }

        return result;
    }
}
