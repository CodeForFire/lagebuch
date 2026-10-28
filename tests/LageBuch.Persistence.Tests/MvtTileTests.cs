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
    public void Every_value_type_comes_back_as_its_invariant_text()
    {
        byte[] Value(int field, int wire, byte[] payload) => [.. Varint(((ulong)field << 3) | (uint)wire), .. payload];
        byte[][] values =
        [
            Value(1, 2, [.. Varint(5), .. "Musterstraße"u8.ToArray()[..5]]),
            Value(2, 5, BitConverter.GetBytes(1.5f)),
            Value(3, 1, BitConverter.GetBytes(-2.25)),
            Value(4, 0, Varint(unchecked((ulong)-3L))),
            Value(5, 0, Varint(42)),
            Value(6, 0, Varint(5)), // zig-zag 5 is -3
            Value(7, 0, Varint(1)),
            Value(7, 0, Varint(0)),
        ];
        string[] keys = ["s", "f", "d", "i", "u", "z", "t", "n"];
        var tags = new List<byte>();
        for (var i = 0; i < keys.Length; i++)
        {
            tags.AddRange(Varint((ulong)i));
            tags.AddRange(Varint((ulong)i));
        }

        byte[] layer =
        [
            .. Bytes(1, "pois"u8.ToArray()),
            .. Bytes(2, [.. Bytes(2, [.. tags]), .. Varint(3 << 3), 1]),
            .. keys.SelectMany(k => Bytes(3, System.Text.Encoding.UTF8.GetBytes(k))),
            .. values.SelectMany(v => Bytes(4, v)),
        ];

        var properties = Assert.Single(Assert.Single(MvtTile.Decode(Bytes(3, layer))).Features).Properties;

        Assert.Equal("Muste", properties["s"]);
        Assert.Equal("1.5", properties["f"]);
        Assert.Equal("-2.25", properties["d"]);
        Assert.Equal("-3", properties["i"]);
        Assert.Equal("42", properties["u"]);
        Assert.Equal("-3", properties["z"]);
        Assert.Equal("true", properties["t"]);
        Assert.Equal("false", properties["n"]);
    }

    [Fact]
    public void As_many_layers_as_the_cap_allows_are_read_and_one_more_is_refused()
    {
        var layer = Bytes(3, Bytes(1, "x"u8.ToArray()));

        Assert.Equal(MvtTile.MaxLayers, MvtTile.Decode([.. Enumerable.Repeat(layer, MvtTile.MaxLayers).SelectMany(l => l)]).Count);
        var e = Assert.Throws<FormatException>(() => MvtTile.Decode([.. Enumerable.Repeat(layer, MvtTile.MaxLayers + 1).SelectMany(l => l)]));
        Assert.Equal("Too many layers.", e.Message);
    }

    [Fact]
    public void A_field_numbered_like_a_layer_but_not_a_message_is_skipped()
    {
        // Field 3 as a varint, then a real layer.
        byte[] tile = [.. Varint(3 << 3), 7, .. Bytes(3, Bytes(1, "water"u8.ToArray()))];

        Assert.Equal("water", Assert.Single(MvtTile.Decode(tile)).Name);
    }

    [Fact]
    public void An_odd_number_of_tag_indices_is_refused()
    {
        byte[] layer = [.. Bytes(1, "roads"u8.ToArray()), .. Bytes(2, Bytes(2, [0, 0, 1]))];

        var e = Assert.Throws<FormatException>(() => MvtTile.Decode(Bytes(3, layer)));
        Assert.Equal("Odd tag count.", e.Message);
    }

    [Theory]
    [InlineData(0, 1)] // key exists, value index one past the table
    [InlineData(1, 0)] // value exists, key index one past the table
    public void A_tag_whose_key_or_value_index_lies_one_past_its_table_is_ignored(byte key, byte value)
    {
        byte[] layer =
        [
            .. Bytes(1, "places"u8.ToArray()),
            .. Bytes(2, Bytes(2, [key, value])),
            .. Bytes(3, "kind"u8.ToArray()),
            .. Bytes(4, Bytes(1, "locality"u8.ToArray())),
        ];

        Assert.Empty(Assert.Single(Assert.Single(MvtTile.Decode(Bytes(3, layer))).Features).Properties);
    }

    [Fact]
    public void A_geometry_type_the_spec_does_not_know_reads_as_unknown()
    {
        byte[] layer = [.. Bytes(1, "x"u8.ToArray()), .. Bytes(2, [.. Varint(3 << 3), 4])];

        Assert.Equal(VectorGeometryType.Unknown, Assert.Single(Assert.Single(MvtTile.Decode(Bytes(3, layer))).Features).Type);
    }

    [Fact]
    public void Each_MoveTo_starts_a_part_and_a_LineTo_without_one_starts_its_own()
    {
        // LineTo (1, 1); MoveTo (+2, +2); LineTo (+1, 0) twice.
        byte[] geometry = [.. Varint((1 << 3) | 2), 2, 2, .. Varint((1 << 3) | 1), 4, 4, .. Varint((2 << 3) | 2), 2, 0, 2, 0];
        byte[] layer = [.. Bytes(1, "roads"u8.ToArray()), .. Bytes(2, [.. Varint(3 << 3), 2, .. Bytes(4, geometry)])];

        var parts = Assert.Single(Assert.Single(MvtTile.Decode(Bytes(3, layer))).Features).Parts;

        Assert.Equal(2, parts.Count);
        Assert.Equal([(1, 1)], parts[0]);
        Assert.Equal([(3, 3), (4, 3), (5, 3)], parts[1]);
    }

    [Fact]
    public void An_unknown_geometry_command_is_refused()
    {
        byte[] layer = [.. Bytes(1, "x"u8.ToArray()), .. Bytes(2, Bytes(4, [.. Varint((1 << 3) | 3)]))];

        var e = Assert.Throws<FormatException>(() => MvtTile.Decode(Bytes(3, layer)));
        Assert.Equal("Unknown geometry command.", e.Message);
    }

    [Fact]
    public void A_length_one_past_what_remains_is_refused_and_one_that_fits_exactly_is_read()
    {
        // A layer whose only field is its name; the declared length is exact, then one too many.
        var name = Bytes(1, "water"u8.ToArray());
        Assert.Equal("water", Assert.Single(MvtTile.Decode([.. Varint((3 << 3) | 2), .. Varint((ulong)name.Length), .. name])).Name);

        var e = Assert.Throws<FormatException>(() => MvtTile.Decode([.. Varint((3 << 3) | 2), .. Varint((ulong)name.Length + 1), .. name]));
        Assert.Equal("Length beyond the message.", e.Message);
    }

    [Fact]
    public void A_fixed_width_value_cut_short_is_refused()
    {
        // A float value (wire type 5) with two of its four bytes.
        byte[] layer = [.. Bytes(1, "x"u8.ToArray()), .. Bytes(4, [.. Varint((2 << 3) | 5), 0, 0])];

        var e = Assert.Throws<FormatException>(() => MvtTile.Decode(Bytes(3, layer)));
        Assert.Equal("Truncated fixed value.", e.Message);
    }

    [Fact]
    public void A_varint_cut_short_or_longer_than_ten_bytes_is_refused()
    {
        Assert.Equal("Truncated varint.", Assert.Throws<FormatException>(() => MvtTile.Decode([0x80])).Message);
        Assert.Equal("Varint too long.", Assert.Throws<FormatException>(() => MvtTile.Decode([.. Enumerable.Repeat((byte)0xFF, 11), 0])).Message);
    }

    [Fact]
    public void An_unsupported_wire_type_is_refused()
    {
        // Field 9, wire type 3 (a deprecated group start).
        var e = Assert.Throws<FormatException>(() => MvtTile.Decode([.. Varint((9 << 3) | 3)]));
        Assert.Equal("Unsupported wire type.", e.Message);
    }

    [Fact]
    public void Unknown_fields_of_every_wire_type_are_skipped()
    {
        byte[] unknown = [.. Varint((9 << 3) | 0), 1, .. Varint((10 << 3) | 1), 0, 0, 0, 0, 0, 0, 0, 0, .. Varint((11 << 3) | 5), 0, 0, 0, 0, .. Bytes(12, [1, 2])];

        Assert.Equal("water", Assert.Single(MvtTile.Decode([.. unknown, .. Bytes(3, [.. unknown, .. Bytes(1, "water"u8.ToArray())])])).Name);
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
