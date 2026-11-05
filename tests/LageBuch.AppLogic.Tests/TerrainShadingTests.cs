using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public sealed class TerrainShadingTests
{
    private const int Side = TerrainSampler.Side;

    [Fact]
    public void Flat_ground_gets_no_shading_so_the_map_keeps_its_colours()
    {
        var tile = Render(TerrainLayer.Schummerung, (_, _) => 520);

        Assert.All(Pixels(tile), p => Assert.Equal(0, Alpha(p)));
    }

    [Fact]
    public void A_slope_facing_the_north_west_light_is_lightened_and_one_facing_away_darkened()
    {
        // Rising to the east, the slope faces west; rising to the west, it faces east.
        var facingWest = Pixel(Render(TerrainLayer.Schummerung, (x, _) => 500 + (0.3f * x)), 128, 128);
        var facingEast = Pixel(Render(TerrainLayer.Schummerung, (x, _) => 600 - (0.3f * x)), 128, 128);

        Assert.True(Alpha(facingWest) > 0);
        Assert.Equal(Alpha(facingWest), Red(facingWest)); // premultiplied white
        Assert.True(Alpha(facingEast) > 0);
        Assert.Equal(0, Red(facingEast)); // black
    }

    [Theory]
    [InlineData(5, 0, 0)]
    [InlineData(15, 0xFF, 0xD9)] // gelb
    [InlineData(25, 0xFF, 0x80)] // orange
    [InlineData(40, 0xD9, 0x00)] // rot
    public void A_slope_is_coloured_by_its_class(double percent, int red, int green)
    {
        var pixel = Pixel(Render(TerrainLayer.Hangneigung, (x, _) => (float)(500 + (percent / 100 * x))), 128, 128);

        if (red == 0)
        {
            Assert.Equal(0, Alpha(pixel));
            return;
        }

        var alpha = Alpha(pixel);
        Assert.Equal(140, alpha);
        Assert.InRange(Red(pixel), (red * alpha / 255) - 1, (red * alpha / 255) + 1);
        Assert.InRange(Green(pixel), (green * alpha / 255) - 1, (green * alpha / 255) + 1);
    }

    [Fact]
    public void Below_the_Einsatzort_is_blue_above_it_red_and_far_off_more_opaque()
    {
        var style = new TerrainStyle(TerrainLayer.RelativZumEinsatzort, ReferenceHeight: 550);

        var below = Pixel(Render(style, (_, _) => 520), 10, 10);
        var above = Pixel(Render(style, (_, _) => 580), 10, 10);
        var level = Pixel(Render(style, (_, _) => 550), 10, 10);
        var slightlyAbove = Pixel(Render(style, (_, _) => 560), 10, 10);

        Assert.True(Blue(below) > Red(below));
        Assert.True(Red(above) > Blue(above));
        Assert.True(Alpha(level) < Alpha(slightlyAbove));
        Assert.True(Alpha(slightlyAbove) < Alpha(above));
    }

    [Fact]
    public void Without_an_Einsatzort_height_the_relative_layer_draws_nothing()
    {
        var tile = Render(new TerrainStyle(TerrainLayer.RelativZumEinsatzort), (x, _) => 500 + x);

        Assert.Null(tile.Pixels);
        Assert.Empty(tile.Contours);
    }

    [Fact]
    public void The_Einsatzort_s_own_height_is_the_heavy_line_and_every_other_line_is_labelled()
    {
        // 538 m at the tile's west edge to 563.6 m at its east edge.
        var tile = Render(new TerrainStyle(TerrainLayer.RelativZumEinsatzort, ReferenceHeight: 550), (x, _) => 538 + (0.1f * x));

        var levels = tile.Contours.ToDictionary(c => c.Height);
        Assert.Equal([540.0, 545, 550, 555, 560], levels.Keys.Order());
        Assert.True(levels[550].IsMajor);
        Assert.Equal("±0 m", levels[550].Label);
        Assert.Equal("−10 m", levels[540].Label);
        Assert.Equal("+10 m", levels[560].Label);
        Assert.Equal(string.Empty, levels[545].Label);
        Assert.False(levels[560].IsMajor);
    }

    [Fact]
    public void A_contour_runs_where_the_ground_crosses_its_height()
    {
        // 0.1 m a pixel to the east: 505 m lies halfway between samples 50 and 51, the centres of
        // the tile's pixels 48 and 49 -- at 49.0.
        var tile = Render(TerrainLayer.Hoehenlinien, (x, _) => 499.95f + (0.1f * x), zoom: 15);

        var line = Assert.Single(tile.Contours, c => Math.Abs(c.Height - 505) < 1e-9);
        Assert.All(line.Segments, s =>
        {
            Assert.Equal(49.0, s.X1, precision: 3);
            Assert.Equal(49.0, s.X2, precision: 3);
        });
        Assert.Equal(WebMercator.TileSizePixels + 1, line.Segments.Count); // one per row of cells, the one past the tile's edge included
    }

    [Theory]
    [InlineData(15, 5)]
    [InlineData(14, 5)]
    [InlineData(13, 10)]
    public void Contours_are_spaced_by_zoom_and_every_fifth_is_a_labelled_Zaehllinie(int zoom, double interval)
    {
        var tile = Render(TerrainLayer.Hoehenlinien, (x, _) => 480 + (0.5f * x), zoom);

        Assert.Equal(interval, TerrainShading.ContourInterval(zoom));
        Assert.All(tile.Contours, c => Assert.Equal(0, Math.IEEERemainder(c.Height, interval), precision: 9));
        var zaehllinie = Assert.Single(tile.Contours, c => Math.Abs(c.Height - 550) < 1e-9);
        Assert.True(zaehllinie.IsMajor);
        Assert.Equal("550 m", zaehllinie.Label);
        Assert.All(tile.Contours.Where(c => !c.IsMajor), c => Assert.Equal(string.Empty, c.Label));
        Assert.Null(tile.Pixels);
    }

    [Fact]
    public void Hoehenschichten_run_from_green_at_the_low_end_to_brown_at_the_high_end()
    {
        var style = new TerrainStyle(TerrainLayer.Hoehenschichten, Low: 500, High: 600);

        var low = Pixel(Render(style, (_, _) => 500), 10, 10);
        var high = Pixel(Render(style, (_, _) => 600), 10, 10);

        Assert.True(Green(low) > Red(low));
        Assert.True(Red(high) > Green(high));
        Assert.Equal(107, Alpha(low));
    }

    [Fact]
    public void Where_there_are_no_heights_nothing_is_drawn()
    {
        var tile = Render(TerrainLayer.Hangneigung, (x, _) => x < Side / 2 ? float.NaN : 500 + (0.5f * x));

        Assert.Equal(0, Alpha(Pixel(tile, 20, 20)));
        Assert.NotEqual(0, Alpha(Pixel(tile, 200, 20)));
    }

    [Fact]
    public void A_tile_spanning_more_height_than_any_terrain_draws_no_contours_instead_of_millions()
    {
        // 5 km of height across one tile: bad data, which must cost the lines, not the app.
        var tile = Render(TerrainLayer.Hoehenlinien, (x, _) => x * 20f);

        Assert.Empty(tile.Contours);
    }

    [Fact]
    public void Heights_of_the_wrong_size_are_refused()
    {
        Assert.Throws<ArgumentException>(() => TerrainShading.Render(new float[10], 1, 15, new TerrainStyle(TerrainLayer.Schummerung)));
    }

    private static TerrainTile Render(TerrainLayer layer, Func<int, int, float> height, int zoom = 15) =>
        Render(new TerrainStyle(layer), height, zoom);

    // Heights by sample column and row, the margin included; one metre a pixel.
    private static TerrainTile Render(TerrainStyle style, Func<int, int, float> height, int zoom = 15)
    {
        var heights = new float[Side * Side];
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                heights[(y * Side) + x] = height(x, y);
            }
        }

        return TerrainShading.Render(heights, 1, zoom, style);
    }

    private static int[] Pixels(TerrainTile tile) => tile.Pixels?.ToArray() ?? new int[WebMercator.TileSizePixels * WebMercator.TileSizePixels];

    private static int Pixel(TerrainTile tile, int x, int y) => Pixels(tile)[(y * WebMercator.TileSizePixels) + x];

    private static int Alpha(int pixel) => (pixel >> 24) & 0xFF;

    private static int Red(int pixel) => (pixel >> 16) & 0xFF;

    private static int Green(int pixel) => (pixel >> 8) & 0xFF;

    private static int Blue(int pixel) => pixel & 0xFF;
}
