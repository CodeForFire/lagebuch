using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// Editor for the Links Stammdaten list — ordered rows of name + URL, unlike
/// <see cref="EditableListSection"/>'s single string per row.
/// </summary>
public sealed partial class LinksSection : EditorSection
{
    private readonly Action _onChanged;

    public LinksSection(string title, IEnumerable<Link> links, Action onChanged) : base(title)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<LinkRow>(
            links.Select(l => new LinkRow(l.Name, l.Url, onChanged)));
    }

    public ObservableCollection<LinkRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        Rows.Add(new LinkRow(string.Empty, string.Empty, _onChanged));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(LinkRow row)
    {
        if (Rows.Remove(row)) _onChanged();
    }

    [RelayCommand]
    private void MoveUp(LinkRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0) { Rows.Move(i, i - 1); _onChanged(); }
    }

    [RelayCommand]
    private void MoveDown(LinkRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1) { Rows.Move(i, i + 1); _onChanged(); }
    }

    /// <summary>Rows with both a non-blank name and URL, trimmed, in current order.</summary>
    public IReadOnlyList<Link> ToValues()
    {
        var result = new List<Link>();
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            var url = row.Url?.Trim() ?? string.Empty;
            if (name.Length > 0 && url.Length > 0) result.Add(new Link(name, url));
        }
        return result;
    }
}
