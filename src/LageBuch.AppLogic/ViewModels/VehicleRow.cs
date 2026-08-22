using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable Fahrzeug entry: Wache plus Funkrufname and Sitzplätze (#76).</summary>
public sealed partial class VehicleRow : ObservableObject
{
    private readonly Action _onChanged;

    public VehicleRow(string wache, string callSign, int seats, Action onChanged)
    {
        _onChanged = onChanged;
        _wache = wache;
        _callSign = callSign;
        _seats = seats;
    }

    [ObservableProperty] private string _wache;
    [ObservableProperty] private string _callSign;
    [ObservableProperty] private int _seats;

    partial void OnWacheChanged(string value) => _onChanged();
    partial void OnCallSignChanged(string value) => _onChanged();
    partial void OnSeatsChanged(int value) => _onChanged();
}
