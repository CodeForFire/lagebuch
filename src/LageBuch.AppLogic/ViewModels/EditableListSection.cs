using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>Editor for a flat, ordered string list (roles, status, ..., and the checklist template).</summary>
public sealed partial class EditableListSection : EditorSection
{
    private readonly Action _onChanged;

    public EditableListSection(string title, IEnumerable<string> values, Action onChanged) : base(title)
    {
        _onChanged = onChanged;
        Items = new ObservableCollection<MasterDataItem>(values.Select(v => new MasterDataItem(v, onChanged)));
    }

    public ObservableCollection<MasterDataItem> Items { get; }

    [RelayCommand]
    private void Add()
    {
        Items.Add(new MasterDataItem(string.Empty, _onChanged));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(MasterDataItem item)
    {
        if (Items.Remove(item)) _onChanged();
    }

    [RelayCommand]
    private void MoveUp(MasterDataItem item)
    {
        var i = Items.IndexOf(item);
        if (i > 0) { Items.Move(i, i - 1); _onChanged(); }
    }

    [RelayCommand]
    private void MoveDown(MasterDataItem item)
    {
        var i = Items.IndexOf(item);
        if (i >= 0 && i < Items.Count - 1) { Items.Move(i, i + 1); _onChanged(); }
    }

    /// <summary>Trimmed, non-empty, de-duplicated (ordinal, first wins), in current order.</summary>
    public IReadOnlyList<string> ToValues()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var item in Items)
        {
            var v = item.Value?.Trim() ?? string.Empty;
            if (v.Length > 0 && seen.Add(v)) result.Add(v);
        }
        return result;
    }
}
