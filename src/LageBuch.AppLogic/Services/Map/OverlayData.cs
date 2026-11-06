using LageBuch.Domain.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// A Kartenebene as read from its file: one frame per point in time, oldest first. A DWD
/// Ausbreitungsprognose (<see cref="IsDwdForecast"/>) also carries what its colours mean.
/// </summary>
public sealed record OverlayData(IReadOnlyList<OverlayFrame> Frames, GeoBounds Bounds)
{
    /// <summary>The file is a DWD Ausbreitungsprognose: features with a <c>pixelvalue</c> and a forecast time.</summary>
    public bool IsDwdForecast { get; init; }

    /// <summary>What each colour of the Ebene stands for; empty when the file does not say.</summary>
    public IReadOnlyList<OverlayLegendEntry> Legend { get; init; } = [];

    /// <summary>What the Lagebuch records about this Ebene when it is imported under <paramref name="name"/>.</summary>
    public MapOverlaySummary Summary(string name) =>
        new(name, Frames.Count, Frames[0].Time, Frames[^1].Time);

    /// <summary>
    /// The frame to show at <paramref name="now"/>: the last one that has begun, or the first when
    /// none has yet. A forecast opened at 16:20 shows 16:15, the state the plume has reached.
    /// </summary>
    public int FrameIndexAt(DateTimeOffset now)
    {
        var index = 0;
        for (var i = 0; i < Frames.Count; i++)
        {
            if (Frames[i].Time is { } time && time <= now)
            {
                index = i;
            }
        }

        return index;
    }
}

/// <summary>One colour of an Ebene and what it means, such as „Stoff > 1e-08 ppm“.</summary>
public sealed record OverlayLegendEntry(uint FillArgb, string Label);

/// <summary>The shapes of a Kartenebene at one point in time; <see cref="Time"/> is null for a plain Ebene.</summary>
public sealed record OverlayFrame(DateTimeOffset? Time, IReadOnlyList<OverlayShape> Shapes);

public enum OverlayShapeKind
{
    /// <summary>Filled rings, outer rings and holes alike, to be filled even-odd.</summary>
    Area,

    /// <summary>Open lines.</summary>
    Line,

    /// <summary>Single positions, each its own part.</summary>
    Point,
}

/// <summary>One drawable piece of a Kartenebene, with its precomputed bounds so drawing can skip it off screen.</summary>
public sealed record OverlayShape(
    OverlayShapeKind Kind,
    IReadOnlyList<IReadOnlyList<GeoPoint>> Parts,
    OverlayStyle Style,
    string? Label,
    GeoBounds Bounds);

/// <summary>
/// How a shape is drawn, as ARGB colours so this layer stays free of Avalonia. Opacity is already
/// folded into the alpha byte.
/// </summary>
public sealed record OverlayStyle(uint FillArgb, uint StrokeArgb, double StrokeWidth);

/// <summary>A box in WGS84 degrees.</summary>
public sealed record GeoBounds(double South, double West, double North, double East)
{
    public GeoBounds Union(GeoBounds other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Math.Min(South, other.South), Math.Min(West, other.West), Math.Max(North, other.North), Math.Max(East, other.East));
    }

    public bool Intersects(GeoBounds other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return South <= other.North && other.South <= North && West <= other.East && other.West <= East;
    }

    public GeoPoint Center => new((South + North) / 2, (West + East) / 2);

    public static GeoBounds Of(IEnumerable<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        double south = 90, west = 180, north = -90, east = -180;
        foreach (var p in points)
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }

        return new(south, west, north, east);
    }
}
