using CommunityToolkit.Mvvm.ComponentModel;

using Feuerwehr.Sync;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class ChecklistViewModel : ObservableObject
{
    public ChecklistViewModel(IIncidentSession session, Action onChanged)
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
    private readonly IIncidentSession _session;
    private readonly Guid _id;
    private readonly Action _onChanged;

    public ChecklistItemViewModel(IIncidentSession session, Guid id, string text, bool isDone, string? note, bool isReadOnly, Action onChanged)
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

    // Driven by the two-way IsChecked binding on the CheckBox. The binding is the single
    // source of truth for IsDone; here we reconcile the domain model and persist. Using a
    // separate Command in addition to the binding would toggle the state twice per click
    // and the visible value would revert, so the checkbox never appeared to persist.
    partial void OnIsDoneChanged(bool value)
    {
        if (IsReadOnly)
            return;
        var item = _session.Incident.Checklist.First(c => c.Id == _id);
        if (item.IsDone != value)
            _session.ToggleChecklistItem(_id);
        _onChanged();
    }
}
