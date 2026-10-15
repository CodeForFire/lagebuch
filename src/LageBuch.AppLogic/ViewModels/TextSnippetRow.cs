using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain.Tasks;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Textbaustein: the Kürzel typed after <c>::</c>, the text it stands for, and an
/// optional Kategorie with the known ones as suggestions, the way <see cref="LinkRow"/> offers groups.
/// A row of the Aufgaben list also carries the Wichtigkeit and Dringlichkeit a new Aufgabe takes
/// from it (<see cref="ShowsPriorities"/>); the ETB list's rows have none.
/// </summary>
public sealed partial class TextSnippetRow : ObservableObject
{
    private readonly Action _onChanged;

    public TextSnippetRow(
        string shortcut,
        string text,
        string category,
        IReadOnlyList<string> categoryOptions,
        Action onChanged,
        bool showsPriorities = false,
        TaskImportance importance = TaskImportance.Medium,
        TaskUrgency urgency = TaskUrgency.Medium)
    {
        _onChanged = onChanged;
        _shortcut = shortcut;
        _text = text;
        _category = category;
        _importance = importance;
        _urgency = urgency;
        CategoryOptions = categoryOptions;
        ShowsPriorities = showsPriorities;
    }

    /// <summary>Whether this row's list gives out Aufgaben, so its pickers are shown and saved.</summary>
    public bool ShowsPriorities { get; }

    /// <summary>The levels as the Wichtigkeit picker offers them (instance for compiled bindings).</summary>
    public IReadOnlyList<ImportanceOption> ImportanceOptions { get; } = TextSnippetsSection.ImportanceChoices;

    /// <summary>The levels as the Dringlichkeit picker offers them.</summary>
    public IReadOnlyList<UrgencyOption> UrgencyOptions { get; } = TextSnippetsSection.UrgencyChoices;

    [ObservableProperty]
    private TaskImportance _importance;

    [ObservableProperty]
    private TaskUrgency _urgency;

    partial void OnImportanceChanged(TaskImportance value) => _onChanged();

    partial void OnUrgencyChanged(TaskUrgency value) => _onChanged();

    [ObservableProperty]
    private string _shortcut;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private string _category;

    /// <summary>The Kategorien the saved Stammdaten already use, each once, in first-seen order.</summary>
    public IReadOnlyList<string> CategoryOptions { get; }

    partial void OnShortcutChanged(string value) => _onChanged();

    partial void OnTextChanged(string value) => _onChanged();

    partial void OnCategoryChanged(string value) => _onChanged();
}
