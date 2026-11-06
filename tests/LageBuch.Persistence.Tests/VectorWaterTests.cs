using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;
using LageBuch.Persistence.Map.Vector;
using static LageBuch.Persistence.Tests.VectorTileFixtures;

namespace LageBuch.Persistence.Tests;

public sealed class VectorWaterTests
{
    // A zoom 15 tile over Fürstenfeldbruck: one extent unit (of 4096) is about 0.2 m on the ground.
    private const int TileX = 17_406;
    private const int TileY = 11_283;

    private static readonly (int X, int Y)[] Square = [(1000, 1000), (3000, 1000), (3000, 3000), (1000, 3000)];

    [Fact]
    public void Inside_an_unnamed_river_area_the_water_is_named_after_the_river_line_through_it()
    {
        var water = Water(
            (VectorGeometryType.Polygon, [("kind", "water"), ("kind_detail", "river")], [Square]),
            (VectorGeometryType.LineString, [("kind", "river"), ("name", "Amper")], [[(2250, 0), (2250, 4096)]]));

        Assert.Equal("Amper", water.At(At(2000, 2000)));
    }

    [Fact]
    public void A_named_lake_gives_its_own_name()
    {
        var water = Water((VectorGeometryType.Polygon, [("kind", "water"), ("name", "Pucher Meer")], [Square]));

        Assert.Equal("Pucher Meer", water.At(At(2000, 2000)));
    }

    [Fact]
    public void Water_with_no_name_and_no_named_river_near_is_water_without_a_name()
    {
        var water = Water(
            (VectorGeometryType.Polygon, [("kind", "water")], [Square]),
            (VectorGeometryType.LineString, [("kind", "river"), ("name", "Amper")], [[(4000, 0), (4000, 4096)]]));

        // The river line lies nearly 600 m away, too far to name this pond after it.
        Assert.Equal(string.Empty, water.At(At(1100, 2000)));
    }

    [Fact]
    public void An_island_in_the_river_is_land()
    {
        (int X, int Y)[] island = [(1800, 1800), (2200, 1800), (2200, 2200), (1800, 2200)];
        var water = Water((VectorGeometryType.Polygon, [("kind", "water")], [Square, island]));

        Assert.Null(water.At(At(2000, 2000)));
        Assert.NotNull(water.At(At(1200, 2000)));
    }

    [Fact]
    public void Beside_the_water_area_is_land()
    {
        var water = Water((VectorGeometryType.Polygon, [("kind", "water")], [Square]));

        Assert.Null(water.At(At(500, 2000)));
    }

    [Theory]
    [InlineData(15, "Ascherbach")]
    [InlineData(100, null)]
    public void A_stream_mapped_only_as_a_line_is_reached_within_a_DGM_cell_of_it(int unitsAway, string? expected)
    {
        var water = Water((VectorGeometryType.LineString, [("kind", "stream"), ("name", "Ascherbach")], [[(2000, 0), (2000, 4096)]]));

        Assert.Equal(expected, water.At(At(2000 + unitsAway, 2000)));
    }

    [Theory]
    [InlineData("fountain")]
    [InlineData("swimming_pool")]
    public void A_fountain_or_a_pool_is_not_a_Gewaesser(string kind)
    {
        var water = Water(
            (VectorGeometryType.Polygon, [("kind", kind)], [Square]),
            (VectorGeometryType.Polygon, [("kind", "water"), ("kind_detail", kind)], [Square]));

        Assert.Null(water.At(At(2000, 2000)));
    }

    [Fact]
    public void A_road_or_a_path_is_not_a_waterway()
    {
        var water = Water((VectorGeometryType.LineString, [("kind", "path")], [[(2000, 0), (2000, 4096)]]));

        Assert.Null(water.At(At(2000, 2000)));
    }

    [Fact]
    public void Only_the_water_layer_counts()
    {
        var tiles = new Tiles { [(TileX, TileY)] = Tile("landuse", (VectorGeometryType.Polygon, [("kind", "water")], [Square])) };

        Assert.Null(new VectorWater(tiles).At(At(2000, 2000)));
    }

    [Fact]
    public void A_damaged_tile_or_no_tile_is_land()
    {
        var tiles = new Tiles { [(TileX, TileY)] = [0xFF, 0xFF, 0xFF] };

        Assert.Null(new VectorWater(tiles).At(At(2000, 2000)));
        Assert.Null(new VectorWater(tiles).At(At(2000, 2000 + 4096)));
    }

    [Fact]
    public void A_tile_is_read_once_for_many_points()
    {
        var tiles = new Tiles { [(TileX, TileY)] = Tile("water", (VectorGeometryType.Polygon, [("kind", "water")], [Square])) };
        var water = new VectorWater(tiles);

        for (var x = 1100; x < 3000; x += 100)
        {
            Assert.NotNull(water.At(At(x, 2000)));
        }

        Assert.Equal(1, tiles.Reads);
    }

    private static VectorWater Water(params (VectorGeometryType Type, (string Key, string Value)[] Tags, (int X, int Y)[][] Parts)[] features) =>
        new(new Tiles { [(TileX, TileY)] = Tile("water", features) });

    // The point at extent units (x, y) of the test tile; y counts down from its north edge.
    private static GeoPoint At(double x, double y)
    {
        const double n = 1 << 15;
        var worldX = TileX + (x / 4096);
        var worldY = TileY + (y / 4096);
        var longitude = (worldX / n * 360) - 180;
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * worldY / n)))) * 180 / Math.PI;
        return new GeoPoint(latitude, longitude);
    }

    private sealed class Tiles : Dictionary<(int X, int Y), byte[]>, IMapTileSource
    {
        public int Reads { get; private set; }

        public byte[]? GetTile(int zoom, int x, int y)
        {
            Reads++;
            return zoom == VectorWater.Zoom && TryGetValue((x, y), out var tile) ? tile : null;
        }

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => VectorWater.Zoom;
    }
}
