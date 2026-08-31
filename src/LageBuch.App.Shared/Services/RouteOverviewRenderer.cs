using Avalonia;
using Avalonia.Media.Imaging;
using LageBuch.App.Shared.Controls;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.App.Shared.Services;

/// <summary>
/// Renders a small map snapshot of a drawn route off-screen for the PDF (#150 phase 2), sharing
/// <see cref="MapDrawing"/> with <see cref="MapCanvasControl"/>'s live view. Lives here (not in
/// LageBuch.App) because App.Shared already has the Avalonia/Skia reference every view uses, and
/// is already where the composition root wires this kind of cross-cutting service.
/// </summary>
public sealed class RouteOverviewRenderer : IRouteOverviewRenderer
{
    private const int ImageWidth = 640;
    private const int ImageHeight = 400;
    private const double Margin = 40;
    private const int MaxZoom = 18;
    private const int MinZoom = 1;

    public byte[]? Render(IReadOnlyList<GeoPoint> routePoints, IMapTileSource tiles)
    {
        ArgumentNullException.ThrowIfNull(routePoints);
        ArgumentNullException.ThrowIfNull(tiles);
        if (routePoints.Count < 2)
        {
            return null;
        }

        var minLat = routePoints.Min(p => p.Latitude);
        var maxLat = routePoints.Max(p => p.Latitude);
        var minLon = routePoints.Min(p => p.Longitude);
        var maxLon = routePoints.Max(p => p.Longitude);
        var center = new GeoPoint((minLat + maxLat) / 2, (minLon + maxLon) / 2);
        var zoom = FitZoom(minLat, minLon, maxLat, maxLon);

        using var bitmap = new RenderTargetBitmap(new PixelSize(ImageWidth, ImageHeight));
        using (var context = bitmap.CreateDrawingContext())
        {
            MapDrawing.Draw(context, tiles, routePoints, center.Latitude, center.Longitude, zoom, ImageWidth, ImageHeight);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>Largest zoom at which the route's bounding box still fits inside the image (minus <see cref="Margin"/>).</summary>
    private static int FitZoom(double minLat, double minLon, double maxLat, double maxLon)
    {
        for (var zoom = MaxZoom; zoom > MinZoom; zoom--)
        {
            var (minX, minY) = WebMercator.ToWorldPixel(new GeoPoint(maxLat, minLon), zoom); // north-west
            var (maxX, maxY) = WebMercator.ToWorldPixel(new GeoPoint(minLat, maxLon), zoom); // south-east
            if (maxX - minX <= ImageWidth - (2 * Margin) && maxY - minY <= ImageHeight - (2 * Margin))
            {
                return zoom;
            }
        }

        return MinZoom;
    }
}
