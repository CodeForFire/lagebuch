using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Fahrzeuge Stammdaten list (#76) — rows of Wache + Funkrufname + Sitzplätze.
/// The Wache is free text so a row can reference a Wache that only exists in the Brigades list;
/// the Kräfte entry matches vehicles against its typed brigade by name, not by id.
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
        Action onChanged)
        : base(title)
    {
        _onChanged = onChanged;
        _wacheOptions = wacheOptions;
        _callSignOptions = callSignOptions;
        Rows = new ObservableCollection<VehicleRow>(
            vehicles.Select(v => NewRow(v.Wache, v.CallSign, v.Seats)));
    }

    public ObservableCollection<VehicleRow> Rows { get; }

    private VehicleRow NewRow(string wache, string callSign, int seats) =>
        new(wache, callSign, seats, _wacheOptions, _callSignOptions, _onChanged);

    [RelayCommand]
    private void Add()
    {
        Rows.Add(NewRow(string.Empty, string.Empty, 0));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(VehicleRow row)
    {
        if (Rows.Remove(row))
        {
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveUp(VehicleRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(VehicleRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }

    /// <summary>Rows with a non-blank Wache and Funkrufname; trimmed, seats as entered.</summary>
    public IReadOnlyList<Vehicle> ToValues()
    {
        var result = new List<Vehicle>();
        foreach (var row in Rows)
        {
            var wache = row.Wache?.Trim() ?? string.Empty;
            var callSign = row.CallSign?.Trim() ?? string.Empty;
            if (wache.Length > 0 && callSign.Length > 0)
            {
                result.Add(new Vehicle(wache, callSign, row.Seats));
            }
        }

        return result;
    }
}
