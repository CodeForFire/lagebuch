using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Fahrzeug entry: Wache plus Funkrufname and Sitzplätze (#76). Wache and
/// Funkrufname carry suggestion lists from the master data for the view's AutoCompleteBox --
/// free text stays valid, so they are suggestions, not a closed set.
/// </summary>
public sealed partial class VehicleRow : ObservableObject
{
    private readonly Action _onChanged;

    public VehicleRow(
        string wache, string callSign, int seats,
        IReadOnlyList<string> wacheOptions, IReadOnlyList<string> callSignOptions, Action onChanged)
    {
        _onChanged = onChanged;
        _wache = wache;
        _callSign = callSign;
        _seats = seats;
        WacheOptions = wacheOptions;
        CallSignOptions = callSignOptions;
    }

    /// <summary>Suggestions from the Stammdaten "Wachen" list.</summary>
    public IReadOnlyList<string> WacheOptions { get; }

    /// <summary>Suggestions from the Stammdaten "Funkrufnamen" list.</summary>
    public IReadOnlyList<string> CallSignOptions { get; }

    [ObservableProperty] private string _wache;
    [ObservableProperty] private string _callSign;
    [ObservableProperty] private int _seats;

    partial void OnWacheChanged(string value) => _onChanged();
    partial void OnCallSignChanged(string value) => _onChanged();
    partial void OnSeatsChanged(int value) => _onChanged();
}
