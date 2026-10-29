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
    // OSM's own land colour, so the gap around a pack's edge reads as "no map here", not as an error.
    private static readonly IBrush Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0xE3, 0xDF));

    public static void Draw(DrawingContext context, MapTileCache tiles, GeoPoint center, int zoom, Size size)
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

        // Several packs are drawn one over the other, each only within its own area: outside it a
        // pack's tiles are empty land that would cover its neighbour's streets.
        IReadOnlyList<MapTileLayer> layers = source is CombinedMapTileSource combined ? combined.Layers : [new MapTileLayer(source, null)];
        foreach (var layer in layers)
        {
            if (layer.Boundary is null)
            {
                DrawLayer(context, tiles, layer.Tiles, center, zoom, size);
                continue;
            }

            using (context.PushGeometryClip(Area(layer.Boundary, center, zoom, size)))
            {
                DrawLayer(context, tiles, layer.Tiles, center, zoom, size);
            }
        }
    }

    private static void DrawLayer(DrawingContext context, MapTileCache tiles, IMapTileSource layer, GeoPoint center, int zoom, Size size)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var left = centerX - (size.Width / 2);
        var top = centerY - (size.Height / 2);
        var (firstX, firstY) = WebMercator.ToTileIndex(left, top);
        var (lastX, lastY) = WebMercator.ToTileIndex(left + size.Width, top + size.Height);
        var maxZoom = layer.GetMaxZoom();
        var tilesPerSide = 1 << zoom;

        for (var tx = Math.Max(firstX, 0); tx <= Math.Min(lastX, tilesPerSide - 1); tx++)
        {
            for (var ty = Math.Max(firstY, 0); ty <= Math.Min(lastY, tilesPerSide - 1); ty++)
            {
                var target = new Rect(
                    (tx * (double)WebMercator.TileSizePixels) - left,
                    (ty * (double)WebMercator.TileSizePixels) - top,
                    WebMercator.TileSizePixels,
                    WebMercator.TileSizePixels);

                if (tiles.Get(layer, zoom, tx, ty) is { } bitmap)
                {
                    context.DrawImage(bitmap, target);
                }
                else if (Overzoom(zoom, tx, ty, maxZoom) is { } ancestor
                    && tiles.Get(layer, ancestor.Zoom, ancestor.X, ancestor.Y) is { } coarse)
                {
                    context.DrawImage(coarse, ancestor.SourceRect, target);
                }
            }
        }
    }

    // The area in screen coordinates. Filled even-odd, so a hole in it stays a hole.
    private static StreamGeometry Area(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, GeoPoint center, int zoom, Size size)
    {
        var geometry = new StreamGeometry();
        using (var area = geometry.Open())
        {
            area.SetFillRule(FillRule.EvenOdd);
            foreach (var ring in rings)
            {
                area.BeginFigure(ToScreen(center, zoom, size, ring[0]), isFilled: true);
                for (var i = 1; i < ring.Count; i++)
                {
                    area.LineTo(ToScreen(center, zoom, size, ring[i]));
                }

                area.EndFigure(isClosed: true);
            }
        }

        return geometry;
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
    public static void DrawWaterPoints(DrawingContext context, IReadOnlyList<WaterPoint> points, GeoPoint center, int zoom, Size size, WaterPoint? selected = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(center);
        if (zoom < WaterPointLayer.MinZoom || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // Compared in degrees first: thousands of points, a few dozen of them on screen.
        const double margin = 40;
        var topLeft = ToGeo(center, zoom, size, new Point(-margin, -margin));
        var bottomRight = ToGeo(center, zoom, size, new Point(size.Width + margin, size.Height + margin));
        var radius = zoom >= WaterPointLayer.LabelsMinZoom ? 7.0 : 5.0;
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        foreach (var point in points)
        {
            var p = point.Position;
            if (p.Latitude > topLeft.Latitude || p.Latitude < bottomRight.Latitude
                || p.Longitude < topLeft.Longitude || p.Longitude > bottomRight.Longitude)
            {
                continue;
            }

            var at = ToScreen(center, zoom, size, p);
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

    /// <summary>Where <paramref name="point"/> lands on a map of <paramref name="size"/> centred on <paramref name="center"/>.</summary>
    public static Point ToScreen(GeoPoint center, int zoom, Size size, GeoPoint point)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return new Point(x - centerX + (size.Width / 2), y - centerY + (size.Height / 2));
    }

    /// <summary>The inverse of <see cref="ToScreen"/>.</summary>
    public static GeoPoint ToGeo(GeoPoint center, int zoom, Size size, Point screen)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        return WebMercator.ToGeo(centerX + screen.X - (size.Width / 2), centerY + screen.Y - (size.Height / 2), zoom);
    }

    /// <summary>
    /// For a zoom past the pack's finest, the ancestor tile at that finest zoom and the part of it
    /// covering this tile: a blurrier but still placed map instead of nothing. Null when the zoom is
    /// within the pack, or the pack has no tiles.
    /// </summary>
    public static (int Zoom, int X, int Y, Rect SourceRect)? Overzoom(int zoom, int x, int y, int? sourceMaxZoom)
    {
        if (sourceMaxZoom is not { } maxZoom || zoom <= maxZoom)
        {
            return null;
        }

        var levels = zoom - maxZoom;
        var ancestorX = x >> levels;
        var ancestorY = y >> levels;
        var side = (double)WebMercator.TileSizePixels / (1 << levels);
        return (maxZoom, ancestorX, ancestorY, new Rect((x - (ancestorX << levels)) * side, (y - (ancestorY << levels)) * side, side, side));
    }
}
