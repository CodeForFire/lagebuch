using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for one user-defined Checkliste template — ordered rows of text + mandatory, unlike
/// <see cref="EditableListSection"/>'s single string per row, plus the list's own name.
/// </summary>
/// <remarks>
/// The section's <see cref="EditorSection.Title"/> <em>is</em> the list's name: it is bound
/// two-way from the detail pane, so typing there renames the rail entry as you go. The
/// <see cref="Id"/> is what an Einsatz seeded from this template carries, so it survives renaming
/// and is never regenerated on load.
/// </remarks>
public sealed partial class ChecklistTemplateSection : EditorSection
{
    private readonly Action _onChanged;

    public ChecklistTemplateSection(
        Guid id, string title, IEnumerable<ChecklistTemplateItem> items, Action onChanged)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(items);
        Id = id;
        _onChanged = onChanged;
        Rows = new ObservableCollection<ChecklistTemplateRow>(
            items.Select(i => new ChecklistTemplateRow(i.Text, i.IsMandatory, onChanged)));

        // Title's change hook is generated on EditorSection, so it cannot be implemented here;
        // renaming still has to mark the editor dirty like any other edit.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Title))
            {
                _onChanged();
            }
        };
    }

    /// <summary>The template's stable id, carried into every Einsatz seeded from it.</summary>
    public Guid Id { get; }

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
