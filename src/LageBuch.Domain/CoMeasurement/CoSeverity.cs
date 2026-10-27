namespace LageBuch.Domain.CoMeasurement;

/// <summary>How dangerous a gas reading is, independent of <see cref="DwellingStatus"/> (which
/// tracks search progress, not the measured value). Each gas sets its own bands in the
/// Stammdaten (#410, see <see cref="GasDefinition"/>); the name dates from when CO was the only
/// gas.</summary>
public enum CoSeverity
{
    Normal,
    Elevated,
    Dangerous,
    Lethal,
}
