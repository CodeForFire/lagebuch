using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class LuftbildAreaTests
{
    // Around the Fürstenfeldbrucker Klosterkirche.
    private static readonly GeoPoint Fuerstenfeldbruck = new(48.1785, 11.2555);

    private static GeoBox Around(GeoPoint center, double halfWidthMetres, double halfHeightMetres)
    {
        var (x, y) = WebMercator.ToMetres(center);
        var southWest = WebMercator.FromMetres(x - halfWidthMetres, y - halfHeightMetres);
        var northEast = WebMercator.FromMetres(x + halfWidthMetres, y + halfHeightMetres);
        return new GeoBox(southWest.Longitude, southWest.Latitude, northEast.Longitude, northEast.Latitude);
    }

    [Fact]
    public void A_section_that_fits_is_taken_as_it_is_at_DOP40_resolution()
    {
        var area = Assert.IsType<LuftbildArea>(LuftbildArea.For(Around(Fuerstenfeldbruck, 600, 300)));

        Assert.Equal(2000, area.Width);
        Assert.Equal(1000, area.Height);
        Assert.Equal(LuftbildArea.MetresPerPixel, (area.MaxX - area.MinX) / area.Width, precision: 9);
        Assert.Equal(LuftbildArea.MetresPerPixel, (area.MaxY - area.MinY) / area.Height, precision: 9);
    }

    [Fact]
    public void A_larger_section_is_cut_to_the_cap_around_its_middle()
    {
        var visible = Around(Fuerstenfeldbruck, 8000, 1000);

        var area = Assert.IsType<LuftbildArea>(LuftbildArea.For(visible));

        Assert.Equal(LuftbildArea.MaxPixels, area.Width);
        Assert.Equal(3333, area.Height);
        var (x, y) = WebMercator.ToMetres(Fuerstenfeldbruck);
        Assert.True(Math.Abs(((area.MinX + area.MaxX) / 2) - x) < 1);
        Assert.True(Math.Abs(((area.MinY + area.MaxY) / 2) - y) < 1);
    }

    [Fact]
    public void A_tiny_section_still_gets_a_useful_picture()
    {
        var area = Assert.IsType<LuftbildArea>(LuftbildArea.For(Around(Fuerstenfeldbruck, 20, 10)));

        Assert.Equal(LuftbildArea.MinPixels, area.Width);
        Assert.Equal(LuftbildArea.MinPixels, area.Height);
    }

    [Fact]
    public void The_scale_stays_under_the_Parzellarkarte_s_limit_of_1_to_5000()
    {
        var area = Assert.IsType<LuftbildArea>(LuftbildArea.For(Around(Fuerstenfeldbruck, 50_000, 50_000)));

        Assert.InRange(area.ScaleDenominator, 2000, 5000);
    }

    [Fact]
    public void The_bounds_are_the_exact_corners_of_the_requested_box()
    {
        var area = Assert.IsType<LuftbildArea>(LuftbildArea.For(Around(Fuerstenfeldbruck, 600, 300)));

        var (west, south) = WebMercator.ToMetres(new GeoPoint(area.Bounds.South, area.Bounds.West));
        var (east, north) = WebMercator.ToMetres(new GeoPoint(area.Bounds.North, area.Bounds.East));
        Assert.True(Math.Abs(west - area.MinX) < 0.001);
        Assert.True(Math.Abs(south - area.MinY) < 0.001);
        Assert.True(Math.Abs(east - area.MaxX) < 0.001);
        Assert.True(Math.Abs(north - area.MaxY) < 0.001);
    }

    [Theory]
    [InlineData(52.52, 13.40)] // Berlin
    [InlineData(48.21, 16.37)] // Wien
    [InlineData(49.45, 8.20)] // Pfalz, west of Bavaria
    public void A_section_outside_Bavaria_is_refused(double latitude, double longitude)
    {
        Assert.Null(LuftbildArea.For(Around(new GeoPoint(latitude, longitude), 500, 500)));
    }

    [Theory]
    [InlineData(47.42, 10.98)] // Zugspitze
    [InlineData(50.32, 11.91)] // Hof
    [InlineData(49.79, 9.95)] // Würzburg
    public void A_section_anywhere_in_Bavaria_is_taken(double latitude, double longitude)
    {
        Assert.NotNull(LuftbildArea.For(Around(new GeoPoint(latitude, longitude), 500, 500)));
    }
}
