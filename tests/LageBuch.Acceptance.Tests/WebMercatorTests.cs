using LageBuch.App.Shared.Controls;
using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Acceptance.Tests;

public class WebMercatorTests
{
    [Fact]
    public void Origin_maps_to_the_center_of_the_world_at_zoom_zero()
    {
        var (x, y) = WebMercator.ToWorldPixel(new GeoPoint(0, 0), zoom: 0);

        Assert.Equal(128, x, 6);
        Assert.Equal(128, y, 6);
    }

    [Fact]
    public void The_antimeridian_maps_to_the_right_edge_of_the_world()
    {
        var (x, _) = WebMercator.ToWorldPixel(new GeoPoint(0, 180), zoom: 0);

        Assert.Equal(256, x, 6);
    }

    [Fact]
    public void Higher_zoom_doubles_the_world_pixel_size()
    {
        var (x0, y0) = WebMercator.ToWorldPixel(new GeoPoint(0, 0), zoom: 0);
        var (x1, y1) = WebMercator.ToWorldPixel(new GeoPoint(0, 0), zoom: 1);

        Assert.Equal(x0 * 2, x1, 6);
        Assert.Equal(y0 * 2, y1, 6);
    }

    [Theory]
    [InlineData(48.1234, 11.4321, 12)]
    [InlineData(0, 0, 5)]
    [InlineData(-33.0, 151.0, 3)]
    [InlineData(85.0, -179.9, 18)]
    public void ToGeo_inverts_ToWorldPixel(double lat, double lon, int zoom)
    {
        var point = new GeoPoint(lat, lon);
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        var back = WebMercator.ToGeo(x, y, zoom);

        Assert.Equal(point.Latitude, back.Latitude, 6);
        Assert.Equal(point.Longitude, back.Longitude, 6);
    }

    [Fact]
    public void TileIndex_floors_the_world_pixel_divided_by_the_tile_size()
    {
        var (x, y) = WebMercator.ToWorldPixel(new GeoPoint(48.0, 11.0), zoom: 10);

        var (tileX, tileY) = WebMercator.ToTileIndex(x, y);

        Assert.Equal((int)Math.Floor(x / 256), tileX);
        Assert.Equal((int)Math.Floor(y / 256), tileY);
    }
}
