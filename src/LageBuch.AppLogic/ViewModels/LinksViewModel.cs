using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Quick-access list of the global Links Stammdaten, shown as a read-only tab in the incident
/// workspace so a link can be opened while working an Einsatz. Unlike <see cref="FilesViewModel"/>
/// this holds no <see cref="LageBuch.Sync.IIncidentSession"/> and mutates nothing — Links are
/// global master data, not incident state, so opening one is not an incident action.
/// </summary>
public sealed partial class LinksViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;

    /// <summary>
    /// The groups the Lagebuchführer collapsed, by the case-insensitive key the grouping uses. Kept
    /// here rather than on <see cref="LinkGroupViewModel"/> because the groups are rebuilt on every
    /// keystroke in the search box, and a search must not forget what was collapsed before it.
    /// </summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);

    public LinksViewModel(IReadOnlyList<Link> links, IFileDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(links);
        _dialogs = dialogs;
        Links = links;
        VisibleLinks = new ObservableCollection<Link>(links);
        ShowGroupHeaders = links.Any(l => l.Group.Length > 0);
        RebuildGroups();
    }

    public IReadOnlyList<Link> Links { get; }

    /// <summary>
    /// The subset of <see cref="Links"/> matching <see cref="FilterText"/>, and what the view
    /// renders. <see cref="Links"/> deliberately stays the full set so "Keine Links hinterlegt"
    /// (nothing in the Stammdaten) stays distinguishable from "nothing matched what you typed" —
    /// two different situations for the operator. Same _all/visible split as
    /// <see cref="RolesViewModel"/> and <see cref="EtbViewModel"/>.
    /// </summary>
    public ObservableCollection<Link> VisibleLinks { get; }

    /// <summary>
    /// <see cref="VisibleLinks"/> gathered by <see cref="Link.Group"/> (#518). Groups appear where
    /// their first link sits in the Stammdaten, so the order the editor's up/down buttons set is the
    /// order on screen; the ungrouped links come last under "Ohne Gruppe". Groups whose names differ
    /// only in case are one group — a typo in the editor should not split Gefahrgut in two.
    /// </summary>
    public ObservableCollection<LinkGroupViewModel> VisibleGroups { get; } = new();

    /// <summary>
    /// False while no link has a group: the tab then shows one headerless group, which is exactly
    /// the flat list a Wehr without groups had before #518.
    /// </summary>
    public bool ShowGroupHeaders { get; }

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Live search over the Links tab (#262): a Wehr with more than a handful of Stammdaten-Links
    /// otherwise has to scan the whole flat list while working an Einsatz.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered))]
    [NotifyPropertyChangedFor(nameof(NoMatchesMessage))]
    private string _filterText = string.Empty;

    /// <summary>
    /// Whether a search term is active — drives the clear button, so it follows the typed text and
    /// not the result count: a term that happens to match every link is still an active filter.
    /// </summary>
    public bool IsFiltered => !string.IsNullOrWhiteSpace(FilterText);

    public string NoMatchesMessage => $"Kein Link passt zu „{FilterText.Trim()}“.";

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Ordinal rather than culture-aware matching: a URL is not culture text, and this mirrors the
    /// <c>FilterMode="ContainsOrdinal"</c> the AutoCompleteBoxes elsewhere in the app already use.
    /// </summary>
    private void ApplyFilter()
    {
        var term = FilterText.Trim();
        VisibleLinks.Clear();
        foreach (var link in Links)
        {
            if (Matches(link, term))
            {
                VisibleLinks.Add(link);
            }
        }

        RebuildGroups();
    }

    private static bool Matches(Link link, string term) =>
        term.Length == 0
        || link.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || link.Url.Contains(term, StringComparison.OrdinalIgnoreCase)
        || link.Group.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// While a search is active every shown group is open, since a hit inside a collapsed group
    /// would read as no hit at all; what the Lagebuchführer collapses meanwhile is not remembered.
    /// </summary>
    private void RebuildGroups()
    {
        var filtered = IsFiltered;
        var groups = new List<(string Key, List<Link> Links)>();
        foreach (var link in VisibleLinks)
        {
            var i = groups.FindIndex(g => string.Equals(g.Key, link.Group, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                groups.Add((link.Group, new List<Link> { link }));
            }
            else
            {
                groups[i].Links.Add(link);
            }
        }

        VisibleGroups.Clear();
        foreach (var (key, links) in groups.OrderBy(g => g.Key.Length == 0))
        {
            VisibleGroups.Add(new LinkGroupViewModel(key, links, filtered || !_collapsed.Contains(key), OnGroupExpandedChanged));
        }
    }

    private void OnGroupExpandedChanged(LinkGroupViewModel group)
    {
        if (IsFiltered)
        {
            return;
        }

        if (group.IsExpanded)
        {
            _collapsed.Remove(group.Key);
        }
        else
        {
            _collapsed.Add(group.Key);
        }
    }

    [RelayCommand]
    private void ExpandAll() => SetAllExpanded(true);

    [RelayCommand]
    private void CollapseAll() => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        foreach (var group in VisibleGroups)
        {
            group.IsExpanded = expanded;
        }
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    /// <summary>
    /// Refuses anything but http(s) before it reaches the OS: on desktop, OpenUrlAsync ultimately
    /// runs Process.Start with UseShellExecute=true, which resolves arbitrary URI handlers and even
    /// local executable paths, and a Link's URL can come from an imported Stammdaten JSON file, not
    /// just what the user themselves typed here.
    /// </summary>
    [RelayCommand]
    private async Task OpenAsync(Link link)
    {
        // Bare-domain normalization is this caller's job, not the validator's: a Stammdaten Link
        // is routinely typed as "example.com". The link's display name is what the error names —
        // the URL itself is not on screen in the Links list.
        var candidate = link.Url.Contains("://", StringComparison.Ordinal) ? link.Url : $"https://{link.Url}";
        ErrorMessage = await UrlLauncher.TryOpenAsync(_dialogs, candidate, link.Name);
    }
}
