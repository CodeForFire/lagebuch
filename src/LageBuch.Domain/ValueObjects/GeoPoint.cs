namespace LageBuch.Domain.ValueObjects;

/// <summary>A position on the Lagekarte, in WGS84 degrees.</summary>
/// <remarks>
/// Refuses NaN, infinity and anything off the globe, so a coordinate read from a hostile file or
/// a sync peer can never reach the tile math as a value it would turn into a nonsense tile index.
/// </remarks>
public sealed record GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must lie between -90 and 90 degrees.");
        }

        if (!double.IsFinite(longitude) || longitude < -180 || longitude > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must lie between -180 and 180 degrees.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>
    /// The point, or null when either coordinate is missing or off the globe. For coordinates read
    /// from a file or a sync peer, where a bad value must cost the position, not the Einsatz.
    /// </summary>
    public static GeoPoint? TryCreate(double? latitude, double? longitude) =>
        latitude is { } lat && longitude is { } lon
        && double.IsFinite(lat) && lat is >= -90 and <= 90
        && double.IsFinite(lon) && lon is >= -180 and <= 180
            ? new GeoPoint(lat, lon)
            : null;
}
