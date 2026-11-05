using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public sealed class TerrainOverlayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-terrain-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_picker_offers_every_layer_and_starts_on_none()
    {
        var terrain = new TerrainOverlay(new MapViewport());

        Assert.Equal(
            ["Aus", "Schummerung", "Höhenlinien", "Höhenschichten", "Hangneigung", "Höhe zum Einsatzort"],
            terrain.Choices.Select(c => c.Label));
        Assert.Equal(TerrainLayer.None, terrain.Layer);
        Assert.True(terrain.Choices[0].IsSelected);
        Assert.Null(terrain.Style);
        Assert.False(terrain.HasElevation);
    }

    [Fact]
    public void A_pack_without_an_elevation_file_leaves_the_picker_hidden_without_an_error()
    {
        var terrain = new TerrainOverlay(new MapViewport());
        var folder = Directory.CreateDirectory(Path.Join(_dir, "old")).FullName;

        terrain.Show([new InstalledRegionPack(FakeRegionPackLibrary.Pack("old", "Alt"), folder)]);

        Assert.False(terrain.HasElevation);
        Assert.Null(terrain.Error);
    }

    [Fact]
    public void An_unreadable_elevation_file_is_named_and_the_other_packs_still_show_terrain()
    {
        var terrain = new TerrainOverlay(new MapViewport());
        var broken = Directory.CreateDirectory(Path.Join(_dir, "sta")).FullName;
        File.WriteAllText(Path.Join(broken, InstalledRegionPack.ElevationFileName), "not a tiff");

        terrain.Show([TerrainFixture.Pack(_dir), new InstalledRegionPack(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"), broken)]);

        Assert.True(terrain.HasElevation);
        Assert.Single(Assert.IsType<LageBuch.Persistence.Map.Elevation.ElevationSet>(terrain.Elevation).Grids);
        Assert.Equal("Höhendaten nicht lesbar (Landkreis Starnberg) – dort kein Gelände.", terrain.Error);
    }

    [Fact]
    public void Picking_a_layer_checks_it_alone_and_hands_the_map_its_style()
    {
        var terrain = Shown();

        terrain.Choices[1].IsSelected = true;

        Assert.Equal(TerrainLayer.Schummerung, terrain.Layer);
        Assert.Equal([false, true, false, false, false, false], terrain.Choices.Select(c => c.IsSelected));
        Assert.Equal(new TerrainStyle(TerrainLayer.Schummerung), terrain.Style);

        terrain.Choices[0].IsSelected = true;

        Assert.Null(terrain.Style);
        Assert.False(terrain.ShowsLegend);
    }

    [Fact]
    public void Each_layer_s_key_explains_it()
    {
        var terrain = Shown();

        terrain.Layer = TerrainLayer.Hoehenlinien;
        Assert.Equal("Höhenlinien alle 5 m, Zähllinie alle 25 m", terrain.LegendText);

        terrain.Layer = TerrainLayer.Hangneigung;
        Assert.True(terrain.ShowsSlopeKey);
        Assert.False(terrain.ShowsRampKey);

        terrain.Layer = TerrainLayer.Hoehenschichten;
        Assert.True(terrain.ShowsRampKey);
        Assert.Matches("^\\d+ m … \\d+ m ü\\. NHN$", terrain.LegendText);
    }

    [Fact]
    public void Heights_relative_to_the_Einsatzort_wait_for_one_to_be_set()
    {
        var terrain = Shown();
        terrain.Layer = TerrainLayer.RelativZumEinsatzort;

        Assert.Null(terrain.Style);
        Assert.Equal("Für Höhen zum Einsatzort erst den Einsatzort setzen.", terrain.LegendText);
        Assert.False(terrain.ShowsRelativeKey);

        terrain.Einsatzort = TerrainFixture.Marktplatz;

        Assert.Equal(550, Assert.NotNull(Assert.IsType<TerrainStyle>(terrain.Style).ReferenceHeight), precision: 1);
        Assert.Equal("als der Einsatzort (550,0 m)", terrain.LegendText);
        Assert.True(terrain.ShowsRelativeKey);
    }

    [Fact]
    public void Too_far_out_the_key_asks_to_zoom_in()
    {
        var map = new MapViewport { Zoom = TerrainSampler.MinZoom - 1, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        terrain.Layer = TerrainLayer.Hangneigung;

        Assert.Equal("Gelände: näher heranzoomen.", terrain.LegendText);
        Assert.False(terrain.ShowsSlopeKey);

        map.ZoomInCommand.Execute(null);

        Assert.Equal("Hangneigung", terrain.LegendText);
        Assert.True(terrain.ShowsSlopeKey);
    }

    [Fact]
    public void The_colour_range_stays_over_a_small_pan_and_follows_a_long_one()
    {
        var map = new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        terrain.Layer = TerrainLayer.Hoehenschichten;
        var start = terrain.Style;

        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 200, 15), 15));
        Assert.Equal(start, terrain.Style);

        // 1 m every 20 m east: a kilometre east is 50 m higher.
        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 700, 15), 15));
        Assert.True(Assert.IsType<TerrainStyle>(terrain.Style).Low > Assert.IsType<TerrainStyle>(start).Low);
    }

    private TerrainOverlay Shown(MapViewport? map = null)
    {
        var terrain = new TerrainOverlay(map ?? new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz });
        terrain.Show([TerrainFixture.Pack(_dir)]);
        return terrain;
    }

    private static GeoPoint Shift(GeoPoint point, double pixelsEast, int zoom)
    {
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return WebMercator.ToGeo(x + pixelsEast, y, zoom);
    }
}
