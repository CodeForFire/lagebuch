using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Link entry: name, URL and an optional group (#518). The group is free text with
/// the already-known groups as typing suggestions, the way <see cref="VehicleRow"/> offers Wachen.
/// </summary>
public sealed partial class LinkRow : ObservableObject
{
    private readonly Action _onChanged;

    [SuppressMessage("Design", "CA1054", Justification = "URLs are free-form display strings in the domain; System.Uri would reject non-parseable values like relay links.")]
    public LinkRow(string name, string url, string group, IReadOnlyList<string> groupOptions, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _url = url;
        _group = group;
        GroupOptions = groupOptions;
    }

    [ObservableProperty]
    private string _name;
    [ObservableProperty]
    private string _url;
    [ObservableProperty]
    private string _group;

    /// <summary>The groups the saved Stammdaten already use, each once, in first-seen order.</summary>
    public IReadOnlyList<string> GroupOptions { get; }

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnUrlChanged(string value) => _onChanged();

    partial void OnGroupChanged(string value) => _onChanged();
}
