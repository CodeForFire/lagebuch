using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Fahrzeug entry: Wache plus Funkrufname, Sitzplätze (#76), ZF-Flag and whether
/// the vehicle is the brigade's own (#458). Wache and
/// Funkrufname carry suggestion lists from the master data for the view's AutoCompleteBox --
/// free text stays valid, so they are suggestions, not a closed set.
/// </summary>
public sealed partial class VehicleRow : ObservableObject
{
    private readonly Action _onChanged;

    public VehicleRow(
        string wache,
        string callSign,
        int seats,
        bool hasZugfuehrer,
        bool isOwn,
        IReadOnlyList<string> wacheOptions,
        IReadOnlyList<string> callSignOptions,
        Action onChanged)
    {
        _onChanged = onChanged;
        _wache = wache;
        _callSign = callSign;
        _seats = seats;
        _hasZugfuehrer = hasZugfuehrer;
        _isOwn = isOwn;
        WacheOptions = wacheOptions;
        CallSignOptions = callSignOptions;
    }

    /// <summary>Suggestions: the Wachen of the vehicles as loaded (derived, not a separate list).</summary>
    public IReadOnlyList<string> WacheOptions { get; }

    /// <summary>Suggestions: the Funkrufnamen of the vehicles and roster as loaded (derived, not a separate list).</summary>
    public IReadOnlyList<string> CallSignOptions { get; }

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

    partial void OnWacheChanged(string value) => _onChanged();

    partial void OnCallSignChanged(string value) => _onChanged();

    partial void OnSeatsChanged(int value) => _onChanged();

    partial void OnHasZugfuehrerChanged(bool value) => _onChanged();

    partial void OnIsOwnChanged(bool value) => _onChanged();
}
