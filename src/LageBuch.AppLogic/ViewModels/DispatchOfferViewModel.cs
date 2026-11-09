using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The alarm the Home screen offers to take up: what the Leitstelle sent, in the few words a
/// Lagebuchführer needs to recognise it, and the alarmed Einsatzmittel to tick.
/// </summary>
public sealed class DispatchOfferViewModel
{
    /// <summary>The alarm, its Einsatzmittel matched against the vehicles and place names of <paramref name="masterData"/>.</summary>
    public DispatchOfferViewModel(DispatchAlarm alarm, MasterDataSet masterData)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        ArgumentNullException.ThrowIfNull(masterData);
        Alarm = alarm;
        Units = alarm.RequestedUnits
            .Select(name => new DispatchUnitChoice(name, DispatchImport.Match(name, masterData.Vehicles, masterData.DispatchPlaceNames)))
            .ToList();
    }

    public DispatchAlarm Alarm { get; }

    /// <summary>Stichwort, or the Einsatztyp when the Leitstelle sent none.</summary>
    public string Title => Alarm.Keyword ?? Alarm.IncidentType ?? "Alarm";

    /// <summary>"Hauptstraße 12, 12345 Musterstadt Nord · alarmiert 17:17 · Einsatznummer T 1.2 …".</summary>
    public string Summary
    {
        get
        {
            var street = string.Join(" ", new[] { Alarm.Street, Alarm.HouseNumber }.OfType<string>());
            var place = string.Join(" ", new[] { Alarm.PostalCode, Alarm.Place }.OfType<string>());
            var address = string.Join(", ", new[] { street, place }.Where(s => s.Length > 0));
            var parts = new List<string>();
            if (address.Length > 0)
            {
                parts.Add(address);
            }

            if (Alarm.AlarmedAt is { } at)
            {
                parts.Add($"alarmiert {Formatting.TimeOfDay(at)}");
            }

            parts.Add($"Einsatznummer {Alarm.IncidentNumber}");
            return string.Join(" · ", parts);
        }
    }

    public bool IsTest => Alarm.IsTest;

    public IReadOnlyList<DispatchUnitChoice> Units { get; }

    public bool HasUnits => Units.Count > 0;

    /// <summary>The alarm with the Einsatzmittel ticked right now.</summary>
    public DispatchImport ToImport() =>
        new(Alarm, Units.Where(u => u.IsSelected).Select(u => new DispatchUnit(u.Name, u.Vehicle)).ToList());
}

/// <summary>
/// One alarmed Einsatzmittel. Ticked from the start when it names a Stammdaten vehicle; a Wache,
/// a Kommandant's Funkrufname or a Kreisbrandrat are alarmed too, but are no vehicle to record.
/// </summary>
public sealed partial class DispatchUnitChoice : ObservableObject
{
    public DispatchUnitChoice(string name, Vehicle? vehicle)
    {
        Name = name;
        Vehicle = vehicle;
        _isSelected = vehicle is not null;
    }

    public string Name { get; }

    public Vehicle? Vehicle { get; }

    /// <summary>"Wache 2 · HLF 20" for a Stammdaten vehicle, "nicht in den Stammdaten" otherwise.</summary>
    public string Detail => Vehicle is { } v
        ? string.Join(" · ", new[] { v.Wache, VehiclePreset.Of(v).VehicleType }.Where(s => !string.IsNullOrWhiteSpace(s)))
        : "nicht in den Stammdaten";

    [ObservableProperty]
    private bool _isSelected;
}
