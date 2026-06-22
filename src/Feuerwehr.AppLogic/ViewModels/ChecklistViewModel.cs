using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class ChecklistViewModel : ObservableObject
{
    public ChecklistViewModel(IncidentSession session, Action onChanged)
    {
        IsReadOnly = session.IsReadOnly;
        Items = session.Incident.Checklist
            .Select(item => new ChecklistItemViewModel(session, item.Id, item.Text, item.IsDone, item.Note, IsReadOnly, onChanged))
            .ToList();
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<ChecklistItemViewModel> Items { get; }
}

public sealed partial class ChecklistItemViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly Guid _id;
    private readonly Action _onChanged;

    public ChecklistItemViewModel(IncidentSession session, Guid id, string text, bool isDone, string? note, bool isReadOnly, Action onChanged)
    {
        _session = session;
        _id = id;
        _onChanged = onChanged;
        Text = text;
        _isDone = isDone;
        _note = note;
        IsReadOnly = isReadOnly;
    }

    public string Text { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string? _note;

    private bool CanToggle => !IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private void Toggle()
    {
        _session.Incident.ToggleChecklistItem(_id);
        IsDone = !IsDone;
        _onChanged();
    }
}
