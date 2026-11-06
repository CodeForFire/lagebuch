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
    [InlineData("river")]
    [InlineData("canal")]
    [InlineData("stream")]
    [InlineData("ditch")]
    [InlineData("drain")]
    public void Each_kind_of_waterway_line_is_a_Gewaesser(string kind)
    {
        var water = Water((VectorGeometryType.LineString, [("kind", kind)], [[(2000, 0), (2000, 4096)]]));

        Assert.Equal(string.Empty, water.At(At(2010, 2000)));
    }

    [Theory]
    [InlineData(2011, 2011, true)] // about 3 m off the middle of a diagonal stream
    [InlineData(2071, 2071, false)] // about 20 m off it
    [InlineData(3071, 929, false)] // 20 m beyond its end, on the line it would make if it went on
    [InlineData(929, 3071, false)] // and 20 m before its start
    public void The_distance_to_a_slanting_stream_is_measured_across_it_and_ends_where_it_ends(int x, int y, bool reached)
    {
        var water = Water((VectorGeometryType.LineString, [("kind", "stream")], [[(1000, 3000), (3000, 1000)]]));

        Assert.Equal(reached, water.At(At(x, y)) is not null);
    }

    [Fact]
    public void A_stream_in_several_parts_is_reached_at_its_nearest_part()
    {
        var water = Water((VectorGeometryType.LineString, [("kind", "stream")], [[(100, 0), (100, 4096)], [(2000, 0), (2000, 4096)]]));

        Assert.NotNull(water.At(At(2010, 2000)));
    }

    [Theory]
    [InlineData(2000, 2000)]
    [InlineData(2000, 2010)]
    public void A_waterway_of_a_single_point_or_with_a_point_twice_is_still_reached(int lastX, int lastY)
    {
        var water = Water((VectorGeometryType.LineString, [("kind", "ditch")], [[(2000, 2000), (lastX, lastY)]]));
        var single = Water((VectorGeometryType.LineString, [("kind", "ditch")], [[(2000, 2000)]]));

        Assert.NotNull(water.At(At(2010, 2000)));
        Assert.NotNull(single.At(At(2010, 2000)));
        Assert.Null(single.At(At(2100, 2000)));
    }

    [Fact]
    public void A_named_river_names_the_water_before_a_nearer_unnamed_one_and_the_nearer_of_two_named_wins()
    {
        var water = Water(
            (VectorGeometryType.Polygon, [("kind", "water")], [Square]),
            (VectorGeometryType.LineString, [("kind", "river")], [[(2010, 0), (2010, 4096)]]),
            (VectorGeometryType.LineString, [("kind", "river"), ("name", "Maisach")], [[(2300, 0), (2300, 4096)]]),
            (VectorGeometryType.LineString, [("kind", "river"), ("name", "Amper")], [[(2100, 0), (2100, 4096)]]));

        Assert.Equal("Amper", water.At(At(2000, 2000)));
    }

    [Theory]
    [InlineData(1980, 1980, true)]
    [InlineData(2020, 2020, false)]
    public void A_shore_that_slants_is_followed_on_both_sides(int x, int y, bool inside)
    {
        (int X, int Y)[] triangle = [(1000, 1000), (3000, 1000), (1000, 3000)];
        var water = Water((VectorGeometryType.Polygon, [("kind", "water")], [triangle]));

        Assert.Equal(inside, water.At(At(x, y)) is not null);
    }

    [Theory]
    [InlineData(1500, true)]
    [InlineData(500, false)]
    [InlineData(3500, false)]
    public void A_point_level_with_the_corners_of_a_diamond_lake_is_inside_only_between_them(int x, bool inside)
    {
        (int X, int Y)[] diamond = [(2000, 1000), (3000, 2000), (2000, 3000), (1000, 2000)];
        var water = Water((VectorGeometryType.Polygon, [("kind", "water")], [diamond]));

        Assert.Equal(inside, water.At(At(x, 2000)) is not null);
    }

    [Fact]
    public void Just_outside_a_river_area_s_shore_is_land()
    {
        var water = Water((VectorGeometryType.Polygon, [("kind", "river")], [Square]));

        Assert.Null(water.At(At(990, 2000)));
        Assert.NotNull(water.At(At(1010, 2000)));
    }

    [Fact]
    public void A_waterway_without_geometry_is_passed_over()
    {
        var water = Water(
            (VectorGeometryType.LineString, [("kind", "stream")], []),
            (VectorGeometryType.LineString, [("kind", "stream"), ("name", "Ascherbach")], [[(2000, 0), (2000, 4096)]]));

        Assert.Equal("Ascherbach", water.At(At(2010, 2000)));
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
        // A layer field that claims 1 000 bytes and carries three: MvtTile refuses it.
        var tiles = new Tiles { [(TileX, TileY)] = [.. Varint((3 << 3) | 2), .. Varint(1_000), 1, 2, 3] };

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
