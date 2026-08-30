using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private static readonly IPen RoutePen = new Pen(Brushes.OrangeRed, 3);
    private static readonly IBrush RoutePointBrush = Brushes.OrangeRed;
    private const double RoutePointRadius = 5;

    public static void Draw(
        DrawingContext context, IMapTileSource? tileSource, IReadOnlyList<GeoPoint>? routePoints,
        double centerLatitude, double centerLongitude, int zoom, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;

        var (centerX, centerY) = WebMercator.ToWorldPixel(new GeoPoint(centerLatitude, centerLongitude), zoom);

        DrawTiles(context, tileSource, zoom, centerX, centerY, width, height);
        DrawRoute(context, routePoints, zoom, centerX, centerY, width, height);
    }

    private static void DrawTiles(
        DrawingContext context, IMapTileSource? tileSource, int zoom, double centerX, double centerY, double width, double height)
    {
        if (tileSource is null)
            return;

        var (firstTileX, firstTileY) = WebMercator.ToTileIndex(centerX - width / 2, centerY - height / 2);
        var (lastTileX, lastTileY) = WebMercator.ToTileIndex(centerX + width / 2, centerY + height / 2);

        for (var tx = firstTileX; tx <= lastTileX; tx++)
        {
            for (var ty = firstTileY; ty <= lastTileY; ty++)
            {
                var bytes = tileSource.GetTile(zoom, tx, ty);
                if (bytes is null)
                    continue;

                using var stream = new MemoryStream(bytes);
                using var bitmap = new Bitmap(stream);
                var screenX = tx * WebMercator.TileSizePixels - centerX + width / 2;
                var screenY = ty * WebMercator.TileSizePixels - centerY + height / 2;
                var destRect = new Rect(screenX, screenY, WebMercator.TileSizePixels, WebMercator.TileSizePixels);
                context.DrawImage(bitmap, new Rect(bitmap.Size), destRect);
            }
        }
    }

    private static void DrawRoute(
        DrawingContext context, IReadOnlyList<GeoPoint>? routePoints, int zoom, double centerX, double centerY,
        double width, double height)
    {
        if (routePoints is not { Count: > 0 })
            return;

        Point? previous = null;
        foreach (var geoPoint in routePoints)
        {
            var (worldX, worldY) = WebMercator.ToWorldPixel(geoPoint, zoom);
            var screen = new Point(worldX - centerX + width / 2, worldY - centerY + height / 2);
            if (previous is { } prev)
                context.DrawLine(RoutePen, prev, screen);
            context.DrawEllipse(RoutePointBrush, null, screen, RoutePointRadius, RoutePointRadius);
            previous = screen;
        }
    }
}
