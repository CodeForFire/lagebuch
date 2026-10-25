namespace LageBuch.Domain.Atemschutz;

/// <summary>
/// A single Druckkontrolle (pressure check) for a Trupp, in bar at a point in time, and where the
/// Trupp said it was when it reported. <paramref name="Location"/> is optional free text; null when
/// none was given, which is every reading recorded before it existed.
/// </summary>
public sealed record PressureReading(DateTimeOffset Time, int Bar, string? Location = null);
