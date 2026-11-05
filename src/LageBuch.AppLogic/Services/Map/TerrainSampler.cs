using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The heights under one map tile, a value per pixel, with a margin of <see cref="Border"/> pixels
/// on every side so slopes and contours run on across the tile's edge without a seam.
/// </summary>
public static class TerrainSampler
{
    /// <summary>Further out a tile spans dozens of DGM tiles and the relief is a blur anyway.</summary>
    public const int MinZoom = 12;

    /// <summary>
    /// The finest zoom terrain is worked out at, about 3 m a pixel against DGM5's 5 m cells. Closer
    /// in, the map enlarges these tiles: finer ones would only show the cells' edges.
    /// </summary>
    public const int MaxZoom = 15;

    /// <summary>The margin around the tile, in pixels.</summary>
    public const int Border = 2;

    /// <summary>One side of the sampled grid: the tile and its margin on both sides.</summary>
    public const int Side = WebMercator.TileSizePixels + (2 * Border);

    // The UTM position is computed exactly every this many pixels and interpolated between: the
    // projection is smooth, and this saves 99 % of its trigonometry.
    private const int NodeStep = 16;

    /// <summary>
    /// The heights of tile <paramref name="x"/>, <paramref name="y"/> at <paramref name="zoom"/>, row
    /// by row from the north-west corner of the margin; <see cref="float.NaN"/> where there is none.
    /// Null when no pack reaches the tile.
    /// </summary>
    public static float[]? Sample(ElevationSet elevation, int zoom, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        var nodes = (Side / NodeStep) + 2;
        var east = new double[nodes * nodes];
        var north = new double[nodes * nodes];
        double minE = double.MaxValue, minN = double.MaxValue, maxE = double.MinValue, maxN = double.MinValue;
        for (var j = 0; j < nodes; j++)
        {
            for (var i = 0; i < nodes; i++)
            {
                var (e, n) = Utm32.FromGeo(WebMercator.ToGeo(
                    (x * (double)WebMercator.TileSizePixels) - Border + 0.5 + (i * (double)NodeStep),
                    (y * (double)WebMercator.TileSizePixels) - Border + 0.5 + (j * (double)NodeStep),
                    zoom));
                east[(j * nodes) + i] = e;
                north[(j * nodes) + i] = n;
                (minE, maxE, minN, maxN) = (Math.Min(minE, e), Math.Max(maxE, e), Math.Min(minN, n), Math.Max(maxN, n));
            }
        }

        // Stryker disable once Block : without the check the tile is sampled in full and comes out empty all the same; the check only saves the 67 000 lookups.
        if (!elevation.Covers(minE, minN, maxE, maxN))
        {
            return null;
        }

        var heights = new float[Side * Side];
        var any = false;
        for (var py = 0; py < Side; py++)
        {
            var j = py / NodeStep;
            var ty = (py % NodeStep) / (double)NodeStep;
            for (var px = 0; px < Side; px++)
            {
                var i = px / NodeStep;
                var tx = (px % NodeStep) / (double)NodeStep;
                var k = (j * nodes) + i;
                var e = Lerp(Lerp(east[k], east[k + 1], tx), Lerp(east[k + nodes], east[k + nodes + 1], tx), ty);
                var n = Lerp(Lerp(north[k], north[k + 1], tx), Lerp(north[k + nodes], north[k + nodes + 1], tx), ty);
                var height = elevation.HeightAt(e, n);
                heights[(py * Side) + px] = height is { } h ? (float)h : float.NaN;
                any |= height is not null;
            }
        }

        return any ? heights : null;
    }

    /// <summary>The ground length of one map pixel at <paramref name="zoom"/> and <paramref name="latitude"/>, in metres.</summary>
    public static double MetresPerPixel(int zoom, double latitude) =>
        156543.03392804097 * Math.Cos(latitude * Math.PI / 180) / Math.Pow(2, zoom);

    /// <summary>The latitude of the middle of tile row <paramref name="y"/>.</summary>
    public static double LatitudeOfRow(int zoom, int y) =>
        WebMercator.ToGeo(0, (y + 0.5) * WebMercator.TileSizePixels, zoom).Latitude;

    /// <summary>
    /// A low and a high height for <see cref="TerrainLayer.Hoehenschichten"/> across what
    /// the rectangle from <paramref name="northWest"/> to <paramref name="southEast"/> spans: the 2nd and 98th percentile of a grid of samples, rounded out
    /// to 10 m so that a small pan keeps the colours -- and the drawn tiles -- as they are. Null
    /// where no pack has a height.
    /// </summary>
    public static (double Low, double High)? Range(ElevationSet elevation, GeoPoint northWest, GeoPoint southEast)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(northWest);
        ArgumentNullException.ThrowIfNull(southEast);
        const int steps = 16;
        var samples = new List<double>();
        for (var j = 0; j < steps; j++)
        {
            for (var i = 0; i < steps; i++)
            {
                var latitude = Lerp(northWest.Latitude, southEast.Latitude, (j + 0.5) / steps);
                var longitude = Lerp(northWest.Longitude, southEast.Longitude, (i + 0.5) / steps);
                if (elevation.HeightAt(new GeoPoint(latitude, longitude)) is { } h)
                {
                    samples.Add(h);
                }
            }
        }

        if (samples.Count == 0)
        {
            return null;
        }

        samples.Sort();
        var low = Math.Floor(samples[(int)(samples.Count * 0.02)] / 10) * 10;
        var high = Math.Ceiling(samples[(int)(samples.Count * 0.98)] / 10) * 10;
        return (low, Math.Max(high, low + 10));
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
