using Avalonia;
using Avalonia.Media;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

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

        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var left = centerX - (size.Width / 2);
        var top = centerY - (size.Height / 2);
        var (firstX, firstY) = WebMercator.ToTileIndex(left, top);
        var (lastX, lastY) = WebMercator.ToTileIndex(left + size.Width, top + size.Height);
        var maxZoom = source.GetMaxZoom();
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

                if (tiles.Get(zoom, tx, ty) is { } bitmap)
                {
                    context.DrawImage(bitmap, target);
                }
                else if (Overzoom(zoom, tx, ty, maxZoom) is { } ancestor
                    && tiles.Get(ancestor.Zoom, ancestor.X, ancestor.Y) is { } coarse)
                {
                    context.DrawImage(coarse, ancestor.SourceRect, target);
                }
            }
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
