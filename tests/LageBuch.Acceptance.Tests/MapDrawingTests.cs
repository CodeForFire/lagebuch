using Avalonia;
using LageBuch.App.Shared.Controls;

namespace LageBuch.Acceptance.Tests;

// #150 follow-up: overzoom -- when a requested zoom is past the configured region's actual max
// rendered detail, MapDrawing falls back to the nearest ancestor tile it does have instead of
// drawing nothing, matching every other map app's "blurry but oriented" behavior past native zoom.
public class MapDrawingTests
{
    [Fact]
    public void ComputeOverzoomTile_returns_null_when_zoom_is_within_the_sources_range()
    {
        Assert.Null(MapDrawing.ComputeOverzoomTile(zoom: 14, x: 100, y: 200, sourceMaxZoom: 15));
        Assert.Null(MapDrawing.ComputeOverzoomTile(zoom: 15, x: 100, y: 200, sourceMaxZoom: 15));
    }

    [Fact]
    public void ComputeOverzoomTile_returns_null_when_the_source_has_no_known_max_zoom()
    {
        Assert.Null(MapDrawing.ComputeOverzoomTile(zoom: 20, x: 100, y: 200, sourceMaxZoom: null));
    }

    [Fact]
    public void ComputeOverzoomTile_finds_the_direct_parent_one_level_up()
    {
        // Tile (x=100,y=200) at z16 is the top-left quadrant of its z15 parent (x=50,y=100).
        var overzoom = MapDrawing.ComputeOverzoomTile(zoom: 16, x: 100, y: 200, sourceMaxZoom: 15);

        Assert.NotNull(overzoom);
        Assert.Equal(15, overzoom!.Value.Zoom);
        Assert.Equal(50, overzoom.Value.X);
        Assert.Equal(100, overzoom.Value.Y);
        Assert.Equal(new Rect(0, 0, 128, 128), overzoom.Value.SourceRect);
    }

    [Fact]
    public void ComputeOverzoomTile_picks_the_correct_quadrant_for_an_odd_tile_index()
    {
        // Tile (x=101,y=201) at z16 is the bottom-right quadrant of the same z15 parent (x=50,y=100).
        var overzoom = MapDrawing.ComputeOverzoomTile(zoom: 16, x: 101, y: 201, sourceMaxZoom: 15);

        Assert.NotNull(overzoom);
        Assert.Equal(15, overzoom!.Value.Zoom);
        Assert.Equal(50, overzoom.Value.X);
        Assert.Equal(100, overzoom.Value.Y);
        Assert.Equal(new Rect(128, 128, 128, 128), overzoom.Value.SourceRect);
    }

    [Fact]
    public void ComputeOverzoomTile_climbs_multiple_levels_when_zoomed_in_further()
    {
        // Two levels up (z17 -> z15): a quarter-size crop of the ancestor tile.
        var overzoom = MapDrawing.ComputeOverzoomTile(zoom: 17, x: 400, y: 800, sourceMaxZoom: 15);

        Assert.NotNull(overzoom);
        Assert.Equal(15, overzoom!.Value.Zoom);
        Assert.Equal(100, overzoom.Value.X);
        Assert.Equal(200, overzoom.Value.Y);
        Assert.Equal(new Rect(0, 0, 64, 64), overzoom.Value.SourceRect);
    }
}
