namespace LageBuch.Domain.CoMeasurement;

/// <summary>Which way a gas gets worse (#410). Almost every gas is <see cref="Rising"/>; O₂ is the
/// one a crew watches fall. A gas has exactly one direction, so O₂ enrichment is out of scope.</summary>
public enum GasDirection
{
    /// <summary>"steigend" — a higher reading is worse.</summary>
    Rising,

    /// <summary>"fallend" — a lower reading is worse.</summary>
    Falling,
}
