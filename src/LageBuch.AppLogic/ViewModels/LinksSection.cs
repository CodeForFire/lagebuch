using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Links Stammdaten list — ordered rows of name + URL, unlike
/// <see cref="EditableListSection"/>'s single string per row.
/// </summary>
public sealed partial class LinksSection : EditorSection
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<string> _groupOptions;

    public LinksSection(string title, IReadOnlyList<Link> links, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(links);
        _onChanged = onChanged;

        // Case-insensitive like the Links tab's grouping, so the suggestions never offer a second
        // spelling of a group the tab would merge anyway.
        _groupOptions = links
            .Select(l => l.Group)
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Rows = new ObservableCollection<LinkRow>(
            links.Select(l => new LinkRow(l.Name, l.Url, l.Group, _groupOptions, onChanged)));
    }

    public ObservableCollection<LinkRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        var row = new LinkRow(string.Empty, string.Empty, string.Empty, _groupOptions, _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(LinkRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row.Name, AllBlank(row.Name, row.Url, row.Group), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    [RelayCommand]
    private void MoveUp(LinkRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(LinkRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }

    /// <summary>Rows with both a non-blank name and URL, trimmed, in current order.</summary>
    public IReadOnlyList<Link> ToValues()
    {
        var result = new List<Link>();
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            var url = row.Url?.Trim() ?? string.Empty;
            if (name.Length > 0 && url.Length > 0)
            {
                result.Add(new Link(name, url, row.Group?.Trim() ?? string.Empty));
            }
        }

        return result;
    }
}
