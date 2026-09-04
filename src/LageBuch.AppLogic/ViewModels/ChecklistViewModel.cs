using CommunityToolkit.Mvvm.ComponentModel;

using LageBuch.Domain;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class ChecklistViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly ChecklistKind _kind;

    public ChecklistViewModel(IIncidentSession session, ChecklistKind kind, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _kind = kind;
        IsReadOnly = session.IsReadOnly;
        Items = ItemsFor(session.Incident, kind)
            .Select(item => new ChecklistItemViewModel(
                session, kind, item.Id, item.Text, item.IsDone, item.Note, item.IsMandatory, IsReadOnly, onChanged))
            .ToList();
        _allMandatoryDone = ComputeAllMandatoryDone();

        // Recomputed after every change to this incident (local toggle, or a remote broadcast),
        // mirroring ScbaViewModel.UpdateAlarm — this is what the AUFBAU/ABBAU tab header dot binds to.
        session.Changed += Recompute;
    }

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

    private void Recompute() => AllMandatoryDone = ComputeAllMandatoryDone();

    private bool ComputeAllMandatoryDone() =>
        ItemsFor(_session.Incident, _kind).Where(i => i.IsMandatory).All(i => i.IsDone);

    internal static IReadOnlyList<ChecklistItem> ItemsFor(Incident incident, ChecklistKind kind) =>
        kind == ChecklistKind.Aufbau ? incident.ChecklistAufbau : incident.ChecklistAbbau;
}

public sealed partial class ChecklistItemViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly ChecklistKind _kind;
    private readonly Guid _id;
    private readonly Action _onChanged;
    private bool _suppressWriteback;

    public ChecklistItemViewModel(
        IIncidentSession session,
        ChecklistKind kind,
        Guid id,
        string text,
        bool isDone,
        string? note,
        bool isMandatory,
        bool isReadOnly,
        Action onChanged)
    {
        _session = session;
        _kind = kind;
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

    public void Dispose() => _session.Changed -= SyncFromIncident;

    private void SyncFromIncident()
    {
        var item = ChecklistViewModel.ItemsFor(_session.Incident, _kind).FirstOrDefault(c => c.Id == _id);
        if (item is null)
        {
            return;
        }

        _suppressWriteback = true; // this is a state pull, not a user toggle — don't write it back
        IsDone = item.IsDone;
        Note = item.Note;
        _suppressWriteback = false;
    }

    public string Text { get; }

    public bool IsMandatory { get; }

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
        if (IsReadOnly || _suppressWriteback)
            return;
        var item = ChecklistViewModel.ItemsFor(_session.Incident, _kind).First(c => c.Id == _id);
        if (item.IsDone != value)
            _session.ToggleChecklistItem(_id);
        _onChanged();
    }
}
