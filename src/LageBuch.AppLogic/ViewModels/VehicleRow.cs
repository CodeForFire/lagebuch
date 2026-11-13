using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Fahrzeug entry: Wache plus Funkrufname, Sitzplätze (#76), ZF-Flag and whether
/// the vehicle is the brigade's own (#458), plus an optional Fahrzeugtelefon and E-Mail and the
/// Fahrzeugtyp of its taktisches Zeichen. Wache and Funkrufname carry suggestion lists from the
/// master data for the view's AutoCompleteBox, the Fahrzeugtyp the codes of
/// <see cref="VehicleTypes"/> -- free text stays valid, so they are suggestions, not a closed set.
/// An empty Fahrzeugtyp fills in from the Fahrzeugkennungen as the Funkrufname is typed; a type
/// already there is never replaced.
/// </summary>
public sealed partial class VehicleRow : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Func<string, string?>? _typeForCallSign;

    public VehicleRow(
        string wache,
        string callSign,
        int seats,
        bool hasZugfuehrer,
        bool isOwn,
        string? phone,
        string? email,
        string? vehicleType,
        IReadOnlyList<string> wacheOptions,
        IReadOnlyList<string> callSignOptions,
        Action onChanged,
        Func<string, string?>? typeForCallSign = null)
    {
        _onChanged = onChanged;
        _typeForCallSign = typeForCallSign;
        _wache = wache;
        _callSign = callSign;
        _seats = seats;
        _hasZugfuehrer = hasZugfuehrer;
        _isOwn = isOwn;
        _phone = phone;
        _email = email;
        _vehicleType = vehicleType;
        WacheOptions = wacheOptions;
        CallSignOptions = callSignOptions;
    }

    /// <summary>Suggestions: the Wachen of the vehicles as loaded (derived, not a separate list).</summary>
    public IReadOnlyList<string> WacheOptions { get; }

    /// <summary>Suggestions: the Funkrufnamen of the vehicles and roster as loaded (derived, not a separate list).</summary>
    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>Suggestions: every Fahrzeugtyp the Lagekarte has a sign for, alphabetically.</summary>
    public static IReadOnlyList<string> VehicleTypeOptions => VehicleTypeChoices.Catalogue;

    [ObservableProperty]
    private string _wache;
    [ObservableProperty]
    private string _callSign;
    [ObservableProperty]
    private int _seats;
    [ObservableProperty]
    private bool _hasZugfuehrer;
    [ObservableProperty]
    private bool _isOwn;
    [ObservableProperty]
    private string? _phone;
    [ObservableProperty]
    private string? _email;
    [ObservableProperty]
    private string? _vehicleType;

    partial void OnWacheChanged(string value) => _onChanged();

    partial void OnCallSignChanged(string value)
    {
        _onChanged();
        if (string.IsNullOrWhiteSpace(VehicleType) && _typeForCallSign?.Invoke(value) is { } type)
        {
            VehicleType = type;
        }
    }

    partial void OnSeatsChanged(int value) => _onChanged();

    partial void OnHasZugfuehrerChanged(bool value) => _onChanged();

    partial void OnIsOwnChanged(bool value) => _onChanged();

    partial void OnPhoneChanged(string? value) => _onChanged();

    partial void OnEmailChanged(string? value) => _onChanged();

    partial void OnVehicleTypeChanged(string? value) => _onChanged();
}
