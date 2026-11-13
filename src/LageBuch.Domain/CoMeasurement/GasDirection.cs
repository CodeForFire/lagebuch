namespace LageBuch.Domain.CoMeasurement;

/// <summary>Which way a gas gets worse (#410). Almost every gas is <see cref="Rising"/>; O₂ is the
/// one a crew watches fall. A gas has exactly one direction; O₂ enrichment is covered by a falling
/// gas's optional <see cref="GasDefinition.UpperAlarm"/> (#927), not by a third direction.</summary>
public enum GasDirection
{
    /// <summary>"steigend" — a higher reading is worse.</summary>
    Rising,

    /// <summary>"fallend" — a lower reading is worse.</summary>
    Falling,
}
