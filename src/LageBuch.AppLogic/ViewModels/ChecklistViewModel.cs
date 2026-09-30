using CommunityToolkit.Mvvm.ComponentModel;

using LageBuch.Domain;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class ChecklistViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Guid _listId;
    private readonly Action _onChanged;
    private readonly Dictionary<Guid, ChecklistItemViewModel> _itemsById;

    public ChecklistViewModel(IIncidentSession session, ChecklistList list, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _listId = list.Id;
        _onChanged = onChanged;
        Title = list.Title;
        IsReadOnly = session.IsReadOnly;
        Items = list.Items
            .Select(item => new ChecklistItemViewModel(
                item.Id, item.Text, item.IsDone, item.Note, item.IsMandatory, IsReadOnly, OnItemToggled))
            .ToList();
        _itemsById = Items.ToDictionary(i => i.Id);
        _allMandatoryDone = AllMandatoryDoneIn(ItemsFor(session.Incident, _listId));

        // The one subscription for the whole list: after every change to this incident (local
        // toggle, another tab, or a remote broadcast) it pushes each item's state into its row
        // and recomputes AllMandatoryDone, mirroring ScbaViewModel.UpdateAlarm — this is what
        // the tab header dot binds to.
        session.Changed += SyncFromIncident;
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

    public void Dispose() => _session.Changed -= SyncFromIncident;

    /// <summary>
    /// The list's current items, resolved by id rather than held by reference: a remote broadcast
    /// replaces the whole <see cref="Incident"/>, so a captured list object would go stale.
    /// </summary>
    internal static IReadOnlyList<ChecklistItem> ItemsFor(Incident incident, Guid listId) =>
        incident.Checklists.FirstOrDefault(l => l.Id == listId)?.Items ?? Array.Empty<ChecklistItem>();

    private static bool AllMandatoryDoneIn(IReadOnlyList<ChecklistItem> items) =>
        items.Where(i => i.IsMandatory).All(i => i.IsDone);

    private void SyncFromIncident()
    {
        var items = ItemsFor(_session.Incident, _listId);
        foreach (var item in items)
        {
            if (_itemsById.TryGetValue(item.Id, out var row))
            {
                row.ApplyFromIncident(item);
            }
        }

        AllMandatoryDone = AllMandatoryDoneIn(items);
    }

    // A user click on one row. The toggle raises Changed synchronously, and SyncFromIncident then
    // writes the same value back into the row, which is a no-op rather than a second toggle.
    private void OnItemToggled(Guid itemId, bool isDone)
    {
        var item = ItemsFor(_session.Incident, _listId).FirstOrDefault(c => c.Id == itemId);
        if (item is null)
        {
            return;
        }

        if (item.IsDone != isDone)
        {
            _session.ToggleChecklistItem(itemId);
        }

        _onChanged();
    }
}

/// <summary>
/// One checklist row. It holds no session: the owning <see cref="ChecklistViewModel"/> pushes the
/// incident's state in through <see cref="ApplyFromIncident"/> and receives the user's clicks
/// through the toggle callback.
/// </summary>
public sealed partial class ChecklistItemViewModel : ObservableObject
{
    private readonly Action<Guid, bool> _onToggled;
    private bool _suppressWriteback;

    public ChecklistItemViewModel(
        Guid id,
        string text,
        bool isDone,
        string? note,
        bool isMandatory,
        bool isReadOnly,
        Action<Guid, bool> onToggled)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(onToggled);
        Id = id;
        _onToggled = onToggled;
        Text = text;
        IsMandatory = isMandatory;
        _isDone = isDone;
        _note = note;
        IsReadOnly = isReadOnly;
    }

    public string Text { get; }

    public bool IsMandatory { get; }

    public bool IsReadOnly { get; }

    internal Guid Id { get; }

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string? _note;

    /// <summary>Reflects the incident's state for this item (another tab, or another device once joined).</summary>
    internal void ApplyFromIncident(ChecklistItem item)
    {
        _suppressWriteback = true; // this is a state pull, not a user toggle — don't write it back
        IsDone = item.IsDone;
        Note = item.Note;
        _suppressWriteback = false;
    }

    // Driven by the two-way IsChecked binding on the CheckBox. The binding is the single
    // source of truth for IsDone; the owning list reconciles the domain model and persists.
    // Using a separate Command in addition to the binding would toggle the state twice per
    // click and the visible value would revert, so the checkbox never appeared to persist.
    partial void OnIsDoneChanged(bool value)
    {
        if (IsReadOnly || _suppressWriteback)
        {
            return;
        }

        _onToggled(Id, value);
    }
}
