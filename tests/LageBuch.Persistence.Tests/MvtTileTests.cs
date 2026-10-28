using LageBuch.Persistence.Map.Vector;
using static LageBuch.Persistence.Tests.VectorTileFixtures;

namespace LageBuch.Persistence.Tests;

public sealed class MvtTileTests
{
    [Fact]
    public void A_polygon_comes_back_with_its_ring_and_its_tags()
    {
        (int X, int Y)[] ring = [(10, 10), (4000, 10), (4000, 4000), (10, 4000)];
        var tile = Tile("water", (VectorGeometryType.Polygon, [("kind", "lake"), ("name", "Ampersee")], [ring]));

        var layer = Assert.Single(MvtTile.Decode(tile));
        Assert.Equal("water", layer.Name);
        Assert.Equal(4096, layer.Extent);
        var feature = Assert.Single(layer.Features);
        Assert.Equal(VectorGeometryType.Polygon, feature.Type);
        Assert.Equal("lake", feature.Get("kind"));
        Assert.Equal("Ampersee", feature.Get("name"));
        Assert.Equal(ring, Assert.Single(feature.Parts));
    }

    [Fact]
    public void Negative_deltas_and_coordinates_in_the_buffer_survive_the_zig_zag()
    {
        (int X, int Y)[] line = [(-64, 4160), (2048, -64), (100, 100)];
        var tile = Tile("roads", (VectorGeometryType.LineString, [("kind_detail", "residential")], [line]));

        var feature = Assert.Single(Assert.Single(MvtTile.Decode(tile)).Features);
        Assert.Equal(line, Assert.Single(feature.Parts));
    }

    [Fact]
    public void A_length_beyond_the_tile_is_refused()
    {
        // A layer field that claims 1 000 bytes and carries three.
        byte[] tile = [.. Varint((3 << 3) | 2), .. Varint(1_000), 1, 2, 3];

        Assert.Throws<FormatException>(() => MvtTile.Decode(tile));
    }

    [Fact]
    public void A_command_count_beyond_the_geometry_is_refused_before_it_allocates()
    {
        // MoveTo with a count of 2^28 and no parameters behind it.
        byte[] geometry = [.. Varint((1UL << 31) | 1)];
        byte[] feature = [.. Varint((3 << 3) | 0), 3, .. Bytes(4, geometry)];
        byte[] layer = [.. Bytes(1, "water"u8.ToArray()), .. Bytes(2, feature)];

        Assert.Throws<FormatException>(() => MvtTile.Decode(Bytes(3, layer)));
    }

    [Fact]
    public void A_tag_index_past_the_key_table_is_ignored_rather_than_read()
    {
        byte[] feature = [.. Bytes(2, [7, 9]), .. Varint((3 << 3) | 0), 1];
        byte[] layer = [.. Bytes(1, "places"u8.ToArray()), .. Bytes(2, feature)];

        var decoded = Assert.Single(Assert.Single(MvtTile.Decode(Bytes(3, layer))).Features);
        Assert.Empty(decoded.Properties);
    }
}
