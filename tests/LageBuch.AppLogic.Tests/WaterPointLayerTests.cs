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
    public void Of_two_hydrants_on_the_same_spot_the_larger_dn_drawn_on_top_is_meant()
    {
        var small = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 80") { NominalDiameter = 80 };
        var large = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "ÜH 150") { NominalDiameter = 150 };

        Assert.Same(large, WaterPointLayer.At([small, large], Marktplatz, 17));
        Assert.Same(large, WaterPointLayer.At([large, small], Marktplatz, 17));
    }

    [Fact]
    public void An_independent_source_lies_over_a_hydrant_and_a_hydrant_of_unknown_dn_lies_lowest()
    {
        var unknown = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH");
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "ÜH 400") { NominalDiameter = 400 };
        var tank = new WaterPoint(Marktplatz, WaterPointKind.Loeschwasserbehaelter, "Z 3");

        Assert.True(WaterPointLayer.StackRank(unknown, null) < WaterPointLayer.StackRank(hydrant, null));
        Assert.Same(tank, WaterPointLayer.At([tank, hydrant, unknown], Marktplatz, 17));
    }

    [Fact]
    public void Of_two_points_on_the_same_spot_the_open_one_drawn_on_top_is_meant()
    {
        var small = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 80") { NominalDiameter = 80 };
        var tank = new WaterPoint(Marktplatz, WaterPointKind.Loeschwasserbehaelter, "Z 3");

        Assert.Same(small, WaterPointLayer.At([small, tank], Marktplatz, 17, selected: small));
    }

    [Fact]
    public void Of_two_equal_points_on_the_same_spot_the_later_one_drawn_over_the_other_is_meant()
    {
        var first = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100") { NominalDiameter = 100 };
        var second = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100") { NominalDiameter = 100 };

        Assert.Same(second, WaterPointLayer.At([first, second], Marktplatz, 17));
    }

    // Each edge of the full flag's body (mast 10, body 32 high, 120 wide), a finger's spare of 4 px
    // beyond it: 2 px inside that reach still means the point, 2 px outside means none. Every tap is
    // further from the foot than a fingertip, so only the flag can catch it.
    [Theory]
    [InlineData(-2, -44, true)]
    [InlineData(-6, -44, false)]
    [InlineData(122, -26, true)]
    [InlineData(126, -26, false)]
    [InlineData(50, -44, true)]
    [InlineData(50, -48, false)]
    [InlineData(50, -8, true)]
    [InlineData(50, -4, false)]
    public void A_tap_within_a_finger_of_the_flag_s_body_still_means_it(double right, double down, bool meant)
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        var found = WaterPointLayer.At([hydrant], Offset(Marktplatz, 18, right, down), 18);

        Assert.Equal(meant, found is not null);
    }

    [Fact]
    public void Where_no_symbols_are_drawn_no_tap_means_one()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        Assert.Null(WaterPointLayer.At([hydrant], Marktplatz, WaterPointLayer.MinZoom - 1));
    }

    [Theory]
    [InlineData(14, null)]
    [InlineData(15, WaterPointDetailLevel.Punkt)]
    [InlineData(16, WaterPointDetailLevel.Mittel)]
    [InlineData(17, WaterPointDetailLevel.Mittel)]
    [InlineData(18, WaterPointDetailLevel.Voll)]
    [InlineData(20, WaterPointDetailLevel.Voll)]
    public void The_zoom_sets_how_much_of_a_marker_shows(int zoom, WaterPointDetailLevel? level) =>
        Assert.Equal(level, WaterPointLayer.LevelAt(zoom));

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    public void A_tap_on_the_flag_s_body_up_and_right_of_the_point_means_it(int zoom)
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");
        var level = WaterPointLayer.LevelAt(zoom) ?? throw new InvalidOperationException("No marker at this zoom.");
        var flag = WaterPointLayer.FlagFor(level) ?? throw new InvalidOperationException("No flag at this zoom.");

        // Towards the body's far end, well beyond a fingertip from the point.
        var onBody = Offset(Marktplatz, zoom, flag.NominalWidth - 6, -(flag.MastLength + (flag.BodyHeight / 2)));

        Assert.Same(hydrant, WaterPointLayer.At([hydrant], onBody, zoom));
    }

    [Fact]
    public void A_tap_left_of_the_mast_or_under_the_body_means_none()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        Assert.Null(WaterPointLayer.At([hydrant], Offset(Marktplatz, 18, -30, -20), 18));
        Assert.Null(WaterPointLayer.At([hydrant], Offset(Marktplatz, 18, 60, 0), 18));
    }

    [Fact]
    public void At_zoom_15_the_disc_has_no_flag_to_tap()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        Assert.Null(WaterPointLayer.At([hydrant], Offset(Marktplatz, 15, 50, -20), 15));
    }

    [Fact]
    public void On_a_turned_map_the_flag_stays_upright_and_a_tap_on_it_still_means_it()
    {
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");

        // A quarter turn clockwise puts north on the right and west at the top: 60 px right and 20 px
        // up on the screen, inside the body, is 60 px north and 20 px west in the map.
        var onScreenBody = Offset(Marktplatz, 18, -20, -60);

        Assert.Same(hydrant, WaterPointLayer.At([hydrant], onScreenBody, 18, rotation: 90));
        Assert.Null(WaterPointLayer.At([hydrant], onScreenBody, 18));
    }

    private static GeoPoint Offset(GeoPoint point, int zoom, double pixelsEast, double pixelsSouth = 0)
    {
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return WebMercator.ToGeo(x + pixelsEast, y + pixelsSouth, zoom);
    }
}
