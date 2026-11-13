namespace LageBuch.Domain.Measurements;

/// <summary>Which Bereich a Messpunkt lies in (#930). Persisted by value, so the numbers are a
/// file and wire contract.</summary>
public enum MeasurementZone
{
    Unknown = 0,

    /// <summary>"Gefahrenbereich".</summary>
    Danger = 1,

    /// <summary>"Übergangsbereich".</summary>
    Transition = 2,

    /// <summary>"Absperrbereich".</summary>
    Cordon = 3,

    /// <summary>"außerhalb".</summary>
    Outside = 4,
}
