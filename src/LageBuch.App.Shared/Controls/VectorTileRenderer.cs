using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Persistence.Map.Vector;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Rasterises one decoded vector tile in the Protomaps basemap schema into a 256 px bitmap, in the
/// colours of a <see cref="MapStyle"/>: by day close to OSM Carto, the look a crew knows from
/// openstreetmap.org. The map then caches and places the bitmaps tile by tile.
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
    private const int HelpPriority = 2;
    private const int StreetPriority = 3;
    private const int PoiPriority = 4;
    private const int WaterPriority = 5;
    private const int HouseNumberPriority = 6;

    // The dot a POI's name is written beside, and the space between the two.
    private const double PoiDotRadius = 4;
    private const double PoiDotGap = 3;

    // One-way arrows: how far apart along the road, and their length.
    private const double OneWaySpacing = 60;
    private const double OneWayArrowLength = 6;

    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");

    // The POIs an Einsatz cares about, coloured by what they mean for it: the Feuerwehr's own
    // houses, help, many people who need help to get out, public offices, a hazard. Clear of the
    // hydrant red and the open-water blue of the water points drawn over the map. The name is
    // what an unnamed one is called; the legend in LagekarteView shows the same colours. Where help
    // comes from outranks a street's name, the rest give way to it, so a town's streets stay named.
    // OSM's amenity=social_facility is left out: it puts Altenheime with Tafeln and Beratungsstellen,
    // and the tiles carry nothing to tell them apart.
    private static readonly Dictionary<string, (Func<MapStyle, IImmutableBrush> Ink, string Unnamed, int Priority)> Pois = new(StringComparer.Ordinal)
    {
        ["fire_station"] = (s => s.FireStationInk, "Feuerwehr", HelpPriority),
        ["hospital"] = (s => s.HelpInk, "Krankenhaus", HelpPriority),
        ["clinic"] = (s => s.HelpInk, "Klinik", HelpPriority),
        ["police"] = (s => s.HelpInk, "Polizei", HelpPriority),
        ["school"] = (s => s.PeopleInk, "Schule", PoiPriority),
        ["kindergarten"] = (s => s.PeopleInk, "Kita", PoiPriority),
        ["childcare"] = (s => s.PeopleInk, "Kita", PoiPriority),
        ["nursing_home"] = (s => s.PeopleInk, "Pflegeheim", PoiPriority),
        ["townhall"] = (s => s.OfficeInk, "Rathaus", PoiPriority),
        ["pharmacy"] = (s => s.OfficeInk, "Apotheke", PoiPriority),
        ["fuel"] = (s => s.FuelInk, "Tankstelle", PoiPriority),
    };

    // OSM highway value -> width in px at zoom 15, major first; the colours come from the MapStyle.
    private static readonly (string Kind, double WidthAt15)[] Roads =
    [
        ("motorway", 7),
        ("trunk", 7),
        ("primary", 7),
        ("secondary", 6.5),
        ("tertiary", 6),
        ("unclassified", 4.5),
        ("residential", 4.5),
        ("living_street", 4.5),
        ("service", 2.5),
        ("pedestrian", 4),
    ];

    private static readonly HashSet<string> RoadKinds = new(Roads.Select(r => r.Kind), StringComparer.Ordinal);

    /// <summary>
    /// Renders the part of <paramref name="layers"/> -- a tile at <c>zoom - levels</c> -- that
    /// the tile at <paramref name="zoom"/> with index offset (<paramref name="subX"/>,
    /// <paramref name="subY"/>) inside it covers. With <paramref name="levels"/> 0 that is the
    /// whole tile. In the colours of <paramref name="style"/>, <see cref="MapStyle.Day"/> unless given.
    /// </summary>
    /// <remarks>
    /// The bitmap starts transparent and the <c>earth</c> layer paints the land. A pack's tiles
    /// are cut to its area, so outside it they show nothing, and a neighbouring pack drawn
    /// underneath shows through.
    /// </remarks>
    public static RenderTargetBitmap Render(IReadOnlyList<VectorLayer> layers, int zoom, int levels, int subX, int subY, MapStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
        using (var context = bitmap.CreateDrawingContext())
        {
            using (context.PushClip(new Rect(0, 0, Size, Size)))
            {
                Draw(context, layers, zoom, levels, subX, subY, style ?? MapStyle.Day);
            }
        }

        return bitmap;
    }

    private static void Draw(DrawingContext context, IReadOnlyList<VectorLayer> layers, int zoom, int levels, int subX, int subY, MapStyle style)
    {
        VectorLayer? Layer(string name) => layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        // Line widths follow Carto's growth: about half again per zoom level.
        var widthScale = Math.Pow(1.6, zoom - 15);

        if (Layer("earth") is { } earth)
        {
            context.DrawGeometry(style.Earth, null, Polygons(earth.Features.Where(f => f.Type == VectorGeometryType.Polygon), Projection(earth.Extent, levels, subX, subY)));
        }

        if (Layer("landuse") is { } landuse)
        {
            var project = Projection(landuse.Extent, levels, subX, subY);
            foreach (var group in landuse.Features.Where(f => f.Type == VectorGeometryType.Polygon).GroupBy(f => f.Get("kind") ?? string.Empty, StringComparer.Ordinal))
            {
                if (style.Landuse.TryGetValue(group.Key, out var brush))
                {
                    context.DrawGeometry(brush, null, Polygons(group, project));
                }
            }
        }

        if (Layer("water") is { } water)
        {
            var project = Projection(water.Extent, levels, subX, subY);
            context.DrawGeometry(style.Water, null, Polygons(water.Features.Where(f => f.Type == VectorGeometryType.Polygon), project));
            foreach (var group in water.Features.Where(f => f.Type == VectorGeometryType.LineString).GroupBy(f => f.Get("kind") ?? string.Empty, StringComparer.Ordinal))
            {
                var width = group.Key switch { "river" => 5.0, "canal" => 4.0, _ => 1.5 } * widthScale;
                context.DrawGeometry(null, new ImmutablePen(style.Water, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Lines(group, project));
            }
        }

        var buildings = Layer("buildings");
        if (buildings is not null && zoom >= 14)
        {
            var project = Projection(buildings.Extent, levels, subX, subY);
            context.DrawGeometry(style.Building, zoom >= 16 ? new ImmutablePen(style.BuildingOutline, 0.5) : null, Polygons(buildings.Features.Where(f => f.Type == VectorGeometryType.Polygon), project));
        }

        var roads = Layer("roads");
        if (roads is not null)
        {
            DrawRoads(context, roads, zoom, widthScale, Projection(roads.Extent, levels, subX, subY), style);
        }

        if (Layer("boundaries") is { } boundaries)
        {
            context.DrawGeometry(null, new ImmutablePen(style.Boundary, 1.5, new ImmutableDashStyle([4, 2], 0)), Lines(boundaries.Features, Projection(boundaries.Extent, levels, subX, subY)));
        }
    }

    /// <summary>
    /// The names on the tile <see cref="Render"/> draws with the same arguments, in its pixels:
    /// places, rivers and lakes, and from <see cref="PoiLayer.MinZoom"/> the POIs an Einsatz
    /// cares about, each beside a dot in its colour; street names from zoom 15 and house numbers
    /// from zoom 17. A label may reach past the tile's edge; the map draws it whole. In the colours of
    /// <paramref name="style"/>, <see cref="MapStyle.Day"/> unless given.
    /// </summary>
    public static IReadOnlyList<MapLabel> Labels(IReadOnlyList<VectorLayer> layers, int zoom, int levels, int subX, int subY, MapStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var map = style ?? MapStyle.Day;
        VectorLayer? Layer(string name) => layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        var labels = new List<MapLabel>();
        if (Layer("places") is { } places)
        {
            PlaceNames(labels, places, zoom, Projection(places.Extent, levels, subX, subY), map);
        }

        if (Layer("pois") is { } pois && zoom >= PoiLayer.MinZoom)
        {
            PoiNames(labels, pois, Projection(pois.Extent, levels, subX, subY), map);
        }

        if (Layer("roads") is { } roads && zoom >= 15)
        {
            RoadNames(labels, roads, zoom, Projection(roads.Extent, levels, subX, subY), map);
        }

        if (Layer("water") is { } water)
        {
            WaterNames(labels, water, zoom, Projection(water.Extent, levels, subX, subY), map);
        }

        if (Layer("buildings") is { } buildings && zoom >= 17)
        {
            HouseNumbers(labels, buildings, Projection(buildings.Extent, levels, subX, subY), map);
        }

        return labels;
    }

    private static void DrawRoads(DrawingContext context, VectorLayer roads, int zoom, double widthScale, Func<(int X, int Y), Point> project, MapStyle style)
    {
        var lines = roads.Features.Where(f => f.Type == VectorGeometryType.LineString && f.Get("is_tunnel") != "true").ToList();

        context.DrawGeometry(null, new ImmutablePen(style.Rail, 2), Lines(lines.Where(f => f.Get("kind") == "rail"), project));

        if (zoom >= 14)
        {
            context.DrawGeometry(null, new ImmutablePen(style.Track, 1.2, new ImmutableDashStyle([3, 1.5], 0)), Lines(lines.Where(f => f.Get("kind_detail") == "track"), project));
            context.DrawGeometry(null, new ImmutablePen(style.Cycleway, 1.2, new ImmutableDashStyle([1.5, 1.5], 0)), Lines(lines.Where(f => f.Get("kind_detail") == "cycleway"), project));
            context.DrawGeometry(null, new ImmutablePen(style.Footway, 1.2, new ImmutableDashStyle([1.5, 1.5], 0)), Lines(lines.Where(f => f.Get("kind_detail") is "footway" or "path" or "steps" or "bridleway"), project));
        }

        // Minor roads first, so a primary road is drawn over the junction; all casings before
        // all fills, so crossing roads of one class merge instead of cutting each other.
        var groups = Roads.Reverse()
            .Select(c => (Colours: style.Roads[c.Kind], Width: Math.Max(c.WidthAt15 * widthScale, 1.0), Geometry: Lines(lines.Where(f => string.Equals(f.Get("kind_detail"), c.Kind, StringComparison.Ordinal)), project)))
            .ToList();
        foreach (var (colours, width, geometry) in groups)
        {
            context.DrawGeometry(null, new ImmutablePen(colours.Casing, width + 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }

        foreach (var (colours, width, geometry) in groups)
        {
            context.DrawGeometry(null, new ImmutablePen(colours.Fill, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }

        if (zoom >= 16)
        {
            context.DrawGeometry(style.OneWayInk, null, OneWayArrows(lines.Where(f => RoadKinds.Contains(f.Get("kind_detail") ?? string.Empty)), project));
        }
    }

    // Arrowheads along every one-way road, pointing the way it may be driven: OSM's oneway=-1
    // runs against the way's direction.
    private static StreamGeometry OneWayArrows(IEnumerable<VectorFeature> roads, Func<(int X, int Y), Point> project)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var road in roads)
            {
                var direction = road.Get("oneway") switch
                {
                    "yes" or "true" or "1" => 1,
                    "-1" => -1,
                    _ => 0,
                };
                if (direction == 0)
                {
                    continue;
                }

                foreach (var line in road.Parts)
                {
                    // The first arrow half a spacing in, so a short road still gets one.
                    var next = OneWaySpacing / 2;
                    var travelled = 0.0;
                    for (var i = 1; i < line.Count; i++)
                    {
                        var (a, b) = (project(line[i - 1]), project(line[i]));
                        var length = Distance(a, b);
                        for (; next <= travelled + length; next += OneWaySpacing)
                        {
                            var along = (b - a) / length;
                            Arrowhead(g, a + (along * (next - travelled)), along * direction);
                        }

                        travelled += length;
                    }
                }
            }
        }

        return geometry;
    }

    private static void Arrowhead(StreamGeometryContext g, Point at, Vector along)
    {
        var across = new Vector(-along.Y, along.X);
        var half = OneWayArrowLength / 2;
        g.BeginFigure(at + (along * half), isFilled: true);
        g.LineTo(at - (along * half) + (across * half));
        g.LineTo(at - (along * half) - (across * half));
        g.EndFigure(true);
    }

    private static void RoadNames(List<MapLabel> labels, VectorLayer roads, int zoom, Func<(int X, int Y), Point> project, MapStyle map)
    {
        var fontSize = zoom >= 17 ? 12.0 : 10.0;
        foreach (var feature in roads.Features)
        {
            if (feature.Type == VectorGeometryType.LineString && feature.Get("name") is { Length: > 0 } name && RoadKinds.Contains(feature.Get("kind_detail") ?? string.Empty))
            {
                NameAlong(labels, feature, name, new LabelStyle(fontSize, map.LabelInk, StreetPriority), project, map);
            }
        }
    }

    // A river's or a canal's name along it, a stream's only close in; a lake's on its label point.
    private static void WaterNames(List<MapLabel> labels, VectorLayer water, int zoom, Func<(int X, int Y), Point> project, MapStyle map)
    {
        var style = new LabelStyle(11, map.WaterLabelInk, WaterPriority, FontStyle.Italic);
        foreach (var feature in water.Features)
        {
            if (feature.Get("name") is not { Length: > 0 } name)
            {
                continue;
            }

            var kind = feature.Get("kind");
            if (feature.Type == VectorGeometryType.LineString)
            {
                if ((kind is "river" or "canal" && zoom >= 13) || (kind is "stream" && zoom >= 15))
                {
                    NameAlong(labels, feature, name, style, project, map);
                }
            }
            else if (feature.Type == VectorGeometryType.Point && kind is "water" or "lake" or "pond" or "reservoir"
                && feature.Parts is [[var point, ..], ..] && project(point) is var at && OnTile(at) && zoom >= ShownFrom(feature))
            {
                labels.Add(Label(name, style, null, at, map));
            }
        }
    }

    // The Einsatz's POIs that lie on this tile, each named beside a dot in its colour. The dot
    // sits on the spot, so the map pins it there however the map is turned.
    private static void PoiNames(List<MapLabel> labels, VectorLayer pois, Func<(int X, int Y), Point> project, MapStyle map)
    {
        foreach (var feature in pois.Features)
        {
            if (feature.Type != VectorGeometryType.Point || !Pois.TryGetValue(feature.Get("kind") ?? string.Empty, out var poi) || feature.Parts is not [[var point, ..], ..])
            {
                continue;
            }

            var at = project(point);
            if (OnTile(at))
            {
                var name = feature.Get("name") is { Length: > 0 } named ? named : poi.Unnamed;
                labels.Add(Label(name, new LabelStyle(11, poi.Ink(map), poi.Priority, Dot: true), null, at, map));
            }
        }
    }

    // A line's name on its longest segment on this tile, if it fits there; the map turns it so it
    // never reads upside down, for however the map itself is turned.
    private static void NameAlong(List<MapLabel> labels, VectorFeature line, string name, LabelStyle style, Func<(int X, int Y), Point> project, MapStyle map)
    {
        if (LongestSegment(line, project) is not var (a, b) || Distance(a, b) < name.Length * style.Size * 0.55)
        {
            return;
        }

        var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        labels.Add(Label(name, style, angle, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), map));
    }

    // The zoom from which the schema says a point is worth a label: a hamlet is not at zoom 12.
    private static double ShownFrom(VectorFeature feature) =>
        double.TryParse(feature.Get("min_zoom"), NumberStyles.Float, CultureInfo.InvariantCulture, out var minZoom) ? minZoom : 0;

    private static void HouseNumbers(List<MapLabel> labels, VectorLayer buildings, Func<(int X, int Y), Point> project, MapStyle map)
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

            labels.Add(Label(number, new LabelStyle(9, map.HouseNumberInk, HouseNumberPriority, Halo: false), null, center, map));
        }
    }

    private static void PlaceNames(List<MapLabel> labels, VectorLayer places, int zoom, Func<(int X, int Y), Point> project, MapStyle map)
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

            var at = project(feature.Parts[0][0]);
            if (size <= 0 || zoom < ShownFrom(feature) || !OnTile(at))
            {
                continue;
            }

            labels.Add(Label(name, new LabelStyle(size, map.LabelInk, priority), null, at, map));
        }
    }

    // A tile also carries the features just past its edge, as a buffer for lines and areas. A
    // label for a point there belongs to the neighbouring tile, or it would be drawn twice.
    private static bool OnTile(Point at) => at.X is >= 0 and < Size && at.Y is >= 0 and < Size;

    // The text to be centred on `at`, along a street running at `angle`, or level when null. With
    // a dot, the text follows the dot and the dot sits on `at`.
    private static MapLabel Label(string text, LabelStyle style, double? angle, Point at, MapStyle map)
    {
        var typeface = new Typeface(FontFamily.Default, style.FontStyle);
        var formatted = new FormattedText(text, LabelCulture, FlowDirection.LeftToRight, typeface, style.Size, style.Ink);
        var halo = style.Halo ? new ImmutablePen(map.Halo, 3, lineJoin: PenLineJoin.Round) : null;
        if (!style.Dot)
        {
            var outline = formatted.BuildGeometry(default) ?? new StreamGeometry();
            return new MapLabel(text, outline, new Size(formatted.Width, formatted.Height), at, angle, style.Priority, style.Ink, halo);
        }

        var dot = new Point(PoiDotRadius, formatted.Height / 2);
        var indent = (2 * PoiDotRadius) + PoiDotGap;
        var withDot = new GeometryGroup { FillRule = FillRule.NonZero };
        withDot.Children.Add(new EllipseGeometry(new Rect(dot.X - PoiDotRadius, dot.Y - PoiDotRadius, 2 * PoiDotRadius, 2 * PoiDotRadius)));
        if (formatted.BuildGeometry(new Point(indent, 0)) is { } glyphs)
        {
            withDot.Children.Add(glyphs);
        }

        return new MapLabel(text, withDot, new Size(indent + formatted.Width, formatted.Height), at, angle, style.Priority, style.Ink, halo, dot);
    }

    // How a kind of name is written: its size, ink and rank in the collision, italic for water,
    // with the halo unless it is a house number, and a dot before a POI's.
    private sealed record LabelStyle(double Size, IImmutableBrush Ink, int Priority, FontStyle FontStyle = FontStyle.Normal, bool Halo = true, bool Dot = false);

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
}
