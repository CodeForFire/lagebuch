using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A Stammdaten Fahrzeug offered in the Kräfte form's FAHRZEUG box (#967). <see cref="Text"/> is
/// what the box filters on and shows once it is picked: the Funkrufname and the Wache, so typing
/// either one finds it.
/// </summary>
public sealed record VehicleOption(Vehicle Vehicle)
{
    public string Text { get; } = string.IsNullOrWhiteSpace(Vehicle.Wache)
        ? Vehicle.CallSign.Trim()
        : $"{Vehicle.CallSign.Trim()} · {Vehicle.Wache.Trim()}";

    // The suggestion box's inline completion reads an item's ToString (Behaviors/SuggestionBox.cs),
    // so it has to be the same text the box filters on.
    public override string ToString() => Text;
}
