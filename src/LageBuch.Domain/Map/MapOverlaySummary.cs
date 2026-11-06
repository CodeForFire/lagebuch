namespace LageBuch.Domain.Map;

/// <summary>
/// What the importing device read from a GeoJSON file before it became an Ebene: the name to show
/// and the span of forecast times. The device that parsed the file sends it along, so the host can
/// write the Lagebuch entry without reading the geometry itself.
/// </summary>
public sealed record MapOverlaySummary(string Name, int FrameCount, DateTimeOffset? FirstTime, DateTimeOffset? LastTime);
