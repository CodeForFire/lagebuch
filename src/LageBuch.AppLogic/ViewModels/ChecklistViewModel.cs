using CommunityToolkit.Mvvm.ComponentModel;

using LageBuch.Domain;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class ChecklistViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Guid _listId;

    public ChecklistViewModel(IIncidentSession session, ChecklistList list, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(list);
        _session = session;
        _listId = list.Id;
        Title = list.Title;
        IsReadOnly = session.IsReadOnly;
        Items = list.Items
            .Select(item => new ChecklistItemViewModel(
                session, list.Id, item.Id, item.Text, item.IsDone, item.Note, item.IsMandatory, IsReadOnly, onChanged))
            .ToList();
        _allMandatoryDone = ComputeAllMandatoryDone();

        // Recomputed after every change to this incident (local toggle, or a remote broadcast),
        // mirroring ScbaViewModel.UpdateAlarm — this is what the tab header dot binds to.
        session.Changed += Recompute;
    }

    /// <summary>The list's name, as this Einsatz recorded it.</summary>
    public string Title { get; }

    /// <summary>
    /// The name as the view's overline shows it. Upper-cased through the same helper the rail
    /// tab uses, so a Checkliste's heading and its tab can never disagree about casing.
    /// </summary>
    public string TitleDisplay => WorkspaceNavItemViewModel.HeaderFor(Title);

    public bool IsReadOnly { get; }

    public IReadOnlyList<ChecklistItemViewModel> Items { get; }

    [ObservableProperty]
    private bool _allMandatoryDone;

    public void Dispose()
    {
        _session.Changed -= Recompute;
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }

    /// <summary>
    /// The list's current items, resolved by id rather than held by reference: a remote broadcast
    /// replaces the whole <see cref="Incident"/>, so a captured list object would go stale.
    /// </summary>
    internal static IReadOnlyList<ChecklistItem> ItemsFor(Incident incident, Guid listId) =>
        incident.Checklists.FirstOrDefault(l => l.Id == listId)?.Items ?? Array.Empty<ChecklistItem>();

    private void Recompute() => AllMandatoryDone = ComputeAllMandatoryDone();

    private bool ComputeAllMandatoryDone() =>
        ItemsFor(_session.Incident, _listId).Where(i => i.IsMandatory).All(i => i.IsDone);
}

public sealed partial class ChecklistItemViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Guid _listId;
    private readonly Guid _id;
    private readonly Action _onChanged;
    private bool _suppressWriteback;

    public ChecklistItemViewModel(
        IIncidentSession session,
        Guid listId,
        Guid id,
        string text,
        bool isDone,
        string? note,
        bool isMandatory,
        bool isReadOnly,
        Action onChanged)
    {
        _session = session;
        _listId = listId;
        _id = id;
        _onChanged = onChanged;
        Text = text;
        IsMandatory = isMandatory;
        _isDone = isDone;
        _note = note;
        IsReadOnly = isReadOnly;

        // Reflect toggles made elsewhere (another tab, or another device once joined).
        _session.Changed += SyncFromIncident;
    }

    public string Text { get; }

    public bool IsMandatory { get; }

    public bool IsReadOnly { get; }

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string? _note;

    public void Dispose() => _session.Changed -= SyncFromIncident;

    // Driven by the two-way IsChecked binding on the CheckBox. The binding is the single
    // source of truth for IsDone; here we reconcile the domain model and persist. Using a
    // separate Command in addition to the binding would toggle the state twice per click
    // and the visible value would revert, so the checkbox never appeared to persist.
    partial void OnIsDoneChanged(bool value)
    {
        if (IsReadOnly || _suppressWriteback)
            return;
        var item = ChecklistViewModel.ItemsFor(_session.Incident, _listId).FirstOrDefault(c => c.Id == _id);
        if (item is null)
            return;
        if (item.IsDone != value)
            _session.ToggleChecklistItem(_id);
        _onChanged();
    }

    private void SyncFromIncident()
    {
        var item = ChecklistViewModel.ItemsFor(_session.Incident, _listId).FirstOrDefault(c => c.Id == _id);
        if (item is null)
        {
            return;
        }

        _suppressWriteback = true; // this is a state pull, not a user toggle — don't write it back
        IsDone = item.IsDone;
        Note = item.Note;
        _suppressWriteback = false;
    }
}
