using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable Messgas (#410): name, unit, direction and up to three bands, plus the
/// value beyond which a reading is probably a typo and, on a falling gas, the enrichment limit
/// (#927).</summary>
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
        _upperAlarm = gas.UpperAlarm;
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
    [NotifyPropertyChangedFor(nameof(IsFalling))]
    private GasDirection _direction;
    [ObservableProperty]
    private decimal? _elevated;
    [ObservableProperty]
    private decimal? _dangerous;
    [ObservableProperty]
    private decimal? _lethal;
    [ObservableProperty]
    private decimal? _plausibleUpTo;

    /// <summary>„Anreicherung ab“: kept while the row is rising, so switching the direction back
    /// and forth loses nothing, but saved only on a falling gas (<see cref="GasDefinition.Sanitized"/>).</summary>
    [ObservableProperty]
    private decimal? _upperAlarm;

    /// <summary>Whether the row offers „Anreicherung ab“ at all: it means nothing on a rising gas.</summary>
    public bool IsFalling => Direction == GasDirection.Falling;

    /// <summary>Whether this row is one of those <see cref="GasesSection.Conflicts"/> names, so
    /// the view can mark it. Set by the section; not an edit of its own.</summary>
    [ObservableProperty]
    private bool _hasConflict;

    /// <summary>The row as it would be saved: name and unit trimmed, nothing else touched.</summary>
    public GasDefinition ToDefinition() =>
        new((Name ?? string.Empty).Trim(), (Unit ?? string.Empty).Trim(), Direction, Elevated, Dangerous, Lethal, PlausibleUpTo, UpperAlarm);

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnUnitChanged(string value) => _onChanged();

    partial void OnDirectionChanged(GasDirection value) => _onChanged();

    partial void OnElevatedChanged(decimal? value) => _onChanged();

    partial void OnDangerousChanged(decimal? value) => _onChanged();

    partial void OnLethalChanged(decimal? value) => _onChanged();

    partial void OnPlausibleUpToChanged(decimal? value) => _onChanged();

    partial void OnUpperAlarmChanged(decimal? value) => _onChanged();
}

/// <summary>A <see cref="GasDirection"/> paired with its label for the picker.</summary>
public readonly record struct GasDirectionOption(GasDirection Value, string Label);
