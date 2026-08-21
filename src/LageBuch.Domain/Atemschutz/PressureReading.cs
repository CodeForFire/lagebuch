namespace LageBuch.Domain.Atemschutz;

/// <summary>A single Druckkontrolle (pressure check) for a Trupp, in bar at a point in time.</summary>
public sealed record PressureReading(DateTimeOffset Time, int Bar);
