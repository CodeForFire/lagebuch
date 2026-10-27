using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// When the map shows the water points, and which one a tap means. The drawing and the tap use
/// the same thresholds, so a point can be tapped exactly when it can be seen.
/// </summary>
public static class WaterPointLayer
{
    /// <summary>Further out, a Landkreis' thousands of hydrants are a red carpet, not information.</summary>
    public const int MinZoom = 15;

    /// <summary>Close enough that "UH 100" beside each hydrant still leaves room for the street.</summary>
    public const int LabelsMinZoom = 17;

    /// <summary>How far from a symbol's centre a tap still means it: about a fingertip.</summary>
    public const double HitRadiusPixels = 16;

    /// <summary>
    /// The water point nearest to <paramref name="tap"/> within <see cref="HitRadiusPixels"/> on a
    /// map at <paramref name="zoom"/>; null when none is that close, or the map is too far out to
    /// show any.
    /// </summary>
    public static WaterPoint? At(IReadOnlyList<WaterPoint> points, GeoPoint tap, int zoom)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(tap);
        if (zoom < MinZoom)
        {
            return null;
        }

        var (tapX, tapY) = WebMercator.ToWorldPixel(tap, zoom);
        WaterPoint? nearest = null;
        var nearestSquared = HitRadiusPixels * HitRadiusPixels;
        foreach (var point in points)
        {
            var (x, y) = WebMercator.ToWorldPixel(point.Position, zoom);
            var squared = ((x - tapX) * (x - tapX)) + ((y - tapY) * (y - tapY));
            if (squared <= nearestSquared)
            {
                nearest = point;
                nearestSquared = squared;
            }
        }

        return nearest;
    }
}
