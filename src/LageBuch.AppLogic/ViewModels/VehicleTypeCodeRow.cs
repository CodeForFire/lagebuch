using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One Fahrzeugkennung: the "12" of "FFB 1/12/1" and the Fahrzeugtyp it stands for, "MZF".</summary>
public sealed partial class VehicleTypeCodeRow : ObservableObject
{
    private readonly Action _onChanged;

    public VehicleTypeCodeRow(string kennung, string? vehicleType, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        ArgumentNullException.ThrowIfNull(onChanged);
        _onChanged = onChanged;
        _kennung = kennung;
        _vehicleType = vehicleType;
    }

    [ObservableProperty]
    private string _kennung;

    // Nullable: the type is an AutoCompleteBox, which writes null back when it is cleared.
    [ObservableProperty]
    private string? _vehicleType;

    partial void OnKennungChanged(string value) => _onChanged();

    partial void OnVehicleTypeChanged(string? value) => _onChanged();
}
