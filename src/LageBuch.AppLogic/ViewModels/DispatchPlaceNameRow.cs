using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One place name of the alarm: "Fürstenfeldbruck" as the Leitstelle writes it, "FFB" as the Funkrufnamen do.</summary>
public sealed partial class DispatchPlaceNameRow : ObservableObject
{
    private readonly Action _onChanged;

    public DispatchPlaceNameRow(string name, string abbreviation, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _abbreviation = abbreviation;
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _abbreviation;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnAbbreviationChanged(string value) => _onChanged();
}
