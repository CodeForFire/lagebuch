using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

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

        // 1 m every 20 m east: about 475 m to 625 m across ±1.5 km of the Marktplatz.
        var (low, high) = Assert.NotNull(TerrainSampler.Range(elevation, new GeoPoint(48.19, 11.235), new GeoPoint(48.17, 11.276)));

        Assert.Equal(0, Math.IEEERemainder(low, 10), precision: 9);
        Assert.Equal(0, Math.IEEERemainder(high, 10), precision: 9);
        Assert.InRange(low, 460, 480);
        Assert.InRange(high, 620, 640);
    }

    [Fact]
    public void Where_no_pack_has_heights_there_is_no_range()
    {
        var elevation = TerrainFixture.Elevation(_dir);

        Assert.Null(TerrainSampler.Range(elevation, new GeoPoint(52.53, 13.39), new GeoPoint(52.51, 13.41)));
    }
}
