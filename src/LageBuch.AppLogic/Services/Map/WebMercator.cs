using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The slippy-map tile math every OSM tile uses: a position becomes a pixel in the whole world map
/// at a zoom, and back. Pure functions, so the map control, its snapshot for the PDF and the tests
/// share one implementation.
/// </summary>
public static class WebMercator
{
    public const int TileSizePixels = 256;

    /// <summary>Web Mercator stops here; past it the projection runs off to infinity.</summary>
    public const double MaxLatitude = 85.05112878;

    /// <summary>The pixel of <paramref name="point"/> in the whole world map at <paramref name="zoom"/>.</summary>
    public static (double X, double Y) ToWorldPixel(GeoPoint point, int zoom)
    {
        ArgumentNullException.ThrowIfNull(point);
        var size = WorldSize(zoom);
        var latitude = Math.Clamp(point.Latitude, -MaxLatitude, MaxLatitude);
        var sinLat = Math.Sin(latitude * Math.PI / 180.0);
        var x = size * ((point.Longitude / 360.0) + 0.5);
        var y = size * (0.5 - (Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)));
        return (x, y);
    }

    /// <summary>
    /// The inverse of <see cref="ToWorldPixel"/>. A pixel off the world's edge -- a map dragged
    /// past the pole or the date line -- is clamped onto it, never handed back as a position
    /// <see cref="GeoPoint"/> would refuse.
    /// </summary>
    public static GeoPoint ToGeo(double worldX, double worldY, int zoom)
    {
        var size = WorldSize(zoom);
        var x = Math.Clamp(worldX, 0, size);
        var y = Math.Clamp(worldY, 0, size);
        var longitude = ((x / size) - 0.5) * 360.0;
        var latitude = ((2 * Math.Atan(Math.Exp(Math.PI * (1 - (2 * y / size))))) - (Math.PI / 2)) * 180.0 / Math.PI;
        return new GeoPoint(Math.Clamp(latitude, -MaxLatitude, MaxLatitude), Math.Clamp(longitude, -180, 180));
    }

    public static (int X, int Y) ToTileIndex(double worldX, double worldY) =>
        ((int)Math.Floor(worldX / TileSizePixels), (int)Math.Floor(worldY / TileSizePixels));

    /// <summary>The middle of an inclusive tile range, as <see cref="Persistence.Map.IMapTileSource.GetTileBounds"/> reports it.</summary>
    public static GeoPoint CenterOfTiles(int zoom, int minX, int maxX, int minY, int maxY) =>
        ToGeo((minX + maxX + 1.0) * TileSizePixels / 2.0, (minY + maxY + 1.0) * TileSizePixels / 2.0, zoom);

    /// <summary>The earth's radius EPSG:3857 projects with, in metres.</summary>
    public const double EarthRadius = 6378137;

    /// <summary>
    /// <paramref name="point"/> in EPSG:3857 metres, x east and y north of the origin: the
    /// coordinates a WMS GetMap request takes its box in.
    /// </summary>
    public static (double X, double Y) ToMetres(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var latitude = Math.Clamp(point.Latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180.0;
        return (EarthRadius * point.Longitude * Math.PI / 180.0, EarthRadius * Math.Log(Math.Tan((Math.PI / 4) + (latitude / 2))));
    }

    /// <summary>The inverse of <see cref="ToMetres"/>, clamped onto the globe like <see cref="ToGeo"/>.</summary>
    public static GeoPoint FromMetres(double x, double y)
    {
        var longitude = x / EarthRadius * 180.0 / Math.PI;
        var latitude = ((2 * Math.Atan(Math.Exp(y / EarthRadius))) - (Math.PI / 2)) * 180.0 / Math.PI;
        return new GeoPoint(Math.Clamp(latitude, -MaxLatitude, MaxLatitude), Math.Clamp(longitude, -180, 180));
    }

    private static double WorldSize(int zoom) => TileSizePixels * Math.Pow(2, zoom);
}
