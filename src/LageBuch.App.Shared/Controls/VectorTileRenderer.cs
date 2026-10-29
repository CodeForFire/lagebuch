using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Persistence.Map.Vector;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Rasterises one decoded vector tile in the Protomaps basemap schema into a 256 px bitmap, in
/// colours close to OSM Carto, the look a crew knows from openstreetmap.org. The map then caches
/// and places the bitmaps tile by tile.
/// </summary>
/// <remarks>
/// The style is fixed in code, for the schema RegionPackLibrary.VectorSchema names. Names are not
/// in the bitmap: <see cref="Labels"/> hands them to the map, which places those of every tile in
/// one pass. Past the archive's deepest zoom it renders the matching part of the ancestor tile at
/// the target zoom, so lines stay sharp and labels keep their size instead of being enlarged.
/// </remarks>
public static class VectorTileRenderer
{
    private const int Size = WebMercator.TileSizePixels;

    // Placed in this order; a later label that would overlap an earlier one is left out.
    private const int TownPriority = 0;
    private const int DistrictPriority = 1;
    private const int StreetPriority = 2;
    private const int HouseNumberPriority = 3;

    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");

    private static readonly IImmutableBrush Earth = Solid(0xF2, 0xEF, 0xE9);
    private static readonly IImmutableBrush Water = Solid(0xAA, 0xD3, 0xDF);
    private static readonly IImmutableBrush Building = Solid(0xD9, 0xD0, 0xC9);
    private static readonly IPen BuildingOutline = new ImmutablePen(Solid(0xC4, 0xB6, 0xAB), 0.5);
    private static readonly IPen Boundary = new ImmutablePen(Solid(0xAC, 0x46, 0xAC), 1.5, new ImmutableDashStyle([4, 2], 0));
    private static readonly IImmutableBrush LabelInk = Solid(0x33, 0x33, 0x33);
    private static readonly IImmutableBrush HouseNumberInk = Solid(0x66, 0x66, 0x66);
    private static readonly IPen Halo = new ImmutablePen(Solid(0xFF, 0xFF, 0xFF), 3, lineJoin: PenLineJoin.Round);

    private static readonly Dictionary<string, IImmutableBrush> Landuse = new(StringComparer.Ordinal)
    {
        ["residential"] = Solid(0xE0, 0xDF, 0xDF),
        ["industrial"] = Solid(0xEB, 0xDB, 0xE8),
        ["commercial"] = Solid(0xF2, 0xDA, 0xD9),
        ["retail"] = Solid(0xFF, 0xD6, 0xD1),
        ["farmland"] = Solid(0xEE, 0xF0, 0xD5),
        ["farmyard"] = Solid(0xF5, 0xDC, 0xBA),
        ["forest"] = Solid(0xAD, 0xD1, 0x9E),
        ["wood"] = Solid(0xAD, 0xD1, 0x9E),
        ["grass"] = Solid(0xCD, 0xEB, 0xB0),
        ["meadow"] = Solid(0xCD, 0xEB, 0xB0),
        ["park"] = Solid(0xC8, 0xFA, 0xCC),
        ["garden"] = Solid(0xC8, 0xFA, 0xCC),
        ["playground"] = Solid(0xDF, 0xFC, 0xE2),
        ["pitch"] = Solid(0x88, 0xE0, 0xBE),
        ["cemetery"] = Solid(0xAA, 0xCB, 0xAF),
        ["allotments"] = Solid(0xC9, 0xE1, 0xBF),
        ["school"] = Solid(0xFF, 0xFF, 0xE5),
        ["hospital"] = Solid(0xFF, 0xFF, 0xE5),
        ["pedestrian"] = Solid(0xDD, 0xDD, 0xE8),
        ["railway"] = Solid(0xEB, 0xDB, 0xE8),
        ["military"] = Solid(0xF2, 0xE4, 0xE4),
        ["wetland"] = Solid(0xD6, 0xEB, 0xE0),
    };

    // OSM highway value -> fill, casing, width in px at zoom 15.
    private static readonly Dictionary<string, RoadStyle> Roads = new(StringComparer.Ordinal)
    {
        ["motorway"] = new(Solid(0xE8, 0x92, 0xA2), Solid(0xDC, 0x2A, 0x67), 7),
        ["trunk"] = new(Solid(0xF9, 0xB2, 0x9C), Solid(0xC8, 0x4E, 0x2F), 7),
        ["primary"] = new(Solid(0xFC, 0xD6, 0xA4), Solid(0xA0, 0x6B, 0x00), 7),
        ["secondary"] = new(Solid(0xF7, 0xFA, 0xBF), Solid(0x70, 0x7D, 0x05), 6.5),
        ["tertiary"] = new(Solid(0xFF, 0xFF, 0xFF), Solid(0x8F, 0x8F, 0x8F), 6),
        ["unclassified"] = new(Solid(0xFF, 0xFF, 0xFF), Solid(0xBB, 0xBB, 0xBB), 4.5),
        ["residential"] = new(Solid(0xFF, 0xFF, 0xFF), Solid(0xBB, 0xBB, 0xBB), 4.5),
        ["living_street"] = new(Solid(0xED, 0xED, 0xED), Solid(0xBB, 0xBB, 0xBB), 4.5),
        ["service"] = new(Solid(0xFF, 0xFF, 0xFF), Solid(0xBB, 0xBB, 0xBB), 2.5),
        ["pedestrian"] = new(Solid(0xDD, 0xDD, 0xE8), Solid(0x99, 0x99, 0x99), 4),
    };

    private static readonly IPen Footway = new ImmutablePen(Solid(0xFA, 0x80, 0x72), 1.2, new ImmutableDashStyle([1.5, 1.5], 0));
    private static readonly IPen Cycleway = new ImmutablePen(Solid(0x00, 0x00, 0xFF), 1.2, new ImmutableDashStyle([1.5, 1.5], 0));
    private static readonly IPen Track = new ImmutablePen(Solid(0x99, 0x66, 0x00), 1.2, new ImmutableDashStyle([3, 1.5], 0));
    private static readonly IPen Rail = new ImmutablePen(Solid(0x70, 0x70, 0x70), 2);

    /// <summary>
    /// Renders the part of <paramref name="layers"/> -- a tile at <c>zoom - levels</c> -- that
    /// the tile at <paramref name="zoom"/> with index offset (<paramref name="subX"/>,
    /// <paramref name="subY"/>) inside it covers. With <paramref name="levels"/> 0 that is the
    /// whole tile.
    /// </summary>
    /// <remarks>
    /// The bitmap starts transparent and the <c>earth</c> layer paints the land. A pack's tiles
    /// are cut to its area, so outside it they show nothing, and a neighbouring pack drawn
    /// underneath shows through.
    /// </remarks>
    public static RenderTargetBitmap Render(IReadOnlyList<VectorLayer> layers, int zoom, int levels, int subX, int subY)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
        using (var context = bitmap.CreateDrawingContext())
        {
            using (context.PushClip(new Rect(0, 0, Size, Size)))
            {
                Draw(context, layers, zoom, levels, subX, subY);
            }
        }

        return bitmap;
    }

    private static void Draw(DrawingContext context, IReadOnlyList<VectorLayer> layers, int zoom, int levels, int subX, int subY)
    {
        VectorLayer? Layer(string name) => layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        // Line widths follow Carto's growth: about half again per zoom level.
        var widthScale = Math.Pow(1.6, zoom - 15);

        if (Layer("earth") is { } earth)
        {
            context.DrawGeometry(Earth, null, Polygons(earth.Features.Where(f => f.Type == VectorGeometryType.Polygon), Projection(earth.Extent, levels, subX, subY)));
        }

        if (Layer("landuse") is { } landuse)
        {
            var project = Projection(landuse.Extent, levels, subX, subY);
            foreach (var group in landuse.Features.Where(f => f.Type == VectorGeometryType.Polygon).GroupBy(f => f.Get("kind") ?? string.Empty, StringComparer.Ordinal))
            {
                if (Landuse.TryGetValue(group.Key, out var brush))
                {
                    context.DrawGeometry(brush, null, Polygons(group, project));
                }
            }
        }

        if (Layer("water") is { } water)
        {
            var project = Projection(water.Extent, levels, subX, subY);
            context.DrawGeometry(Water, null, Polygons(water.Features.Where(f => f.Type == VectorGeometryType.Polygon), project));
            foreach (var group in water.Features.Where(f => f.Type == VectorGeometryType.LineString).GroupBy(f => f.Get("kind") ?? string.Empty, StringComparer.Ordinal))
            {
                var width = group.Key switch { "river" => 5.0, "canal" => 4.0, _ => 1.5 } * widthScale;
                context.DrawGeometry(null, new ImmutablePen(Water, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Lines(group, project));
            }
        }

        var buildings = Layer("buildings");
        if (buildings is not null && zoom >= 14)
        {
            var project = Projection(buildings.Extent, levels, subX, subY);
            context.DrawGeometry(Building, zoom >= 16 ? BuildingOutline : null, Polygons(buildings.Features.Where(f => f.Type == VectorGeometryType.Polygon), project));
        }

        var roads = Layer("roads");
        if (roads is not null)
        {
            DrawRoads(context, roads, zoom, widthScale, Projection(roads.Extent, levels, subX, subY));
        }

        if (Layer("boundaries") is { } boundaries)
        {
            context.DrawGeometry(null, Boundary, Lines(boundaries.Features, Projection(boundaries.Extent, levels, subX, subY)));
        }
    }

    /// <summary>
    /// The names on the tile <see cref="Render"/> draws with the same arguments, in its pixels:
    /// places, street names from zoom 15 and house numbers from zoom 17. A label may reach past
    /// the tile's edge; the map draws it whole.
    /// </summary>
    public static IReadOnlyList<MapLabel> Labels(IReadOnlyList<VectorLayer> layers, int zoom, int levels, int subX, int subY)
    {
        ArgumentNullException.ThrowIfNull(layers);
        VectorLayer? Layer(string name) => layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        var labels = new List<MapLabel>();
        if (Layer("places") is { } places)
        {
            PlaceNames(labels, places, zoom, Projection(places.Extent, levels, subX, subY));
        }

        if (Layer("roads") is { } roads && zoom >= 15)
        {
            RoadNames(labels, roads, zoom, Projection(roads.Extent, levels, subX, subY));
        }

        if (Layer("buildings") is { } buildings && zoom >= 17)
        {
            HouseNumbers(labels, buildings, Projection(buildings.Extent, levels, subX, subY));
        }

        return labels;
    }

    private static void DrawRoads(DrawingContext context, VectorLayer roads, int zoom, double widthScale, Func<(int X, int Y), Point> project)
    {
        var lines = roads.Features.Where(f => f.Type == VectorGeometryType.LineString && f.Get("is_tunnel") != "true").ToList();

        context.DrawGeometry(null, Rail, Lines(lines.Where(f => f.Get("kind") == "rail"), project));

        if (zoom >= 14)
        {
            context.DrawGeometry(null, Track, Lines(lines.Where(f => f.Get("kind_detail") == "track"), project));
            context.DrawGeometry(null, Cycleway, Lines(lines.Where(f => f.Get("kind_detail") == "cycleway"), project));
            context.DrawGeometry(null, Footway, Lines(lines.Where(f => f.Get("kind_detail") is "footway" or "path" or "steps" or "bridleway"), project));
        }

        // Minor roads first, so a primary road is drawn over the junction; all casings before
        // all fills, so crossing roads of one class merge instead of cutting each other.
        var classes = Roads.Reverse().ToList();
        var groups = classes
            .Select(c => (Style: c.Value, Geometry: Lines(lines.Where(f => string.Equals(f.Get("kind_detail"), c.Key, StringComparison.Ordinal)), project)))
            .ToList();
        foreach (var (style, geometry) in groups)
        {
            var width = Math.Max(style.WidthAt15 * widthScale, 1.0);
            context.DrawGeometry(null, new ImmutablePen(style.Casing, width + 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }

        foreach (var (style, geometry) in groups)
        {
            var width = Math.Max(style.WidthAt15 * widthScale, 1.0);
            context.DrawGeometry(null, new ImmutablePen(style.Fill, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }
    }

    // A road's name on its longest segment, turned so it never reads upside down.
    private static void RoadNames(List<MapLabel> labels, VectorLayer roads, int zoom, Func<(int X, int Y), Point> project)
    {
        var fontSize = zoom >= 17 ? 12.0 : 10.0;
        foreach (var feature in roads.Features)
        {
            if (feature.Type != VectorGeometryType.LineString || feature.Get("name") is not { Length: > 0 } name || !Roads.ContainsKey(feature.Get("kind_detail") ?? string.Empty))
            {
                continue;
            }

            if (LongestSegment(feature, project) is not var (a, b) || Distance(a, b) < name.Length * fontSize * 0.55)
            {
                continue;
            }

            // The map turns the name the right way up, for however the map itself is turned.
            var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            labels.Add(Label(name, fontSize, LabelInk, Halo, StreetPriority, angle, middle));
        }
    }

    private static void HouseNumbers(List<MapLabel> labels, VectorLayer buildings, Func<(int X, int Y), Point> project)
    {
        foreach (var feature in buildings.Features)
        {
            if (feature.Get("addr_housenumber") is not { Length: > 0 } number || feature.Parts.Count == 0)
            {
                continue;
            }

            var center = Centroid(feature.Parts[0], project);
            if (!OnTile(center))
            {
                continue;
            }

            labels.Add(Label(number, 9, HouseNumberInk, null, HouseNumberPriority, null, center));
        }
    }

    private static void PlaceNames(List<MapLabel> labels, VectorLayer places, int zoom, Func<(int X, int Y), Point> project)
    {
        foreach (var feature in places.Features)
        {
            if (feature.Type != VectorGeometryType.Point || feature.Get("name") is not { Length: > 0 } name || feature.Parts.Count == 0 || feature.Parts[0].Count == 0)
            {
                continue;
            }

            var (size, priority) = feature.Get("kind") switch
            {
                "locality" => (zoom >= 14 ? 16.0 : 13.0, TownPriority),
                "neighbourhood" or "macrohood" => (11.0, DistrictPriority),
                _ => (0.0, 0),
            };

            // The schema says from which zoom a place is worth a label: a hamlet is not at zoom 12.
            var shownFrom = double.TryParse(feature.Get("min_zoom"), NumberStyles.Float, CultureInfo.InvariantCulture, out var minZoom) ? minZoom : 0;
            var at = project(feature.Parts[0][0]);
            if (size <= 0 || zoom < shownFrom || !OnTile(at))
            {
                continue;
            }

            labels.Add(Label(name, size, LabelInk, Halo, priority, null, at));
        }
    }

    // A tile also carries the features just past its edge, as a buffer for lines and areas. A
    // label for a point there belongs to the neighbouring tile, or it would be drawn twice.
    private static bool OnTile(Point at) => at.X is >= 0 and < Size && at.Y is >= 0 and < Size;

    // The text to be centred on `at`, along a street running at `angle`, or level when null.
    private static MapLabel Label(string text, double size, IImmutableBrush ink, IPen? halo, int priority, double? angle, Point at)
    {
        var formatted = new FormattedText(text, LabelCulture, FlowDirection.LeftToRight, Typeface.Default, size, ink);
        var outline = formatted.BuildGeometry(default) ?? new StreamGeometry();
        return new MapLabel(text, outline, new Size(formatted.Width, formatted.Height), at, angle, priority, ink, halo);
    }

    // Tile coordinates of the source tile to pixels of the target tile.
    private static Func<(int X, int Y), Point> Projection(int extent, int levels, int subX, int subY)
    {
        var part = (double)extent / (1 << levels);
        var scale = Size / part;
        var left = subX * part;
        var top = subY * part;
        return p => new Point((p.X - left) * scale, (p.Y - top) * scale);
    }

    private static StreamGeometry Polygons(IEnumerable<VectorFeature> features, Func<(int X, int Y), Point> project)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.SetFillRule(FillRule.EvenOdd);
            foreach (var feature in features)
            {
                foreach (var ring in feature.Parts)
                {
                    Figure(g, ring, project, closed: true);
                }
            }
        }

        return geometry;
    }

    private static StreamGeometry Lines(IEnumerable<VectorFeature> features, Func<(int X, int Y), Point> project)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var feature in features)
            {
                if (feature.Type != VectorGeometryType.LineString)
                {
                    continue;
                }

                foreach (var line in feature.Parts)
                {
                    Figure(g, line, project, closed: false);
                }
            }
        }

        return geometry;
    }

    private static void Figure(StreamGeometryContext g, IReadOnlyList<(int X, int Y)> points, Func<(int X, int Y), Point> project, bool closed)
    {
        if (points.Count < 2)
        {
            return;
        }

        g.BeginFigure(project(points[0]), isFilled: closed);
        for (var i = 1; i < points.Count; i++)
        {
            g.LineTo(project(points[i]));
        }

        g.EndFigure(closed);
    }

    // The longest stretch of the road that lies on this tile. A segment that runs on past the tile
    // counts with its visible part only, so a long straight road is labelled on every tile it
    // crosses, and never with a name hanging off the edge.
    private static (Point A, Point B)? LongestSegment(VectorFeature feature, Func<(int X, int Y), Point> project)
    {
        (Point A, Point B)? best = null;
        var bestLength = 0.0;
        foreach (var line in feature.Parts)
        {
            for (var i = 1; i < line.Count; i++)
            {
                if (ClipToTile(project(line[i - 1]), project(line[i])) is not var (a, b))
                {
                    continue;
                }

                var length = Distance(a, b);
                if (length > bestLength)
                {
                    bestLength = length;
                    best = (a, b);
                }
            }
        }

        return best;
    }

    // Liang-Barsky: the part of the segment from a to b inside the tile, or null if none is.
    private static (Point A, Point B)? ClipToTile(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        double enter = 0, leave = 1;
        foreach (var (p, q) in new[] { (-dx, a.X), (dx, Size - a.X), (-dy, a.Y), (dy, Size - a.Y) })
        {
            if (Math.Abs(p) < 1e-12)
            {
                // Parallel to this edge: wholly outside it, or no limit from it.
                if (q < 0)
                {
                    return null;
                }

                continue;
            }

            var t = q / p;
            if (p < 0)
            {
                enter = Math.Max(enter, t);
            }
            else
            {
                leave = Math.Min(leave, t);
            }
        }

        return enter < leave ? (new Point(a.X + (enter * dx), a.Y + (enter * dy)), new Point(a.X + (leave * dx), a.Y + (leave * dy))) : null;
    }

    private static Point Centroid(IReadOnlyList<(int X, int Y)> ring, Func<(int X, int Y), Point> project)
    {
        double x = 0, y = 0;
        foreach (var p in ring)
        {
            var s = project(p);
            x += s.X;
            y += s.Y;
        }

        return new Point(x / ring.Count, y / ring.Count);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    private static ImmutableSolidColorBrush Solid(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private sealed record RoadStyle(IImmutableBrush Fill, IImmutableBrush Casing, double WidthAt15);
}
