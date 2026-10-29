using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class WebMercatorTests
{
    [Fact]
    public void The_origin_lies_in_the_middle_of_the_world()
    {
        var (x, y) = WebMercator.ToWorldPixel(new GeoPoint(0, 0), 0);

        Assert.Equal(128, x, precision: 6);
        Assert.Equal(128, y, precision: 6);
    }

    [Fact]
    public void A_position_survives_the_round_trip_through_pixels()
    {
        var fuerstenfeldbruck = new GeoPoint(48.1785, 11.2555);
        var (x, y) = WebMercator.ToWorldPixel(fuerstenfeldbruck, 15);

        var back = WebMercator.ToGeo(x, y, 15);

        Assert.Equal(fuerstenfeldbruck.Latitude, back.Latitude, precision: 6);
        Assert.Equal(fuerstenfeldbruck.Longitude, back.Longitude, precision: 6);
    }

    [Fact]
    public void A_known_position_falls_in_its_osm_tile()
    {
        // Fürstenfeldbruck's Marktplatz is OSM tile 15/17408/11366.
        var (x, y) = WebMercator.ToWorldPixel(new GeoPoint(48.1785, 11.2555), 15);

        Assert.Equal((17408, 11366), WebMercator.ToTileIndex(x, y));
    }

    [Theory]
    [InlineData(-500, -500)]
    [InlineData(1e9, 1e9)]
    public void A_pixel_dragged_off_the_world_comes_back_on_its_edge(double x, double y)
    {
        var point = WebMercator.ToGeo(x, y, 3);

        Assert.InRange(point.Latitude, -WebMercator.MaxLatitude, WebMercator.MaxLatitude);
        Assert.InRange(point.Longitude, -180, 180);
    }

    [Fact]
    public void The_centre_of_a_tile_range_is_its_middle()
    {
        // Zoom 1 has four tiles; the middle of all four is the origin.
        var center = WebMercator.CenterOfTiles(1, 0, 1, 0, 1);

        Assert.Equal(0, center.Latitude, precision: 6);
        Assert.Equal(0, center.Longitude, precision: 6);
    }

    [Fact]
    public void A_tile_range_past_the_world_edge_centres_on_that_edge_not_somewhere_else()
    {
        // A tampered pack can name columns far beyond 2^zoom; the sum must not wrap an int.
        var center = WebMercator.CenterOfTiles(22, 10_000_000, 10_000_000, 0, 0);

        Assert.Equal(180, center.Longitude, precision: 6);
    }
}
