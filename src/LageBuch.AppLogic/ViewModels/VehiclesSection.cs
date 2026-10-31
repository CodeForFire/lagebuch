using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Fahrzeuge Stammdaten list (#76) — rows of Wache + Funkrufname + Sitzplätze.
/// This is the single place Wachen and Funkrufnamen are maintained: both lists are derived from
/// these rows (see <see cref="MasterDataSet.Brigades"/> / <see cref="MasterDataSet.RadioCallSigns"/>).
/// Wache and Funkrufname are free text with the already-known values as typing suggestions; the
/// Kräfte entry matches vehicles against its typed brigade by name, not by id.
/// </summary>
public sealed partial class VehiclesSection : EditorSection
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<string> _wacheOptions;
    private readonly IReadOnlyList<string> _callSignOptions;

    public VehiclesSection(
        string title,
        IEnumerable<Vehicle> vehicles,
        IReadOnlyList<string> wacheOptions,
        IReadOnlyList<string> callSignOptions,
        Action onChanged,
        Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        _wacheOptions = wacheOptions;
        _callSignOptions = callSignOptions;
        Rows = new ObservableCollection<VehicleRow>(
            vehicles.Select(v => NewRow(v.Wache, v.CallSign, v.Seats, v.HasZugfuehrer, v.IsOwn, v.Phone, v.Email, v.VehicleType)));
    }

    public ObservableCollection<VehicleRow> Rows { get; }

    private VehicleRow NewRow(string wache, string callSign, int seats, bool hasZugfuehrer, bool isOwn, string? phone, string? email, string? vehicleType) =>
        new(wache, callSign, seats, hasZugfuehrer, isOwn, phone, email, vehicleType, _wacheOptions, _callSignOptions, _onChanged);

    [RelayCommand]
    private void Add()
    {
        var row = NewRow(string.Empty, string.Empty, 0, false, isOwn: true, phone: null, email: null, vehicleType: null);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(VehicleRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.CallSign, AllBlank(row.Wache, row.CallSign, row.Phone, row.Email, row.VehicleType), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>
    /// Rows with a non-blank Wache and Funkrufname; trimmed, seats as entered, a blank
    /// Fahrzeugtelefon, E-Mail or Fahrzeugtyp stored as none, and a Fahrzeugtyp cut to its cap.
    /// </summary>
    public IReadOnlyList<Vehicle> ToValues()
    {
        var result = new List<Vehicle>();
        foreach (var row in Rows)
        {
            var wache = row.Wache?.Trim() ?? string.Empty;
            var callSign = row.CallSign?.Trim() ?? string.Empty;
            if (wache.Length > 0 && callSign.Length > 0)
            {
                var phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim();
                var email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim();
                var type = string.IsNullOrWhiteSpace(row.VehicleType) ? null : row.VehicleType.Trim();
                if (type is { Length: > VehicleTypes.MaxLength })
                {
                    type = type[..VehicleTypes.MaxLength].TrimEnd();
                }

                // A typed "hlf" is stored as the catalogue spells it, so the sign reads the same everywhere.
                type = VehicleTypes.Find(type)?.Code ?? type;
                result.Add(new Vehicle(wache, callSign, row.Seats, row.HasZugfuehrer, row.IsOwn, phone, email, type));
            }
        }

        return result;
    }
}
