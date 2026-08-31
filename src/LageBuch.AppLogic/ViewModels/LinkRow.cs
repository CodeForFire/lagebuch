using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable Link entry: name plus URL.</summary>
public sealed partial class LinkRow : ObservableObject
{
    private readonly Action _onChanged;

    [SuppressMessage("Design", "CA1054", Justification = "URLs are free-form display strings in the domain; System.Uri would reject non-parseable values like relay links.")]
    public LinkRow(string name, string url, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _url = url;
    }

    [ObservableProperty]
    private string _name;
    [ObservableProperty]
    private string _url;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnUrlChanged(string value) => _onChanged();
}
