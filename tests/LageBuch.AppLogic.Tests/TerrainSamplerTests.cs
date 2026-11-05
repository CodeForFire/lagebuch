using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;
using LageBuch.Persistence.Tests;

namespace LageBuch.AppLogic.Tests;

public sealed class TerrainSamplerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-terrain-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Each_sample_is_the_height_under_its_pixel_centre_the_margin_included()
    {
        var elevation = TerrainFixture.Elevation(_dir);
        const int zoom = 15;
        var (wx, wy) = WebMercator.ToWorldPixel(TerrainFixture.Marktplatz, zoom);
        var (tx, ty) = WebMercator.ToTileIndex(wx, wy);

        var heights = Assert.IsType<float[]>(TerrainSampler.Sample(elevation, zoom, tx, ty));

        Assert.Equal(TerrainSampler.Side * TerrainSampler.Side, heights.Length);
        foreach (var (px, py) in new[] { (0, 0), (2, 2), (130, 77), (259, 259), (17, 250) })
        {
            var at = WebMercator.ToGeo((tx * 256.0) - TerrainSampler.Border + px + 0.5, (ty * 256.0) - TerrainSampler.Border + py + 0.5, zoom);
            Assert.Equal(Assert.NotNull(elevation.HeightAt(at)), heights[(py * TerrainSampler.Side) + px], precision: 2);
        }
    }

    [Fact]
    public void A_tile_no_pack_reaches_is_not_sampled_at_all()
    {
        var elevation = TerrainFixture.Elevation(_dir);
        var (wx, wy) = WebMercator.ToWorldPixel(new GeoPoint(52.52, 13.40), 15);
        var (tx, ty) = WebMercator.ToTileIndex(wx, wy);

        Assert.Null(TerrainSampler.Sample(elevation, 15, tx, ty));
    }

    [Fact]
    public void A_pixel_is_about_3_m_at_zoom_15_in_Bavaria()
    {
        Assert.Equal(3.19, TerrainSampler.MetresPerPixel(15, 48.18), precision: 2);
        Assert.Equal(6.37, TerrainSampler.MetresPerPixel(14, 48.18), precision: 2);
    }

    [Fact]
    public void The_colour_range_is_rounded_out_to_10_m()
    {
        var elevation = TerrainFixture.Elevation(_dir);

        // ±1.5 km east at 1 m in 20, ±1.1 km north at 1 m in 40: about 448 m to 652 m.
        var (low, high) = Assert.NotNull(TerrainSampler.Range(elevation, new GeoPoint(48.19, 11.235), new GeoPoint(48.17, 11.276)));

        Assert.Equal(0, Math.IEEERemainder(low, 10), precision: 9);
        Assert.Equal(0, Math.IEEERemainder(high, 10), precision: 9);
        Assert.InRange(low, 430, 460);
        Assert.InRange(high, 630, 660);
    }

    [Fact]
    public void A_tile_inside_a_grid_but_all_NoData_is_not_sampled()
    {
        var folder = Directory.CreateDirectory(Path.Join(_dir, "hole")).FullName;
        var path = GeoTiffFixtures.Write(Path.Join(folder, "elevation.tif"), TerrainFixture.Grid, (_, _) => GeoTiffFixtures.NoData, tileSide: 64);
        var elevation = new ElevationSet([new GeoTiffElevation(path)]);
        var (wx, wy) = WebMercator.ToWorldPixel(TerrainFixture.Marktplatz, 15);
        var (tx, ty) = WebMercator.ToTileIndex(wx, wy);

        Assert.Null(TerrainSampler.Sample(elevation, 15, tx, ty));
    }

    [Fact]
    public void A_tile_row_s_latitude_is_that_of_its_middle()
    {
        var (_, y) = WebMercator.ToWorldPixel(TerrainFixture.Marktplatz, 15);
        var row = (int)Math.Floor(y / 256);

        Assert.Equal(WebMercator.ToGeo(0, (row + 0.5) * 256, 15).Latitude, TerrainSampler.LatitudeOfRow(15, row), precision: 9);
        Assert.True(TerrainSampler.LatitudeOfRow(15, row) > TerrainSampler.LatitudeOfRow(15, row + 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_range_samples_the_rectangle_itself_not_a_row_or_column_beyond(bool northToSouth)
    {
        // 500 m at the rectangle's one edge to 600 m at the other: its 16 sample rows (or columns) lie
        // at 503.1 m to 596.9 m, which round out to exactly 500 m and 600 m; a sample beyond the
        // rectangle would round out a step further.
        var (e0, n0) = TerrainFixture.MarktplatzUtm;
        const double side = 1000;
        var folder = Directory.CreateDirectory(Path.Join(_dir, "ramp")).FullName;
        var grid = new GeoTiffFixtures.Grid(e0 - 1500, n0 + 1500, 5, 600, 600);
        var path = GeoTiffFixtures.Write(
            Path.Join(folder, "elevation.tif"),
            grid,
            (e, n) => (float)(northToSouth ? 500 + ((n0 - n) / side * 100) : 500 + ((e - e0) / side * 100)),
            tileSide: 128);
        var elevation = new ElevationSet([new GeoTiffElevation(path)]);
        var northWest = Geo(e0, n0);
        var southEast = Geo(e0 + side, n0 - side);

        var (low, high) = Assert.NotNull(TerrainSampler.Range(elevation, northWest, southEast));

        Assert.Equal(500, low, precision: 6);
        Assert.Equal(600, high, precision: 6);
    }

    [Fact]
    public void Flat_ground_still_gets_a_range_of_10_m()
    {
        var folder = Directory.CreateDirectory(Path.Join(_dir, "flat")).FullName;
        var path = GeoTiffFixtures.Write(Path.Join(folder, "elevation.tif"), TerrainFixture.Grid, (_, _) => 523f, tileSide: 64);
        var elevation = new ElevationSet([new GeoTiffElevation(path)]);

        var (low, high) = Assert.NotNull(TerrainSampler.Range(elevation, new GeoPoint(48.19, 11.24), new GeoPoint(48.17, 11.27)));

        Assert.Equal(520, low, precision: 6);
        Assert.Equal(530, high, precision: 6);
    }

    // The position at an EPSG:25832 easting and northing, found by a few steps of Newton's method
    // on the forward projection, which is all the code has.
    private static GeoPoint Geo(double east, double north)
    {
        var guess = TerrainFixture.Marktplatz;
        for (var i = 0; i < 6; i++)
        {
            var (e, n) = Utm32.FromGeo(guess);
            var (e2, _) = Utm32.FromGeo(new GeoPoint(guess.Latitude, guess.Longitude + 1e-5));
            var (_, n2) = Utm32.FromGeo(new GeoPoint(guess.Latitude + 1e-5, guess.Longitude));
            guess = new GeoPoint(guess.Latitude + ((north - n) / ((n2 - n) / 1e-5)), guess.Longitude + ((east - e) / ((e2 - e) / 1e-5)));
        }

        return guess;
    }

    [Fact]
    public void Where_no_pack_has_heights_there_is_no_range()
    {
        var elevation = TerrainFixture.Elevation(_dir);

        Assert.Null(TerrainSampler.Range(elevation, new GeoPoint(52.53, 13.39), new GeoPoint(52.51, 13.41)));
    }
}
