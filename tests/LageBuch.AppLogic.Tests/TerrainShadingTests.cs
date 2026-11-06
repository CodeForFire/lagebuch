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

    // Shaded relief, worked out by hand for a slope of 0.3 (or 0.1) m per m, 2.5 × exaggerated:
    // flat ground reflects sin 45° = 0.707 of the light; more is drawn white at up to 35 %, less
    // black at up to 60 %, both doubled from the flat value and capped at full strength.
    [Theory]
    [InlineData(0.3, 0, 255, 75)] // facing west, into the light: 0.831 reflected
    [InlineData(-0.3, 0, 0, 153)] // facing east, away from it: 0.301, capped
    [InlineData(0.1, 0, 255, 52)] // facing west, gentler: 0.793
    [InlineData(-0.1, 0, 0, 55)] // facing east, gentler: 0.579
    [InlineData(0, 0.3, 0, 124)] // facing south: 0.421
    [InlineData(0, -0.3, 255, -1)] // facing north, towards the light from the north: lightened
    public void A_slope_is_shaded_by_how_much_light_it_reflects(double eastRise, double northRise, int white, int alpha)
    {
        // A rise to the north is a fall with the row number: rows run south.
        var pixel = Pixel(Render(TerrainLayer.Schummerung, (x, y) => (float)(500 + (eastRise * x) - (northRise * y))), 128, 128);

        if (alpha < 0)
        {
            Assert.True(Alpha(pixel) > 0);
        }
        else
        {
            Assert.InRange(Alpha(pixel), alpha - 1, alpha + 1);
        }

        Assert.Equal(white == 0 ? 0 : Alpha(pixel), Red(pixel));
    }

    [Theory]
    [InlineData(-0.3, 0, 102)] // facing east: black, capped at 40 % rather than Schummerung's 60 %
    [InlineData(0.3, 0, 43)] // facing west: white at 20 % of the 0.84 reached, not 35 %
    public void Under_the_Fliessweg_the_relief_is_lighter_so_the_line_stands_out(double eastRise, double northRise, int alpha)
    {
        var pixel = Pixel(Render(TerrainLayer.Fliessweg, (x, y) => (float)(500 + (eastRise * x) - (northRise * y))), 128, 128);

        Assert.InRange(Alpha(pixel), alpha - 1, alpha + 1);
    }

    [Fact]
    public void Relief_measures_slope_in_metres_so_a_coarser_pixel_flattens_it()
    {
        // 0.3 m per pixel at 2 m a pixel is 0.15 m per m: the "facing west, gentler"-like value.
        var coarse = Pixel(Render(new TerrainStyle(TerrainLayer.Schummerung), (x, _) => (float)(500 + (0.3 * x)), metresPerPixel: 2), 128, 128);
        var fine = Pixel(Render(TerrainLayer.Schummerung, (x, _) => (float)(500 + (0.3 * x))), 128, 128);

        Assert.True(Alpha(coarse) < Alpha(fine));
    }

    [Theory]
    [InlineData(0.12, 0.09, 0xFF, 0xD9)] // 15 %: gelb
    [InlineData(0.15, 0.2, 0xFF, 0x80)] // 25 %: orange
    [InlineData(0, -0.4, 0xD9, 0x00)] // 40 % to the north: rot
    public void A_slope_s_steepness_takes_both_directions_into_account(double eastRise, double northRise, int red, int green)
    {
        var pixel = Pixel(Render(TerrainLayer.Hangneigung, (x, y) => (float)(500 + (eastRise * x) - (northRise * y))), 128, 128);

        var alpha = Alpha(pixel);
        Assert.Equal(140, alpha);
        Assert.InRange(Red(pixel), (red * alpha / 255) - 1, (red * alpha / 255) + 1);
        Assert.InRange(Green(pixel), (green * alpha / 255) - 1, (green * alpha / 255) + 1);
    }

    [Fact]
    public void Steepness_is_in_percent_of_ground_metres()
    {
        // 0.3 m per pixel at 2 m a pixel: 15 %, gelb rather than the 30 % orange.
        var pixel = Pixel(Render(new TerrainStyle(TerrainLayer.Hangneigung), (x, _) => (float)(500 + (0.3 * x)), metresPerPixel: 2), 128, 128);

        Assert.InRange(Green(pixel), (0xD9 * 140 / 255) - 1, (0xD9 * 140 / 255) + 1);
    }

    [Theory]
    [InlineData(561, 0xFF, 0xF1, 0x76)] // halfway: the ramp's yellow
    [InlineData(515.25, 0x65, 0xA4, 0x4B)] // an eighth: halfway from dark to light green
    public void Hoehenschichten_blend_along_the_ramp(double height, int red, int green, int blue)
    {
        var style = new TerrainStyle(TerrainLayer.Hoehenschichten, Low: 500, High: 622);

        var pixel = Pixel(Render(style, (_, _) => (float)height), 10, 10);

        Assert.Equal(107, Alpha(pixel));
        Assert.InRange(Red(pixel), (red * 107 / 255) - 1, (red * 107 / 255) + 1);
        Assert.InRange(Green(pixel), (green * 107 / 255) - 1, (green * 107 / 255) + 1);
        Assert.InRange(Blue(pixel), (blue * 107 / 255) - 1, (blue * 107 / 255) + 1);
    }

    [Fact]
    public void Hoehenschichten_darken_a_slope_away_from_the_light_over_their_colour()
    {
        // Facing east at 0.3: the relief's black at 35 % over the yellow at 42 %: 62 % opaque, the
        // yellow showing through at 0.42 × 0.65 = 27 %.
        var style = new TerrainStyle(TerrainLayer.Hoehenschichten, Low: 500, High: 622);

        var pixel = Pixel(Render(style, (x, _) => (float)(561 + (0.3 * (130 - x)))), 128, 128);

        Assert.InRange(Alpha(pixel), 158, 160);
        Assert.InRange(Red(pixel), 69, 71);
        Assert.InRange(Green(pixel), 65, 67);
        Assert.InRange(Blue(pixel), 31, 33);
    }

    [Fact]
    public void Without_a_range_a_single_height_still_gets_a_colour()
    {
        var pixel = Pixel(Render(new TerrainStyle(TerrainLayer.Hoehenschichten, Low: 550, High: 550), (_, _) => 550.5f), 10, 10);

        // Low equals High: one metre of range, so 550.5 m is halfway, the ramp's yellow.
        Assert.InRange(Green(pixel), (0xF1 * 107 / 255) - 1, (0xF1 * 107 / 255) + 1);
    }

    [Theory]
    [InlineData(520, 0x21, 0x66, 0xAC, 166)] // 30 m below: the full blue at 65 %
    [InlineData(565, 0xD8, 0x8C, 0x95, 108)] // 15 m above: halfway from white to the red, at 42.5 %
    [InlineData(550, 0xFF, 0xFF, 0xFF, 51)] // the Einsatzort's own height: a faint white
    public void Relative_colours_are_mixed_from_white_by_the_height_difference(double height, int red, int green, int blue, int alpha)
    {
        var pixel = Pixel(Render(new TerrainStyle(TerrainLayer.RelativZumEinsatzort, ReferenceHeight: 550), (_, _) => (float)height), 10, 10);

        Assert.Equal(alpha, Alpha(pixel));
        Assert.InRange(Red(pixel), (red * alpha / 255) - 1, (red * alpha / 255) + 1);
        Assert.InRange(Green(pixel), (green * alpha / 255) - 1, (green * alpha / 255) + 1);
        Assert.InRange(Blue(pixel), (blue * alpha / 255) - 1, (blue * alpha / 255) + 1);
    }

    [Fact]
    public void Relative_lines_count_from_the_Einsatzort_s_own_height()
    {
        // 552 m: lines at 542, 547, 552, 557, 562, labelled every 10 m from 552.
        var tile = Render(new TerrainStyle(TerrainLayer.RelativZumEinsatzort, ReferenceHeight: 552), (x, _) => 538 + (0.1f * x));

        var levels = tile.Contours.ToDictionary(c => c.Height);
        Assert.Equal([542.0, 547, 552, 557, 562], levels.Keys.Order());
        Assert.Equal("−10 m", levels[542].Label);
        Assert.Equal(string.Empty, levels[547].Label);
        Assert.Equal("+10 m", levels[562].Label);
        Assert.Equal([552.0], tile.Contours.Where(c => c.IsMajor).Select(c => c.Height));
    }

    [Fact]
    public void Every_fifth_contour_and_only_it_is_a_Zaehllinie()
    {
        var tile = Render(TerrainLayer.Hoehenlinien, (x, _) => 480 + (0.5f * x), zoom: 15);

        Assert.Equal([500.0, 525, 550, 575, 600], tile.Contours.Where(c => c.IsMajor).Select(c => c.Height));
        Assert.Equal("500 m", tile.Contours.Single(c => Math.Abs(c.Height - 500) < 1e-9).Label);
        Assert.Equal(string.Empty, tile.Contours.Single(c => Math.Abs(c.Height - 505) < 1e-9).Label);
    }

    [Fact]
    public void The_contours_of_a_cone_are_closed_rings_at_the_right_radius()
    {
        // 600 m at the tile's middle, falling 0.5 m per pixel: the 575 m line is a circle of 50 px.
        var tile = Render(TerrainLayer.Hoehenlinien, (x, y) => (float)(600 - (0.5 * Math.Sqrt(((x - 130.0) * (x - 130.0)) + ((y - 130.0) * (y - 130.0))))), zoom: 15);
        var ring = Assert.Single(tile.Contours, c => Math.Abs(c.Height - 575) < 1e-9);

        // Every end is shared with exactly one other piece: the line never breaks.
        var ends = ring.Segments.SelectMany(s => new[] { (Math.Round(s.X1, 6), Math.Round(s.Y1, 6)), (Math.Round(s.X2, 6), Math.Round(s.Y2, 6)) });
        Assert.All(ends.GroupBy(e => e), g => Assert.Equal(2, g.Count()));
        Assert.All(ring.Segments, s =>
        {
            // The tile's pixel (x, y) is the sample (x + 2, y + 2); its centre lies at x + 0.5.
            var r = Math.Sqrt(((s.X1 - 128.5) * (s.X1 - 128.5)) + ((s.Y1 - 128.5) * (s.Y1 - 128.5)));
            Assert.InRange(r, 49.0, 51.0);
        });
    }

    [Theory]
    [InlineData(1)] // high to the north-west and south-east
    [InlineData(-1)] // high to the north-east and south-west
    public void A_saddle_cell_gets_two_lines_not_one(int sign)
    {
        var tile = Render(TerrainLayer.Hoehenlinien, (x, y) => (float)(500 + (sign * (x - 130.5) * (y - 130.5) * 0.01)), zoom: 15);

        // The saddle point is at the middle of the cell from pixel 128 to 129, both ways.
        var through = tile.Contours.Single(c => Math.Abs(c.Height - 500) < 1e-9).Segments
            .Count(s => (s.X1 + s.X2) / 2 is > 128.5 and < 129.5 && (s.Y1 + s.Y2) / 2 is > 128.5 and < 129.5);
        Assert.Equal(2, through);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ground_exactly_at_a_contour_s_height_counts_as_above_it(bool highToTheWest)
    {
        // A step: 505 m on one half, 500 m on the other. The 505 m line runs straight along the step.
        var tile = Render(TerrainLayer.Hoehenlinien, (x, _) => (x < 130) == highToTheWest ? 505f : 500f, zoom: 15);

        var line = Assert.Single(tile.Contours, c => Math.Abs(c.Height - 505) < 1e-9);
        Assert.All(line.Segments, s => Assert.Equal(s.X1, s.X2, precision: 9));
    }

    [Fact]
    public void A_contour_reaches_the_tile_s_west_edge_and_stops_one_cell_past_the_east_edge()
    {
        // 500 m lies between samples 1 (499.95 m) and 2 (500.05 m): at the tile's x = 0.0, the
        // edge of the cell just outside the tile.
        var west = Render(TerrainLayer.Hoehenlinien, (x, _) => 499.85f + (0.1f * x), zoom: 15);
        Assert.Equal(WebMercator.TileSizePixels + 1, west.Contours.Single(c => Math.Abs(c.Height - 500) < 1e-9).Segments.Count(s => Math.Abs(s.X1) < 0.01));

        // A line only beyond the last cell, at sample 258.5, is not drawn by this tile.
        var east = Render(TerrainLayer.Hoehenlinien, (x, _) => x >= 259 ? 510f : 500f, zoom: 15);
        Assert.DoesNotContain(east.Contours.SelectMany(c => c.Segments), s => s.X1 > 256.6);
    }

    [Fact]
    public void A_level_whose_only_crossing_lies_in_the_margin_is_left_out()
    {
        // Sample column 0 is 510 m; smoothed, its neighbour reaches 503.3 m. No cell crosses 505 m or 510 m.
        var tile = Render(TerrainLayer.Hoehenlinien, (x, _) => x == 0 ? 510f : 500f, zoom: 15);

        Assert.Empty(tile.Contours);
    }

    [Fact]
    public void A_hole_in_the_heights_breaks_the_lines_around_it_instead_of_bending_them()
    {
        var tile = Render(TerrainLayer.Hoehenlinien, (x, y) => x == 100 && y == 100 ? float.NaN : 500 + (0.5f * x) + (0.5f * y), zoom: 15);

        // The sample (100, 100) is the pixel (98, 98), centre 98.5: no line touches the cells around it.
        Assert.DoesNotContain(tile.Contours.SelectMany(c => c.Segments), s =>
            Math.Abs(((s.X1 + s.X2) / 2) - 98.5) < 1 && Math.Abs(((s.Y1 + s.Y2) / 2) - 98.5) < 1);
    }

    [Fact]
    public void A_hole_is_never_shaded_and_neither_is_ground_beside_it()
    {
        // The sample (100, 100) is the tile's pixel (98, 98); its neighbours' slopes need it too.
        var tile = Render(TerrainLayer.Schummerung, (x, y) => x == 100 && y == 100 ? float.NaN : 600 - (0.3f * x), zoom: 15);

        Assert.Equal(0, Alpha(Pixel(tile, 98, 98)));
        Assert.Equal(0, Alpha(Pixel(tile, 97, 98)));
        Assert.Equal(0, Alpha(Pixel(tile, 98, 97)));
        Assert.NotEqual(0, Alpha(Pixel(tile, 90, 90)));
    }

    [Fact]
    public void Smoothing_averages_each_height_with_its_eight_neighbours()
    {
        var heights = new float[Side * Side];
        heights[(10 * Side) + 10] = 9;

        var smooth = TerrainShading.Smooth(heights);

        Assert.Equal(1, smooth[(9 * Side) + 9], precision: 5);
        Assert.Equal(1, smooth[(11 * Side) + 11], precision: 5);
        Assert.Equal(1, smooth[(10 * Side) + 11], precision: 5);
        Assert.Equal(0, smooth[(12 * Side) + 10], precision: 5);
    }

    [Fact]
    public void Smoothing_repeats_the_edge_past_the_grid()
    {
        var heights = new float[Side * Side];
        heights[0] = 9;

        // At the corner, four of the nine looked at are the corner itself.
        Assert.Equal(4, TerrainShading.Smooth(heights)[0], precision: 5);
    }

    [Fact]
    public void Smoothing_leaves_a_hole_a_hole_and_averages_around_it()
    {
        var heights = new float[Side * Side];
        Array.Fill(heights, 8);
        heights[(10 * Side) + 10] = float.NaN;
        heights[(10 * Side) + 12] = 16;

        var smooth = TerrainShading.Smooth(heights);

        Assert.True(float.IsNaN(smooth[(10 * Side) + 10]));
        Assert.Equal(9, smooth[(10 * Side) + 11], precision: 5); // (7 × 8 + 16) / 8
    }

    [Fact]
    public void Heights_of_the_wrong_size_are_refused()
    {
        Assert.Throws<ArgumentException>(() => TerrainShading.Render(new float[10], 1, 15, new TerrainStyle(TerrainLayer.Schummerung)));
    }

    private static TerrainTile Render(TerrainLayer layer, Func<int, int, float> height, int zoom = 15) =>
        Render(new TerrainStyle(layer), height, zoom);

    // Heights by sample column and row, the margin included; one metre a pixel unless said otherwise.
    private static TerrainTile Render(TerrainStyle style, Func<int, int, float> height, int zoom = 15, double metresPerPixel = 1)
    {
        var heights = new float[Side * Side];
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                heights[(y * Side) + x] = height(x, y);
            }
        }

        return TerrainShading.Render(heights, metresPerPixel, zoom, style);
    }

    private static int[] Pixels(TerrainTile tile) => tile.Pixels?.ToArray() ?? new int[WebMercator.TileSizePixels * WebMercator.TileSizePixels];

    private static int Pixel(TerrainTile tile, int x, int y) => Pixels(tile)[(y * WebMercator.TileSizePixels) + x];

    private static int Alpha(int pixel) => (pixel >> 24) & 0xFF;

    private static int Red(int pixel) => (pixel >> 16) & 0xFF;

    private static int Green(int pixel) => (pixel >> 8) & 0xFF;

    private static int Blue(int pixel) => pixel & 0xFF;
}
