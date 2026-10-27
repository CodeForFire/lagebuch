using LageBuch.Persistence.Map;

namespace LageBuch.Persistence.Tests;

public class CombinedMapTileSourceTests
{
    [Fact]
    public void A_tile_comes_from_the_first_pack_that_has_it()
    {
        var ffb = new Tiles(maxZoom: 17, bounds: (11, 1086, 1090, 709, 712), (14, 1, 1, [0x01]), (14, 2, 2, [0x02]));
        var sta = new Tiles(maxZoom: 17, bounds: (11, 1086, 1090, 711, 715), (14, 2, 2, [0x22]), (14, 3, 3, [0x33]));
        var map = new CombinedMapTileSource([new(ffb, null), new(sta, null)]);

        Assert.Equal(new byte[] { 0x01 }, map.GetTile(14, 1, 1));
        Assert.Equal(new byte[] { 0x02 }, map.GetTile(14, 2, 2));
        Assert.Equal(new byte[] { 0x33 }, map.GetTile(14, 3, 3));
        Assert.Null(map.GetTile(14, 4, 4));
    }

    [Fact]
    public void The_bounds_cover_every_pack_at_the_coarsest_zoom_any_has()
    {
        // At zoom 12 the second pack's tiles 2180..2190 are zoom-11 tiles 1090..1095.
        var map = new CombinedMapTileSource([
            new(new Tiles(maxZoom: 17, bounds: (11, 1086, 1090, 709, 712)), null),
            new(new Tiles(maxZoom: 16, bounds: (12, 2180, 2190, 1430, 1431)), null),
            new(new Tiles(maxZoom: null, bounds: null), null),
        ]);

        Assert.Equal((11, 1086, 1095, 709, 715), map.GetTileBounds());
        Assert.Equal(17, map.GetMaxZoom());
    }

    [Fact]
    public void Packs_without_tiles_make_a_map_without_tiles()
    {
        var map = new CombinedMapTileSource([new(new Tiles(maxZoom: null, bounds: null), null)]);

        Assert.Null(map.GetTileBounds());
        Assert.Null(map.GetMaxZoom());
        Assert.Null(map.GetTile(11, 0, 0));
    }

    private sealed class Tiles(int? maxZoom, (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? bounds, params (int Zoom, int X, int Y, byte[] Data)[] tiles) : IMapTileSource
    {
        public byte[]? GetTile(int zoom, int x, int y) => tiles.FirstOrDefault(t => t.Zoom == zoom && t.X == x && t.Y == y).Data;

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => bounds;

        public int? GetMaxZoom() => maxZoom;
    }
}
