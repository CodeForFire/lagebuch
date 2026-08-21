using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable Link entry: name plus URL.</summary>
public sealed partial class LinkRow : ObservableObject
{
    private readonly Action _onChanged;

    public LinkRow(string name, string url, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _url = url;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _url;

    partial void OnNameChanged(string value) => _onChanged();
    partial void OnUrlChanged(string value) => _onChanged();
}
