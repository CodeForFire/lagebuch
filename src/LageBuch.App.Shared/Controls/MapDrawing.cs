using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Paints the tiles around a centre at a zoom into a rectangle. One routine for the live map and,
/// later, its snapshot in the Bericht, so the two cannot disagree about where a tile goes.
/// </summary>
public static class MapDrawing
{
    // A shade darker than the map's land, so the gap around a pack's edge reads as "no map here", not as an error.
    private static readonly IBrush Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0xE3, 0xDF));

    /// <summary>A street named again closer than this, in pixels, is named once.</summary>
    public const double RepeatDistance = 256;

    // Free space kept around a label, so two names never touch.
    private const double LabelGap = 2;

    /// <summary>
    /// The map around <paramref name="center"/> at <paramref name="zoom"/>, turned clockwise by
    /// <paramref name="rotation"/> degrees about the middle of <paramref name="size"/>. Names are
    /// laid over it upright, a street's name along its street.
    /// </summary>
    public static void Draw(DrawingContext context, MapTileCache tiles, GeoPoint center, int zoom, Size size, double rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(center);
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        context.FillRectangle(Background, new Rect(size));
        if (tiles.Source is not { } source)
        {
            return;
        }

        // Several packs are drawn one over the other. Each pack's tiles are cut to its area when it
        // is built, so outside it a pack draws nothing that could cover its neighbour's streets.
        IReadOnlyList<IMapTileSource> layers = source is CombinedMapTileSource combined ? combined.Layers : [source];
        var turn = Turn(size, rotation);
        var labels = new List<(MapLabel Label, Point At)>();
        foreach (var layer in layers)
        {
            DrawLayer(context, tiles, layer, center, zoom, size, turn, labels);
        }

        // The names of every vector tile, over all tiles and unclipped, so none is cut at a tile's edge.
        foreach (var (label, placement) in PlaceLabels(labels, size, rotation))
        {
            using (context.PushTransform(placement))
            {
                if (label.Halo is { } halo)
                {
                    context.DrawGeometry(null, halo, label.Outline);
                }

                context.DrawGeometry(label.Ink, null, label.Outline);
            }
        }
    }

    /// <summary>
    /// Which of the tiles' labels to draw, in order, each with where it goes on screen: the most
    /// important first, and each only if it lies on the screen, overlaps none placed before it and
    /// does not repeat a name placed within <see cref="RepeatDistance"/>. <paramref name="labels"/>
    /// pairs each label with the point on screen it belongs to; on a map turned by
    /// <paramref name="rotation"/> degrees a street's name turns with its street, never upside
    /// down, and every other name stays level.
    /// </summary>
    public static IReadOnlyList<(MapLabel Label, Matrix Placement)> PlaceLabels(IEnumerable<(MapLabel Label, Point At)> labels, Size size, double rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var screen = new Rect(size);
        var placed = new List<(MapLabel Label, Point Center)>();
        var result = new List<(MapLabel Label, Matrix Placement)>();
        var taken = new List<Rect>();
        foreach (var (label, at) in labels.OrderBy(l => l.Label.Priority))
        {
            var placement = Placement(label, at, rotation);
            var bounds = new Rect(label.TextSize).TransformToAABB(placement);
            if (!screen.Intersects(bounds)
                || taken.Exists(t => t.Intersects(bounds.Inflate(LabelGap)))
                || placed.Exists(p => string.Equals(p.Label.Text, label.Text, StringComparison.Ordinal) && Distance(p.Center, bounds.Center) < RepeatDistance))
            {
                continue;
            }

            placed.Add((label, bounds.Center));
            result.Add((label, placement));
            taken.Add(bounds);
        }

        return result;
    }

    // The text centred on `at`: level, or along its street turned with the map, the right way up.
    private static Matrix Placement(MapLabel label, Point at, double rotation)
    {
        var angle = 0.0;
        if (label.Angle is { } along)
        {
            angle = Math.IEEERemainder(along + (rotation * Math.PI / 180), 2 * Math.PI);
            if (angle > Math.PI / 2)
            {
                angle -= Math.PI;
            }
            else if (angle < -Math.PI / 2)
            {
                angle += Math.PI;
            }
        }

        return Matrix.CreateTranslation(-label.TextSize.Width / 2, -label.TextSize.Height / 2) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(at.X, at.Y);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    // Turns the north-up map clockwise by `rotation` degrees about the middle of the screen.
    private static Matrix Turn(Size size, double rotation)
    {
        var middle = new Vector(size.Width / 2, size.Height / 2);
        return Matrix.CreateTranslation(-middle) * Matrix.CreateRotation(rotation * Math.PI / 180) * Matrix.CreateTranslation(middle);
    }

    private static void DrawLayer(DrawingContext context, MapTileCache tiles, IMapTileSource layer, GeoPoint center, int zoom, Size size, Matrix turn, List<(MapLabel Label, Point At)> labels)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var left = centerX - (size.Width / 2);
        var top = centerY - (size.Height / 2);

        // Turned, the screen shows a tilted square of the north-up map: draw every tile it touches.
        var shown = turn.TryInvert(out var back) ? new Rect(size).TransformToAABB(back) : new Rect(size);
        var (firstX, firstY) = WebMercator.ToTileIndex(left + shown.Left, top + shown.Top);
        var (lastX, lastY) = WebMercator.ToTileIndex(left + shown.Right, top + shown.Bottom);
        var tilesPerSide = 1 << zoom;

        using (context.PushTransform(turn))
        {
            for (var tx = Math.Max(firstX, 0); tx <= Math.Min(lastX, tilesPerSide - 1); tx++)
            {
                for (var ty = Math.Max(firstY, 0); ty <= Math.Min(lastY, tilesPerSide - 1); ty++)
                {
                    var target = new Rect(
                        (tx * (double)WebMercator.TileSizePixels) - left,
                        (ty * (double)WebMercator.TileSizePixels) - top,
                        WebMercator.TileSizePixels,
                        WebMercator.TileSizePixels);

                    // Past the pack's deepest zoom the cache draws the ancestor's part of the tile at
                    // this zoom, so a missing tile here is one the pack does not have.
                    if (tiles.Get(layer, zoom, tx, ty) is { } bitmap)
                    {
                        context.DrawImage(bitmap, target);
                        labels.AddRange(tiles.Labels(layer, zoom, tx, ty).Select(l => (l, turn.Transform(target.TopLeft + l.Anchor))));
                    }
                }
            }
        }
    }

    // DIN 14090 colours: hydrants red, open water and tanks blue.
    private static readonly IBrush HydrantBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
    private static readonly IBrush OpenWaterBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly IPen WaterOutline = new ImmutablePen(Brushes.White, 1.5);
    private static readonly IPen SaugstelleRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)), 3);
    private static readonly IPen SelectedRing = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), 2.5);
    private static readonly IBrush LabelBackground = new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LabelText = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// The water points inside the visible map, one symbol per kind -- a red dot for a hydrant, a
    /// blue ring for a Saugstelle, a blue square for a Löschwasserbehälter, a blue diamond for a
    /// Löschteich -- and from <see cref="WaterPointLayer.LabelsMinZoom"/> their label. A ring marks
    /// <paramref name="selected"/>. Nothing below <see cref="WaterPointLayer.MinZoom"/>.
    /// </summary>
    public static void DrawWaterPoints(DrawingContext context, IReadOnlyList<WaterPoint> points, GeoPoint center, int zoom, Size size, WaterPoint? selected = null, double rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(center);
        if (zoom < WaterPointLayer.MinZoom || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // Compared in degrees first: thousands of points, a few dozen of them on screen. On a
        // turned map the screen's corners are no longer north-west and south-east, so take all four.
        const double margin = 40;
        Point[] corners = [new(-margin, -margin), new(size.Width + margin, -margin), new(-margin, size.Height + margin), new(size.Width + margin, size.Height + margin)];
        var reach = corners.Select(c => ToGeo(center, zoom, size, c, rotation)).ToList();
        var (north, south) = (reach.Max(g => g.Latitude), reach.Min(g => g.Latitude));
        var (west, east) = (reach.Min(g => g.Longitude), reach.Max(g => g.Longitude));
        var radius = zoom >= WaterPointLayer.LabelsMinZoom ? 7.0 : 5.0;
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        foreach (var point in points)
        {
            var p = point.Position;
            if (p.Latitude > north || p.Latitude < south || p.Longitude < west || p.Longitude > east)
            {
                continue;
            }

            var at = ToScreen(center, zoom, size, p, rotation);
            DrawSymbol(context, point.Kind, at, radius);
            var isSelected = ReferenceEquals(point, selected);
            if (isSelected)
            {
                context.DrawEllipse(null, SelectedRing, at, radius + 5, radius + 5);
            }

            if (zoom >= WaterPointLayer.LabelsMinZoom && point.Label.Length > 0)
            {
                var text = new FormattedText(point.Label, LabelCulture, FlowDirection.LeftToRight, typeface, 11, LabelText);
                var origin = new Point(at.X + radius + (isSelected ? 9 : 3), at.Y - (text.Height / 2)); // clear of the selection ring
                context.FillRectangle(LabelBackground, new Rect(origin.X - 2, origin.Y, text.Width + 4, text.Height), 2);
                context.DrawText(text, origin);
            }
        }
    }

    private static void DrawSymbol(DrawingContext context, WaterPointKind kind, Point at, double radius)
    {
        switch (kind)
        {
            case WaterPointKind.Hydrant:
                context.DrawEllipse(HydrantBrush, WaterOutline, at, radius, radius);
                break;
            case WaterPointKind.Saugstelle:
                // White underneath, so the ring reads on a blue river as well as on land.
                context.DrawEllipse(Brushes.White, SaugstelleRing, at, radius - 1, radius - 1);
                break;
            case WaterPointKind.Loeschwasserbehaelter:
                context.DrawRectangle(OpenWaterBrush, WaterOutline, new Rect(at.X - radius, at.Y - radius, 2 * radius, 2 * radius));
                break;
            default:
                var diamond = new StreamGeometry();
                using (var path = diamond.Open())
                {
                    path.BeginFigure(new Point(at.X, at.Y - radius - 1), isFilled: true);
                    path.LineTo(new Point(at.X + radius + 1, at.Y));
                    path.LineTo(new Point(at.X, at.Y + radius + 1));
                    path.LineTo(new Point(at.X - radius - 1, at.Y));
                    path.EndFigure(isClosed: true);
                }

                context.DrawGeometry(OpenWaterBrush, WaterOutline, diamond);
                break;
        }
    }

    /// <summary>
    /// Where <paramref name="point"/> lands on a map of <paramref name="size"/> centred on
    /// <paramref name="center"/> and turned clockwise by <paramref name="rotation"/> degrees.
    /// </summary>
    public static Point ToScreen(GeoPoint center, int zoom, Size size, GeoPoint point, double rotation = 0)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return Turn(size, rotation).Transform(new Point(x - centerX + (size.Width / 2), y - centerY + (size.Height / 2)));
    }

    /// <summary>The inverse of <see cref="ToScreen"/>.</summary>
    public static GeoPoint ToGeo(GeoPoint center, int zoom, Size size, Point screen, double rotation = 0)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var northUp = Turn(size, -rotation).Transform(screen);
        return WebMercator.ToGeo(centerX + northUp.X - (size.Width / 2), centerY + northUp.Y - (size.Height / 2), zoom);
    }

    /// <summary>
    /// A movement of <paramref name="screen"/> pixels on a map turned by <paramref name="rotation"/>
    /// degrees, as the same movement on the north-up map: what a pan has to move the centre by.
    /// </summary>
    public static Vector ToNorthUp(Vector screen, double rotation) =>
        Matrix.CreateRotation(-rotation * Math.PI / 180).Transform(new Point(screen.X, screen.Y)) - default(Point);
}
