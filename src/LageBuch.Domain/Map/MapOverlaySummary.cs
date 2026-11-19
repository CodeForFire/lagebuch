using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Map;

/// <summary>
/// What the importing device read from a file before it became an Ebene: the name to show and the
/// span of forecast times, and for an image its <see cref="Kind"/>, the box it covers and the
/// licence line it has to carry. The device that read the file sends it along, so the host can
/// write the Lagebuch entry without reading the file itself.
/// </summary>
public sealed record MapOverlaySummary(
    string Name,
    int FrameCount,
    DateTimeOffset? FirstTime,
    DateTimeOffset? LastTime,
    MapOverlayKind Kind = MapOverlayKind.Vector,
    GeoBox? Bounds = null,
    string? Attribution = null);
