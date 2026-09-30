using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One collapsible group on the Links tab (#518): the links of <see cref="LinksViewModel.VisibleLinks"/>
/// that share a <see cref="Link.Group"/>. Rebuilt on every filter change, so the expand state the
/// Lagebuchführer chose lives in <see cref="LinksViewModel"/>, which this reports each change to.
/// </summary>
public sealed partial class LinkGroupViewModel : ObservableObject
{
    public const string UngroupedName = "Ohne Gruppe";

    private readonly Action<LinkGroupViewModel> _onExpandedChanged;

    public LinkGroupViewModel(string key, IReadOnlyList<Link> links, bool isExpanded, Action<LinkGroupViewModel> onExpandedChanged)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(links);
        Key = key;
        Links = links;
        _isExpanded = isExpanded;
        _onExpandedChanged = onExpandedChanged;
    }

    /// <summary>The group as the Stammdaten spell it first; empty for the ungrouped links.</summary>
    public string Key { get; }

    public string Name => Key.Length == 0 ? UngroupedName : Key;

    public IReadOnlyList<Link> Links { get; }

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => _onExpandedChanged(this);

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
