using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Straße entry (#461): the name, and the Ortsteil, PLZ and Ort it lies in where
/// known. The Ortsteil carries the Ortsteile as suggestions; free text stays valid.
/// </summary>
public sealed partial class StreetRow : ObservableObject
{
    private readonly Action _onChanged;

    public StreetRow(
        string name,
        string district,
        string postalCode,
        string place,
        IReadOnlyList<string> districtOptions,
        Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _district = district;
        _postalCode = postalCode;
        _place = place;
        DistrictOptions = districtOptions;
    }

    /// <summary>Suggestions: the Ortsteile as loaded.</summary>
    public IReadOnlyList<string> DistrictOptions { get; }

    [ObservableProperty]
    private string _name;
    [ObservableProperty]
    private string _district;
    [ObservableProperty]
    private string _postalCode;
    [ObservableProperty]
    private string _place;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnDistrictChanged(string value) => _onChanged();

    partial void OnPostalCodeChanged(string value) => _onChanged();

    partial void OnPlaceChanged(string value) => _onChanged();
}
