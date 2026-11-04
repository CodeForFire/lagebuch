using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;
using LageBuch.Persistence.Map.Vector;
using LageBuch.Persistence.Tests;

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
    [InlineData(30, 30)]
    [InlineData(-15, 345)]
    [InlineData(375, 15)]
    [InlineData(-720, 0)]
    public void A_turn_from_the_map_is_kept_as_a_turn_from_0_up_to_360(double turned, double kept)
    {
        var viewport = new MapViewport();

        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, turned));

        Assert.Equal(kept, viewport.Rotation, precision: 9);
    }

    [Fact]
    public void Moving_the_view_on_purpose_keeps_the_map_turned()
    {
        var viewport = new MapViewport();
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, 90));

        viewport.ChangeViewCommand.Execute(new MapViewChange(new GeoPoint(48.1, 11.2), 17));

        Assert.Equal(90, viewport.Rotation, precision: 9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_turn_that_is_no_number_leaves_the_map_as_it_was(double turned)
    {
        var viewport = new MapViewport();
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, 40));

        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, turned));

        Assert.Equal(40, viewport.Rotation, precision: 9);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.4, false)]
    [InlineData(359.6, false)]
    [InlineData(0.5, true)]
    [InlineData(359.5, true)]
    [InlineData(180, true)]
    public void The_north_button_is_offered_only_on_a_map_turned_by_more_than_an_eye_can_tell(double turned, bool offered)
    {
        var viewport = new MapViewport();

        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, turned));

        Assert.Equal(offered, viewport.IsRotated);
        Assert.Equal(offered, viewport.FaceNorthCommand.CanExecute(null));
    }

    [Fact]
    public void The_north_button_turns_the_map_back_and_then_is_no_longer_offered()
    {
        var viewport = new MapViewport();
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, 135));
        var changes = new List<string?>();
        viewport.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewport.FaceNorthCommand.Execute(null);

        Assert.Equal(0, viewport.Rotation, precision: 9);
        Assert.False(viewport.IsRotated);
        Assert.Contains(nameof(MapViewport.IsRotated), changes);
        Assert.False(viewport.FaceNorthCommand.CanExecute(null));
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
            // The fixture archive covers Fürstenfeldbruck, W 10.95 to E 11.45 and S 48.06 to N 48.32.
            File.WriteAllBytes(Path.Join(folder, InstalledRegionPack.VectorTilesFileName), VectorTileFixtures.Archive([(11, 1086, 709, [0x1A, 0x00])]));
            var viewport = new MapViewport();
            viewport.Show(new InstalledRegionPack(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"), folder));

            var (west, north) = WebMercator.ToWorldPixel(new GeoPoint(48.32, 10.95), 11);
            var (east, south) = WebMercator.ToWorldPixel(new GeoPoint(48.06, 11.45), 11);
            var (westX, northY) = WebMercator.ToTileIndex(west, north);
            var (eastX, southY) = WebMercator.ToTileIndex(east, south);
            var expected = WebMercator.CenterOfTiles(11, westX, eastX, northY, southY);
            Assert.Equal(12, viewport.Zoom);
            Assert.Equal(expected.Latitude, viewport.Center.Latitude, precision: 6);
            Assert.Equal(expected.Longitude, viewport.Center.Longitude, precision: 6);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Several_packs_become_one_layer_each_drawn_from_its_vector_tiles_in_their_order()
    {
        var viewport = new MapViewport();

        viewport.ShowAll(
            [
                new InstalledRegionPack(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"), Path.Join("packs", "sta")),
                new InstalledRegionPack(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"), Path.Join("packs", "ffb")),
            ],
            centerOnPacks: false);

        var layers = Assert.IsType<CombinedMapTileSource>(viewport.TileSource).Layers;
        Assert.Equal(2, layers.Count);
        Assert.All(layers, l => Assert.IsType<PmTilesFileSource>(l));
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
