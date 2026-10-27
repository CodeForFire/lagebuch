using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class WaterPointLayerTests
{
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);

    [Fact]
    public void A_tap_on_a_symbol_means_that_water_point()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        Assert.Same(hydrant, WaterPointLayer.At([hydrant], Offset(Marktplatz, 17, 10), 17));
    }

    [Fact]
    public void A_tap_further_than_a_fingertip_means_none()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        Assert.Null(WaterPointLayer.At([hydrant], Offset(Marktplatz, 17, WaterPointLayer.HitRadiusPixels + 2), 17));
    }

    [Fact]
    public void Of_two_points_in_reach_the_nearer_one_is_meant()
    {
        var near = new WaterPoint(Offset(Marktplatz, 17, 4), WaterPointKind.Hydrant, "near");
        var far = new WaterPoint(Offset(Marktplatz, 17, 12), WaterPointKind.Saugstelle, "far");

        Assert.Same(near, WaterPointLayer.At([far, near], Marktplatz, 17));
    }

    [Fact]
    public void Where_no_symbols_are_drawn_no_tap_means_one()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        Assert.Null(WaterPointLayer.At([hydrant], Marktplatz, WaterPointLayer.MinZoom - 1));
    }

    private static GeoPoint Offset(GeoPoint point, int zoom, double pixelsEast)
    {
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return WebMercator.ToGeo(x + pixelsEast, y, zoom);
    }
}
