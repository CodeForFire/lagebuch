using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using LageBuch.App.Shared.Services;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.Acceptance.Tests;

// Issue #150 (Plan B): the small map snapshot embedded in the PDF for a route-based Leitung.
public class RouteOverviewRendererTests
{
    private sealed class EmptyTileSource : IMapTileSource
    {
        public byte[]? GetTile(int zoom, int x, int y) => null;

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => null;
    }

    [AvaloniaFact]
    public void Renders_a_decodable_png_framing_the_whole_route()
    {
        var renderer = new RouteOverviewRenderer();
        var route = new[] { new GeoPoint(48.0, 11.0), new GeoPoint(48.05, 11.08) };

        var png = renderer.Render(route, new EmptyTileSource());

        Assert.NotNull(png);
        using var ms = new MemoryStream(png!);
        using var bitmap = new Bitmap(ms);
        Assert.True(bitmap.PixelSize.Width > 0);
        Assert.True(bitmap.PixelSize.Height > 0);
    }

    [AvaloniaFact]
    public void Returns_null_for_a_degenerate_single_point_route()
    {
        var renderer = new RouteOverviewRenderer();

        var png = renderer.Render(new[] { new GeoPoint(48.0, 11.0) }, new EmptyTileSource());

        Assert.Null(png);
    }
}
