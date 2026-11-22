namespace LageBuch.Domain.ValueObjects;

/// <summary>A box on the Lagekarte, in WGS84 degrees: its west, south, east and north edges.</summary>
/// <remarks>
/// Like <see cref="GeoPoint"/>, refuses NaN, infinity, anything off the globe and edges out of
/// order, so a box read from a hostile file or a sync peer never reaches the map as one it cannot
/// draw. A box with no width or no height is refused too: nothing could be laid into it.
/// </remarks>
public sealed record GeoBox
{
    public GeoBox(double west, double south, double east, double north)
    {
        if (!IsValid(west, south, east, north))
        {
            throw new ArgumentOutOfRangeException(nameof(west), "A box needs finite edges on the globe, west of east and south of north.");
        }

        West = west;
        South = south;
        East = east;
        North = north;
    }

    public double West { get; }

    public double South { get; }

    public double East { get; }

    public double North { get; }

    /// <summary>
    /// The box, or null when an edge is missing, off the globe or out of order. For edges read from
    /// a file or a sync peer, where a bad value must cost the box, not the Einsatz.
    /// </summary>
    public static GeoBox? TryCreate(double? west, double? south, double? east, double? north) =>
        west is { } w && south is { } s && east is { } e && north is { } n && IsValid(w, s, e, n)
            ? new GeoBox(w, s, e, n)
            : null;

    private static bool IsValid(double west, double south, double east, double north) =>
        double.IsFinite(west) && double.IsFinite(east) && double.IsFinite(south) && double.IsFinite(north)
        && west >= -180 && east <= 180 && south >= -90 && north <= 90
        && west < east && south < north;
}
