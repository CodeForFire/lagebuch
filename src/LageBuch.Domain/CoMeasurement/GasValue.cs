namespace LageBuch.Domain.CoMeasurement;

/// <summary>One gas's current value in a Wohnung, with the band it falls in (#410).</summary>
public sealed record GasValue(IncidentGas Gas, decimal Value, CoSeverity Severity);
