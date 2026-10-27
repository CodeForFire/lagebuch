using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;

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
