using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.AppLogic.Tests;

public class MapViewportTests
{
    [Fact]
    public void A_pan_or_zoom_from_the_map_becomes_the_view()
    {
        var viewport = new MapViewport();

        viewport.ChangeViewCommand.Execute(new MapViewChange(new GeoPoint(48.1, 11.2), 15));

        Assert.Equal(new GeoPoint(48.1, 11.2), viewport.Center);
        Assert.Equal(15, viewport.Zoom);
    }

    [Theory]
    [InlineData(2, MapViewport.MinZoom)]
    [InlineData(25, MapViewport.MaxZoom)]
    public void A_zoom_past_what_the_map_can_show_stops_at_its_limit(int requested, int expected)
    {
        var viewport = new MapViewport();

        viewport.ChangeViewCommand.Execute(new MapViewChange(new GeoPoint(48, 11), requested));

        Assert.Equal(expected, viewport.Zoom);
    }

    [Fact]
    public void The_zoom_buttons_stop_at_the_limits()
    {
        var viewport = new MapViewport();
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, MapViewport.MaxZoom));

        Assert.False(viewport.ZoomInCommand.CanExecute(null));
        viewport.ZoomOutCommand.Execute(null);
        Assert.Equal(MapViewport.MaxZoom - 1, viewport.Zoom);
        Assert.True(viewport.ZoomInCommand.CanExecute(null));
    }

    [Fact]
    public void The_zoom_out_button_stops_at_the_coarsest_zoom()
    {
        var viewport = new MapViewport();

        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, MapViewport.MinZoom + 1));
        Assert.True(viewport.ZoomOutCommand.CanExecute(null));

        viewport.ZoomOutCommand.Execute(null);

        Assert.False(viewport.ZoomOutCommand.CanExecute(null));
    }

    [Fact]
    public void No_view_change_leaves_the_view_as_it_was()
    {
        var viewport = new MapViewport();
        viewport.ChangeViewCommand.Execute(new MapViewChange(new GeoPoint(48.1, 11.2), 15));

        viewport.ChangeViewCommand.Execute(null);

        Assert.Equal(new GeoPoint(48.1, 11.2), viewport.Center);
        Assert.Equal(15, viewport.Zoom);
    }

    [Fact]
    public void A_pack_is_shown_centred_one_zoom_finer_than_its_coarsest_tiles()
    {
        var folder = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;
        try
        {
            // Far from where an empty viewport starts. MBTiles stores XYZ row 300 at zoom 10 as 1023 - 300.
            using (var cn = SqliteConnectionFactory.OpenReadWrite(Path.Join(folder, InstalledRegionPack.TilesFileName)))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);" +
                    "INSERT INTO tiles VALUES (10, 200, 723, x'00');";
                cmd.ExecuteNonQuery();
            }

            var viewport = new MapViewport();
            viewport.Show(new InstalledRegionPack(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"), folder));

            var expected = WebMercator.CenterOfTiles(10, 200, 200, 300, 300);
            Assert.Equal(11, viewport.Zoom);
            Assert.Equal(expected.Latitude, viewport.Center.Latitude, precision: 6);
            Assert.Equal(expected.Longitude, viewport.Center.Longitude, precision: 6);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Several_packs_become_layers_each_with_its_area_and_those_without_one_underneath()
    {
        var root = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;
        try
        {
            var ffb = Directory.CreateDirectory(Path.Join(root, "ffb")).FullName;
            var sta = Directory.CreateDirectory(Path.Join(root, "sta")).FullName;
            File.WriteAllText(
                Path.Join(ffb, InstalledRegionPack.BoundaryFileName),
                """{"type": "Feature", "geometry": {"type": "Polygon", "coordinates": [[[11.0, 48.0], [11.4, 48.0], [11.4, 48.3], [11.0, 48.0]]]}}""");
            var viewport = new MapViewport();

            viewport.ShowAll(
                [
                    new InstalledRegionPack(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"), ffb),
                    new InstalledRegionPack(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"), sta),
                ],
                centerOnPacks: false);

            var layers = Assert.IsType<CombinedMapTileSource>(viewport.TileSource).Layers;
            Assert.Null(layers[0].Boundary);
            Assert.Equal(4, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<GeoPoint>>>(layers[1].Boundary)).Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Showing_no_pack_takes_the_map_and_its_attribution_away()
    {
        var viewport = new MapViewport();
        viewport.Show(new InstalledRegionPack(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"), "missing"));
        Assert.NotNull(viewport.TileSource);

        viewport.Show(null);

        Assert.Null(viewport.TileSource);
        Assert.Null(viewport.Attribution);
    }
}
