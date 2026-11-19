using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The part of the map LUFTBILD LADEN asks the LDBV for: a box in EPSG:3857 metres and the size of
/// the picture in pixels. The box is the visible section of the Lagekarte, cut to
/// <see cref="MaxPixels"/> a side at <see cref="MetresPerPixel"/>, so one download never runs past
/// what the services hand out and stays well under <see cref="Domain.Files.IncidentFile.MaxSizeBytes"/>.
/// </summary>
public sealed record LuftbildArea
{
    /// <summary>
    /// The DOP40's own resolution in Bavaria: 40 cm on the ground is about 60 cm in Web Mercator at
    /// 48° north. It is also a scale of about 1:2150, well under the 1:5000 beyond which the
    /// Parzellarkarte draws nothing.
    /// </summary>
    public const double MetresPerPixel = 0.6;

    /// <summary>
    /// The longest side of one picture: about 2.4 km in Web Mercator, 1.6 km on the ground. Under
    /// the services' own 6000 px, and a JPEG of it stays a few megabytes.
    /// </summary>
    public const int MaxPixels = 4096;

    /// <summary>The shortest side: a map zoomed onto one house still gets the street around it.</summary>
    public const int MinPixels = 512;

    /// <summary>The German message for a section the LDBV has no pictures of.</summary>
    public const string OutsideBavaria = "Luftbilder gibt es nur für Bayern.";

    // How the WMS standard turns metres per pixel into a scale: a pixel is 0.28 mm.
    private const double MetresPerScreenPixel = 0.00028;

    /// <summary>Bavaria's bounding box, a little generous at every edge.</summary>
    private static readonly GeoBox Bavaria = new(8.95, 47.25, 13.85, 50.6);

    private LuftbildArea(double minX, double minY, double maxX, double maxY, int width, int height)
    {
        (MinX, MinY, MaxX, MaxY, Width, Height) = (minX, minY, maxX, maxY, width, height);
        var southWest = WebMercator.FromMetres(minX, minY);
        var northEast = WebMercator.FromMetres(maxX, maxY);
        Bounds = new GeoBox(southWest.Longitude, southWest.Latitude, northEast.Longitude, northEast.Latitude);
    }

    /// <summary>The west edge, in EPSG:3857 metres.</summary>
    public double MinX { get; }

    /// <summary>The south edge, in EPSG:3857 metres.</summary>
    public double MinY { get; }

    /// <summary>The east edge, in EPSG:3857 metres.</summary>
    public double MaxX { get; }

    /// <summary>The north edge, in EPSG:3857 metres.</summary>
    public double MaxY { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The box in WGS84, where the picture is laid on the map. Its corners are the request's exactly.</summary>
    public GeoBox Bounds { get; }

    /// <summary>The scale the services draw the picture at, as the WMS standard computes it.</summary>
    public double ScaleDenominator => (MaxX - MinX) / Width / MetresPerScreenPixel;

    /// <summary>
    /// The area to ask for when the Lagekarte shows <paramref name="visible"/>: centred on it, as large
    /// as it within the caps. Null when its middle lies outside Bavaria, where the LDBV has none
    /// (<see cref="OutsideBavaria"/>).
    /// </summary>
    public static LuftbildArea? For(GeoBox visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var (west, south) = WebMercator.ToMetres(new GeoPoint(visible.South, visible.West));
        var (east, north) = WebMercator.ToMetres(new GeoPoint(visible.North, visible.East));
        var (x, y) = ((west + east) / 2, (south + north) / 2);
        var middle = WebMercator.FromMetres(x, y);
        if (middle.Longitude < Bavaria.West || middle.Longitude > Bavaria.East
            || middle.Latitude < Bavaria.South || middle.Latitude > Bavaria.North)
        {
            return null;
        }

        var width = Pixels(east - west);
        var height = Pixels(north - south);
        var halfWidth = width * MetresPerPixel / 2;
        var halfHeight = height * MetresPerPixel / 2;
        return new LuftbildArea(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, width, height);
    }

    private static int Pixels(double metres) => Math.Clamp((int)Math.Round(metres / MetresPerPixel), MinPixels, MaxPixels);
}
