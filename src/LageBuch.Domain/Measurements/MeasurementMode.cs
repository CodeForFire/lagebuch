namespace LageBuch.Domain.Measurements;

/// <summary>How a value was taken (#930). Persisted by value.</summary>
public enum MeasurementMode
{
    /// <summary>"abgesetzt (Messtrupp)".</summary>
    Remote = 0,

    /// <summary>"aus dem Fahrzeug".</summary>
    FromVehicle = 1,

    /// <summary>"Dauermessung (stationär)".</summary>
    Stationary = 2,
}
