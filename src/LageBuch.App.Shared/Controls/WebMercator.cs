using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Standard OSM/slippy-map Web Mercator tile math (#150 Plan B) — pure functions, no Avalonia
/// dependency, shared by the Wasserförderung map canvas's pan/zoom and the "click adds a route
/// point" conversion.
/// </summary>
public static class WebMercator
{
    public const int TileSizePixels = 256;

    /// <summary>Lat/lon (degrees) to the pixel position in the whole rendered world map at <paramref name="zoom"/>.</summary>
    public static (double X, double Y) ToWorldPixel(GeoPoint point, int zoom)
    {
        var n = TileSizePixels * Math.Pow(2, zoom);
        var x = n * (point.Longitude / 360.0 + 0.5);
        var sinLat = Math.Sin(point.Latitude * Math.PI / 180.0);
        var y = n * (0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI));
        return (x, y);
    }

    /// <summary>Inverse of <see cref="ToWorldPixel"/>.</summary>
    public static GeoPoint ToGeo(double worldX, double worldY, int zoom)
    {
        var n = TileSizePixels * Math.Pow(2, zoom);
        var lon = (worldX / n - 0.5) * 360.0;
        var latRad = 2 * Math.Atan(Math.Exp(Math.PI * (1 - 2 * worldY / n))) - Math.PI / 2;
        return new GeoPoint(latRad * 180.0 / Math.PI, lon);
    }

    public static (int X, int Y) ToTileIndex(double worldX, double worldY) =>
        ((int)Math.Floor(worldX / TileSizePixels), (int)Math.Floor(worldY / TileSizePixels));
}
