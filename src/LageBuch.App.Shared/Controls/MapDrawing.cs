using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The tile+polyline drawing shared by <see cref="MapCanvasControl"/>'s live view and
/// <c>RouteOverviewRenderer</c>'s off-screen PDF snapshot (#150 Plan B) — one implementation of
/// "paint the map centered at (lat,lon)/zoom into this rectangle" for both.
/// </summary>
public static class MapDrawing
{
    private const double RoutePointRadius = 5;

    private static readonly IPen RoutePen = new Pen(Brushes.OrangeRed, 3);
    private static readonly IBrush RoutePointBrush = Brushes.OrangeRed;

    // A selected existing Leitung's saved route (#150 follow-up) is a reference overlay, not the
    // thing being actively drawn -- a visually distinct color keeps it from being mistaken for an
    // in-progress DrawnRoutePoints sketch.
    private static readonly IPen SelectedRoutePen = new Pen(Brushes.DodgerBlue, 3);
    private static readonly IBrush SelectedRoutePointBrush = Brushes.DodgerBlue;

    [SuppressMessage("Design", "CA1062", Justification = "routePoints/selectedRoutePoints are null by design -- a manually entered (Plan A) Leitung or no map-mode drawing/selection in progress -- that is a normal, handled input, not a guard omission.")]
    public static void Draw(
        DrawingContext context,
        IMapTileSource? tileSource,
        IReadOnlyList<GeoPoint>? routePoints,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        double width,
        double height,
        IReadOnlyList<GeoPoint>? selectedRoutePoints = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var (centerX, centerY) = WebMercator.ToWorldPixel(new GeoPoint(centerLatitude, centerLongitude), zoom);

        DrawTiles(context, tileSource, zoom, centerX, centerY, width, height);
        DrawRoute(context, selectedRoutePoints, zoom, centerX, centerY, width, height, SelectedRoutePen, SelectedRoutePointBrush);
        DrawRoute(context, routePoints, zoom, centerX, centerY, width, height, RoutePen, RoutePointBrush);
    }

    private static void DrawTiles(
        DrawingContext context, IMapTileSource? tileSource, int zoom, double centerX, double centerY, double width, double height)
    {
        if (tileSource is null)
        {
            return;
        }

        var (firstTileX, firstTileY) = WebMercator.ToTileIndex(centerX - (width / 2), centerY - (height / 2));
        var (lastTileX, lastTileY) = WebMercator.ToTileIndex(centerX + (width / 2), centerY + (height / 2));
        var sourceMaxZoom = tileSource.GetMaxZoom();

        for (var tx = firstTileX; tx <= lastTileX; tx++)
        {
            for (var ty = firstTileY; ty <= lastTileY; ty++)
            {
                var bytes = tileSource.GetTile(zoom, tx, ty);
                var sourceRect = (Rect?)null;

                if (bytes is null)
                {
                    if (ComputeOverzoomTile(zoom, tx, ty, sourceMaxZoom) is not { } overzoom)
                    {
                        continue;
                    }

                    bytes = tileSource.GetTile(overzoom.Zoom, overzoom.X, overzoom.Y);
                    if (bytes is null)
                    {
                        continue;
                    }

                    sourceRect = overzoom.SourceRect;
                }

                using var stream = new MemoryStream(bytes);
                using var bitmap = new Bitmap(stream);
                var screenX = (tx * WebMercator.TileSizePixels) - centerX + (width / 2);
                var screenY = (ty * WebMercator.TileSizePixels) - centerY + (height / 2);
                var destRect = new Rect(screenX, screenY, WebMercator.TileSizePixels, WebMercator.TileSizePixels);
                context.DrawImage(bitmap, sourceRect ?? new Rect(bitmap.Size), destRect);
            }
        }
    }

    /// <summary>
    /// When the exact tile at (<paramref name="zoom"/>, <paramref name="x"/>, <paramref name="y"/>)
    /// isn't available and <paramref name="zoom"/> is past the source's actual max detail
    /// (<paramref name="sourceMaxZoom"/>), computes the ancestor tile at that max zoom and the
    /// source sub-rect within it covering this tile's area — "overzoom": an increasingly blurry
    /// but still-oriented view past the region's native detail, instead of drawing nothing
    /// (#150 follow-up). Null when zoom is already within range, or the source's max is unknown
    /// (an empty tile source).
    /// </summary>
    public static (int Zoom, int X, int Y, Rect SourceRect)? ComputeOverzoomTile(int zoom, int x, int y, int? sourceMaxZoom)
    {
        if (sourceMaxZoom is not { } maxZoom || zoom <= maxZoom)
        {
            return null;
        }

        var levels = zoom - maxZoom;
        var ancestorX = x >> levels;
        var ancestorY = y >> levels;
        var subSize = (double)WebMercator.TileSizePixels / (1 << levels);
        var subX = (x - (ancestorX << levels)) * subSize;
        var subY = (y - (ancestorY << levels)) * subSize;
        return (maxZoom, ancestorX, ancestorY, new Rect(subX, subY, subSize, subSize));
    }

    private static void DrawRoute(
        DrawingContext context,
        IReadOnlyList<GeoPoint>? routePoints,
        int zoom,
        double centerX,
        double centerY,
        double width,
        double height,
        IPen pen,
        IBrush pointBrush)
    {
        if (routePoints is not { Count: > 0 })
        {
            return;
        }

        Point? previous = null;
        foreach (var geoPoint in routePoints)
        {
            var (worldX, worldY) = WebMercator.ToWorldPixel(geoPoint, zoom);
            var screen = new Point(worldX - centerX + (width / 2), worldY - centerY + (height / 2));
            if (previous is { } prev)
            {
                context.DrawLine(pen, prev, screen);
            }

            context.DrawEllipse(pointBrush, null, screen, RoutePointRadius, RoutePointRadius);
            previous = screen;
        }
    }
}
