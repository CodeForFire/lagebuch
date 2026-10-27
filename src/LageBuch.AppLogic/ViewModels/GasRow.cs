using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable Messgas (#410): name, unit, direction and up to three bands, plus the
/// value beyond which a reading is probably a typo.</summary>
public sealed partial class GasRow : ObservableObject
{
    private readonly Action _onChanged;

    public GasRow(GasDefinition gas, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(gas);
        _onChanged = onChanged;
        _name = gas.Name;
        _unit = gas.Unit;
        _direction = gas.Direction;
        _elevated = gas.Elevated;
        _dangerous = gas.Dangerous;
        _lethal = gas.Lethal;
        _plausibleUpTo = gas.PlausibleUpTo;
    }

    /// <summary>steigend and fallend with their labels, built once by <see cref="GasesSection.DirectionChoices"/>.</summary>
    public IReadOnlyList<GasDirectionOption> DirectionOptions { get; } = GasesSection.DirectionChoices;

    /// <summary>What the unit field suggests; the unit itself stays free text.</summary>
    public IReadOnlyList<string> UnitSuggestions { get; } = GasDefinition.UnitSuggestions;

    public static int MaxNameLength => GasDefinition.MaxNameLength;

    public static int MaxUnitLength => GasDefinition.MaxUnitLength;

    public static decimal MaxValue => GasDefinition.MaxValue;

    [ObservableProperty]
    private string _name;
    [ObservableProperty]
    private string _unit;
    [ObservableProperty]
    private GasDirection _direction;
    [ObservableProperty]
    private decimal? _elevated;
    [ObservableProperty]
    private decimal? _dangerous;
    [ObservableProperty]
    private decimal? _lethal;
    [ObservableProperty]
    private decimal? _plausibleUpTo;

    /// <summary>The row as it would be saved: name and unit trimmed, nothing else touched.</summary>
    public GasDefinition ToDefinition() =>
        new((Name ?? string.Empty).Trim(), (Unit ?? string.Empty).Trim(), Direction, Elevated, Dangerous, Lethal, PlausibleUpTo);

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnUnitChanged(string value) => _onChanged();

    partial void OnDirectionChanged(GasDirection value) => _onChanged();

    partial void OnElevatedChanged(decimal? value) => _onChanged();

    partial void OnDangerousChanged(decimal? value) => _onChanged();

    partial void OnLethalChanged(decimal? value) => _onChanged();

    partial void OnPlausibleUpToChanged(decimal? value) => _onChanged();
}

/// <summary>A <see cref="GasDirection"/> paired with its label for the picker.</summary>
public readonly record struct GasDirectionOption(GasDirection Value, string Label);
