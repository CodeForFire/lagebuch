namespace LageBuch.Domain.Measurements;

/// <summary>Whether a Messung has been looked at (#930). An <see cref="Outlier"/> stays in the log
/// but never counts as the latest value. Persisted by value.</summary>
public enum MeasurementStatus
{
    /// <summary>"unbestätigt".</summary>
    Unconfirmed = 0,

    /// <summary>"bestätigt".</summary>
    Confirmed = 1,

    /// <summary>"Ausreißer".</summary>
    Outlier = 2,
}
