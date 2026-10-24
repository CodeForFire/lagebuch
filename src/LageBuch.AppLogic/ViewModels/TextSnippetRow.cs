using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Textbaustein: the Kürzel typed after <c>::</c>, the text it stands for, and an
/// optional Kategorie with the known ones as suggestions, the way <see cref="LinkRow"/> offers groups,
/// and one checkbox per area that offers it.
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
        bool forEtb = true,
        bool forTasks = true)
    {
        _onChanged = onChanged;
        _shortcut = shortcut;
        _text = text;
        _category = category;
        _forEtb = forEtb;
        _forTasks = forTasks;
        CategoryOptions = categoryOptions;
    }

    [ObservableProperty]
    private string _shortcut;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private string _category;

    /// <summary>Whether the ETB fields offer this Textbaustein.</summary>
    [ObservableProperty]
    private bool _forEtb;

    /// <summary>Whether the Aufgabe fields offer this Textbaustein.</summary>
    [ObservableProperty]
    private bool _forTasks;

    /// <summary>The Kategorien the saved Stammdaten already use, each once, in first-seen order.</summary>
    public IReadOnlyList<string> CategoryOptions { get; }

    partial void OnShortcutChanged(string value) => _onChanged();

    partial void OnTextChanged(string value) => _onChanged();

    partial void OnCategoryChanged(string value) => _onChanged();

    partial void OnForEtbChanged(bool value) => _onChanged();

    partial void OnForTasksChanged(bool value) => _onChanged();
}
