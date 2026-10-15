using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Textbausteine: ordered rows of Kürzel, text and Kategorie. The order is the
/// order the suggestions list them in. A Kürzel that appears twice is named on screen rather than
/// silently dropped on save, because only the operator knows which of the two was meant.
/// </summary>
public sealed partial class TextSnippetsSection : EditorSection
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<string> _categoryOptions;

    // withPriorities is true for the Aufgaben list: its rows carry the Wichtigkeit and Dringlichkeit
    // a new Aufgabe takes from them. The ETB list has none, and saves every row at Mittel.
    public TextSnippetsSection(
        string title,
        IReadOnlyList<TextSnippet> snippets,
        Action onChanged,
        Action<string, Action>? requestConfirm = null,
        bool withPriorities = false)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        HasPriorities = withPriorities;
        _onChanged = () =>
        {
            RefreshConflicts();
            onChanged();
        };
        _categoryOptions = snippets
            .Select(s => s.Category)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Rows = new ObservableCollection<TextSnippetRow>(
            snippets.Select(s => new TextSnippetRow(
                s.Shortcut, s.Text, s.Category, _categoryOptions, _onChanged, withPriorities, s.Importance, s.Urgency)));
        RefreshConflicts();
    }

    /// <summary>Shared by every row: one list, not a copy each.</summary>
    internal static IReadOnlyList<ImportanceOption> ImportanceChoices { get; } = TasksViewModel.ImportanceLevels();

    /// <inheritdoc cref="ImportanceChoices"/>
    internal static IReadOnlyList<UrgencyOption> UrgencyChoices { get; } = TasksViewModel.UrgencyLevels();

    /// <summary>Whether the rows carry priorities -- the Aufgaben list does, the ETB list does not.</summary>
    public bool HasPriorities { get; }

    public ObservableCollection<TextSnippetRow> Rows { get; }

    /// <summary>"Doppelte Kürzel: stl, abg", or null while every Kürzel is unique.</summary>
    [ObservableProperty]
    private string? _conflicts;

    [RelayCommand]
    private void Add()
    {
        var row = new TextSnippetRow(string.Empty, string.Empty, string.Empty, _categoryOptions, _onChanged, HasPriorities);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(TextSnippetRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Shortcut, AllBlank(row.Shortcut, row.Text, row.Category), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    [RelayCommand]
    private void MoveUp(TextSnippetRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(TextSnippetRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }

    /// <summary>
    /// The rows as they will be saved, through the same normalization every other way in uses:
    /// Kürzel lower case and letters/digits only, text capped, incomplete rows dropped.
    /// </summary>
    public IReadOnlyList<TextSnippet> ToValues() =>
        TextSnippet.Normalize(Rows.Select(r => new TextSnippet(
            r.Shortcut ?? string.Empty,
            r.Text ?? string.Empty,
            r.Category ?? string.Empty,
            HasPriorities ? r.Importance : TaskImportance.Medium,
            HasPriorities ? r.Urgency : TaskUrgency.Medium)));

    private void RefreshConflicts()
    {
        var duplicates = Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => TextSnippet.NormalizeShortcut(r.Shortcut))
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        Conflicts = duplicates.Length == 0 ? null : $"Doppelte Kürzel: {string.Join(", ", duplicates)}";
    }
}
