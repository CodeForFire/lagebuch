using LageBuch.Domain.ValueObjects;

namespace LageBuch.Persistence.Map.Vector;

/// <summary>
/// Whether a point lies in a Gewässer, by the <c>water</c> layer of a Kartenpaket's vector
/// tiles: inside a water area, or within <see cref="LineReachMetres"/> of a river, canal, stream
/// or ditch that the map has only as a line. Reads each tile once and keeps it for the instance's
/// life -- one instance per Fließweg, which crosses a handful of tiles. Not thread-safe.
/// </summary>
/// <remarks>
/// The tiles come out of a downloaded pack: a tile that will not decode counts as land, never as
/// an error, since <see cref="MvtTile"/> already caps what a tile can make it allocate.
/// </remarks>
public sealed class VectorWater(IMapTileSource tiles)
{
    /// <summary>The zoom the tiles are read at: the packs' finest, where a Bach is still mapped.</summary>
    public const int Zoom = 15;

    /// <summary>A point this close to a waterway line, in metres, is in it: one DGM5 cell.</summary>
    public const double LineReachMetres = 5;

    /// <summary>An unnamed water area takes the name of a waterway line this close, in metres.</summary>
    public const double NameReachMetres = 100;

    private const double EarthCircumference = 40_075_016.686;

    private static readonly HashSet<string> Waterways = new(["river", "canal", "stream", "ditch", "drain"], StringComparer.Ordinal);

    private static readonly HashSet<string> NotGewaesser = new(["fountain", "swimming_pool"], StringComparer.Ordinal);

    private readonly IMapTileSource _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    private readonly Dictionary<(int X, int Y), VectorLayer?> _read = [];

    /// <summary>
    /// The name of the Gewässer at <paramref name="point"/>: empty for one without a name, null on land.
    /// </summary>
    public string? At(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        const double n = 1 << Zoom;
        var latitude = Math.Clamp(point.Latitude, -85.0511, 85.0511) * Math.PI / 180;
        var worldX = (point.Longitude + 180) / 360 * n;
        var worldY = (1 - (Math.Log(Math.Tan(latitude) + (1 / Math.Cos(latitude))) / Math.PI)) / 2 * n;
        var (tileX, tileY) = ((int)Math.Floor(worldX), (int)Math.Floor(worldY));
        if (Layer(tileX, tileY) is not { } layer)
        {
            return null;
        }

        var x = (worldX - tileX) * layer.Extent;
        var y = (worldY - tileY) * layer.Extent;
        var metresPerUnit = EarthCircumference * Math.Cos(latitude) / n / layer.Extent;

        var area = layer.Features.FirstOrDefault(f => f.Type == VectorGeometryType.Polygon && !Has(f, "kind", NotGewaesser) && !Has(f, "kind_detail", NotGewaesser) && f.Parts.Count(ring => Inside(ring, x, y)) % 2 == 1);

        // In an area, the nearest named waterway gives it its name; outside one, a waterway this
        // close is the Gewässer itself. A named one goes before a nearer one without a name.
        var reach = area is null ? LineReachMetres : NameReachMetres;
        var waterways = layer.Features
            .Where(f => f.Type == VectorGeometryType.LineString && f.Parts.Count > 0 && Has(f, "kind", Waterways))
            .Select(f => (Feature: f, Metres: f.Parts.Min(part => DistanceTo(part, x, y)) * metresPerUnit));

        // Stryker disable once Equality : a waterway exactly at the reach may count either way.
        var line = waterways.Where(c => c.Metres <= reach)
            .OrderBy(c => Named(c.Feature) is null)
            .ThenBy(c => c.Metres)
            .Select(c => c.Feature)
            .FirstOrDefault();

        if (area is not null)
        {
            return Named(area) ?? Named(line) ?? string.Empty;
        }

        return line is null ? null : Named(line) ?? string.Empty;
    }

    private static string? Named(VectorFeature? feature) => feature?.Get("name") is { Length: > 0 } name ? name : null;

    private static bool Has(VectorFeature feature, string key, HashSet<string> values) => feature.Get(key) is { } value && values.Contains(value);

    // Even-odd: a ring crossed an odd number of times by a ray to the east holds the point.
    private static bool Inside(IReadOnlyList<(int X, int Y)> ring, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (a, b) = (ring[i], ring[j]);
            if ((a.Y > y) != (b.Y > y) && x < a.X + ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    // To the nearest of the line's segments; a line of one point is a segment of no length.
    private static double DistanceTo(IReadOnlyList<(int X, int Y)> line, double x, double y)
    {
        var nearest = double.MaxValue;
        for (var i = 0; i < line.Count; i++)
        {
            nearest = Math.Min(nearest, DistanceToSegment(line[Math.Max(0, i - 1)], line[i], x, y));
        }

        return nearest;
    }

    private static double DistanceToSegment((int X, int Y) a, (int X, int Y) b, double x, double y)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        var t = a == b ? 0 : Math.Clamp((((x - a.X) * dx) + ((y - a.Y) * dy)) / (Square(dx) + Square(dy)), 0, 1);
        return Math.Sqrt(Square(a.X + (t * dx) - x) + Square(a.Y + (t * dy) - y));
    }

    private static double Square(double value) => value * value;

    private VectorLayer? Layer(int x, int y)
    {
        if (!_read.TryGetValue((x, y), out var layer))
        {
            layer = Decode(_tiles.GetTile(Zoom, x, y));
            _read[(x, y)] = layer;
        }

        return layer;
    }

    // No tile at all decodes to no layers: land, like a tile without water.
    private static VectorLayer? Decode(byte[]? tile)
    {
        try
        {
            return MvtTile.Decode(tile).FirstOrDefault(l => string.Equals(l.Name, "water", StringComparison.Ordinal));
        }
        catch (FormatException)
        {
            // A damaged tile is land here: the map draws nothing there either.
            return null;
        }
    }
}
