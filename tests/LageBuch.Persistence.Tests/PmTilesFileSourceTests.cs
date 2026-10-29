using LageBuch.Persistence.Map.Vector;
using static LageBuch.Persistence.Tests.VectorTileFixtures;

namespace LageBuch.Persistence.Tests;

public sealed class PmTilesFileSourceTests : IDisposable
{
    private readonly string _dir = System.IO.Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    private string ArchivePath => Path.Join(_dir, "map.pmtiles");

    public void Dispose() => System.IO.Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(0, 0, 0, 0UL)]
    [InlineData(1, 0, 0, 1UL)]
    [InlineData(1, 0, 1, 2UL)]
    [InlineData(1, 1, 1, 3UL)]
    [InlineData(1, 1, 0, 4UL)]
    [InlineData(2, 0, 0, 5UL)]

    // From lagebuch-geodata's own reader (scripts/pmtiles_read.py), written separately.
    [InlineData(2, 1, 0, 6UL)]
    [InlineData(2, 0, 2, 9UL)]
    [InlineData(2, 2, 2, 13UL)]
    [InlineData(2, 3, 0, 20UL)]
    [InlineData(3, 0, 5, 40UL)]
    [InlineData(3, 5, 1, 77UL)]
    [InlineData(4, 9, 6, 300UL)]
    [InlineData(5, 24, 29, 1000UL)]
    [InlineData(10, 0, 0, 349525UL)]
    public void The_tile_id_follows_the_spec_hilbert_curve(int zoom, int x, int y, ulong id) =>
        Assert.Equal(id, PmTilesFileSource.TileId(zoom, x, y));

    [Fact]
    public void Every_tile_of_a_full_archive_is_found_under_its_own_address()
    {
        // All 21 tiles of zoom 0 to 2, each holding its own address, so a lookup that lands one
        // entry off returns the wrong bytes rather than none.
        var tiles = Enumerable.Range(0, 3).SelectMany(z => Enumerable.Range(0, 1 << z).SelectMany(x => Enumerable.Range(0, 1 << z).Select(y => (z, x, y, new byte[] { (byte)z, (byte)x, (byte)y })))).ToList();
        File.WriteAllBytes(ArchivePath, Archive(tiles));
        var source = new PmTilesFileSource(ArchivePath);

        foreach (var (z, x, y, content) in tiles)
        {
            Assert.Equal(content, source.GetTile(z, x, y));
        }
    }

    [Theory]
    [InlineData(1, 2, 0)]
    [InlineData(1, 0, 2)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    [InlineData(2, 4, 4)]
    [InlineData(-1, 0, 0)]
    [InlineData(23, 0, 0)]
    public void An_address_off_the_world_is_null_even_where_its_tile_id_would_hit_a_tile(int zoom, int x, int y)
    {
        var tiles = Enumerable.Range(0, 4).SelectMany(z => Enumerable.Range(0, 1 << z).SelectMany(tx => Enumerable.Range(0, 1 << z).Select(ty => (z, tx, ty, new byte[] { 1 })))).ToList();
        File.WriteAllBytes(ArchivePath, Archive(tiles));

        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(zoom, x, y));
    }

    [Fact]
    public void The_deepest_supported_zoom_is_read()
    {
        File.WriteAllBytes(ArchivePath, Archive([(PmTilesFileSource.MaxSupportedZoom, 5, 7, [0xD0])], maxZoom: PmTilesFileSource.MaxSupportedZoom));

        Assert.Equal(new byte[] { 0xD0 }, new PmTilesFileSource(ArchivePath).GetTile(PmTilesFileSource.MaxSupportedZoom, 5, 7));
    }

    [Fact]
    public void A_run_of_tiles_shares_one_blob_and_ends_where_the_run_does()
    {
        // Ids 5 to 7 (zoom 2) share one blob; 8 has none, 9 its own.
        var shared = Gzip([0xEE]);
        var own = Gzip([0xEF]);
        var root = Gzip(Directory(new PmTilesEntry(5, 3, (uint)shared.Length, 0), new PmTilesEntry(9, 1, (uint)own.Length, (ulong)shared.Length)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [], [.. shared, .. own]));
        var source = new PmTilesFileSource(ArchivePath);

        Assert.Equal(new byte[] { 0xEE }, source.GetTile(2, 0, 0)); // id 5
        Assert.Equal(new byte[] { 0xEE }, source.GetTile(2, 1, 0)); // id 6
        Assert.Equal(new byte[] { 0xEE }, source.GetTile(2, 1, 1)); // id 7, the run's last
        Assert.Null(source.GetTile(2, 0, 1)); // id 8
        Assert.Equal(new byte[] { 0xEF }, source.GetTile(2, 0, 2)); // id 9
        Assert.Null(source.GetTile(1, 0, 0)); // id 1, before the first entry
    }

    [Fact]
    public void A_tile_in_the_second_leaf_directory_is_found_at_that_leafs_offset()
    {
        var first = Gzip([0xC1]);
        var second = Gzip([0xC2]);
        var leafOne = Gzip(Directory(new PmTilesEntry(PmTilesFileSource.TileId(3, 1, 1), 1, (uint)first.Length, 0)));
        var leafTwo = Gzip(Directory(new PmTilesEntry(PmTilesFileSource.TileId(5, 20, 9), 1, (uint)second.Length, (ulong)first.Length)));
        var root = Gzip(Directory(
            new PmTilesEntry(PmTilesFileSource.TileId(3, 1, 1), 0, (uint)leafOne.Length, 0),
            new PmTilesEntry(PmTilesFileSource.TileId(5, 0, 0), 0, (uint)leafTwo.Length, (ulong)leafOne.Length)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [.. leafOne, .. leafTwo], [.. first, .. second]));
        var source = new PmTilesFileSource(ArchivePath);

        Assert.Equal(new byte[] { 0xC2 }, source.GetTile(5, 20, 9));
        Assert.Equal(new byte[] { 0xC1 }, source.GetTile(3, 1, 1));
    }

    [Theory]
    [InlineData(PmTilesFileSource.MaxDirectoryDepth, true)]
    [InlineData(PmTilesFileSource.MaxDirectoryDepth + 1, false)]
    public void Tiles_behind_as_many_directories_as_the_cap_allows_are_found_and_no_deeper(int directories, bool found)
    {
        // A chain: the root points at a leaf, each leaf at the next, and the last holds the tile.
        // The leaves are laid out last first, so each pointer's target is already placed.
        var tile = Gzip([0xAB]);
        var leaves = new List<byte>();
        var target = Gzip(Directory(new PmTilesEntry(PmTilesFileSource.TileId(4, 3, 3), 1, (uint)tile.Length, 0)));
        for (var leaf = 1; leaf < directories - 1; leaf++)
        {
            var placedAt = (ulong)leaves.Count;
            leaves.AddRange(target);
            target = Gzip(Directory(new PmTilesEntry(0, 0, (uint)target.Length, placedAt)));
        }

        var firstLeafAt = (ulong)leaves.Count;
        leaves.AddRange(target);
        var root = Gzip(Directory(new PmTilesEntry(0, 0, (uint)target.Length, firstLeafAt)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [.. leaves], tile));

        Assert.Equal(found, new PmTilesFileSource(ArchivePath).GetTile(4, 3, 3) is not null);
    }

    [Fact]
    public void The_header_bounds_become_the_tile_range_at_zoom_11()
    {
        // Assemble writes Fürstenfeldbruck's bounds: 10.95..11.45 E, 48.06..48.32 N.
        File.WriteAllBytes(ArchivePath, Archive([(11, 1087, 710, [1])]));

        Assert.Equal((11, 1086, 1089, 709, 711), new PmTilesFileSource(ArchivePath).GetTileBounds());
    }

    [Fact]
    public void An_archive_starting_deeper_than_zoom_11_reports_its_range_at_its_first_zoom()
    {
        var root = Gzip(Directory(new PmTilesEntry(PmTilesFileSource.TileId(12, 2175, 1420), 1, 1, 0)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [], [0], minZoom: 12));

        Assert.Equal((12, 2172, 2178, 1418, 1422), new PmTilesFileSource(ArchivePath).GetTileBounds());
    }

    [Fact]
    public void The_zoom_range_is_known_before_any_tile_is_read()
    {
        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, [1])], maxZoom: 14));

        Assert.Equal(14, new PmTilesFileSource(ArchivePath).GetMaxZoom());
    }

    [Fact]
    public void A_header_whose_root_directory_is_unreadable_reads_as_an_archive_without_tiles()
    {
        File.WriteAllBytes(ArchivePath, Assemble(Gzip([0xFF, 0xFF]), [], []));
        var source = new PmTilesFileSource(ArchivePath);

        Assert.Null(source.GetMaxZoom());
        Assert.Null(source.GetTileBounds());
        Assert.Null(source.GetTile(0, 0, 0));
    }

    public static TheoryData<string, byte[]> BrokenHeaders()
    {
        var root = Gzip(Directory(new PmTilesEntry(0, 1, 1, 0)));
        byte[] Patched(int at, byte value)
        {
            var file = Assemble(root, [], [7]);
            file[at] = value;
            return file;
        }

        return new()
        {
            { "header only", Assemble(root, [], [7])[..PmTilesFileSource.HeaderLength] },
            { "magic", Patched(0, (byte)'X') },
            { "spec version 2", Assemble(root, [], [7], version: 2) },
            { "raster tiles", Assemble(root, [], [7], tileType: 2) },
            { "min zoom above max", Assemble(root, [], [7], minZoom: 10, maxZoom: 9) },
            { "zoom past 22", Assemble(root, [], [7], maxZoom: PmTilesFileSource.MaxSupportedZoom + 1) },
            { "root past the end", Patched(16, 0xF0) },
            { "leaves past the end", Patched(48, 0xF0) },
            { "tile data past the end", Patched(64, 0xF0) },
        };
    }

    [Theory]
    [MemberData(nameof(BrokenHeaders))]
    public void A_header_that_breaks_the_spec_or_points_outside_the_file_reads_as_no_archive(string broken, byte[] file)
    {
        File.WriteAllBytes(ArchivePath, file);
        var source = new PmTilesFileSource(ArchivePath);

        Assert.True(source.GetTile(0, 0, 0) is null, broken);
        Assert.True(source.GetMaxZoom() is null, broken);
    }

    [Fact]
    public void A_tile_entry_reaching_past_its_section_is_null()
    {
        var root = Gzip(Directory(new PmTilesEntry(0, 1, 2, 0)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [], [7, 8, 9], tileCompression: 1));
        var source = new PmTilesFileSource(ArchivePath);

        Assert.Equal(new byte[] { 7, 8 }, source.GetTile(0, 0, 0));
        var past = Gzip(Directory(new PmTilesEntry(0, 1, 2, 2)));
        File.WriteAllBytes(ArchivePath, Assemble(past, [], [7, 8, 9], tileCompression: 1));
        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0));
    }

    [Fact]
    public void An_uncompressed_tile_exactly_at_the_cap_is_read_and_one_byte_more_is_not()
    {
        var atCap = new byte[PmTilesFileSource.MaxTileBytes];
        atCap[^1] = 0x5A;
        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, atCap)], gzipTiles: false));
        Assert.Equal((byte?)0x5A, new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0)?[^1]);

        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, new byte[PmTilesFileSource.MaxTileBytes + 1])], gzipTiles: false));
        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0));
    }

    [Fact]
    public void A_gzipped_tile_inflating_to_exactly_the_cap_is_read()
    {
        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, new byte[PmTilesFileSource.MaxTileBytes])]));

        Assert.Equal(PmTilesFileSource.MaxTileBytes, new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0)?.Length);
    }

    [Fact]
    public void A_tile_with_an_unknown_compression_is_null()
    {
        var root = Gzip(Directory(new PmTilesEntry(0, 1, 1, 0)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [], [7], tileCompression: 3));

        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0));
    }

    [Fact]
    public void The_archive_is_read_while_another_process_holds_it_open_for_writing()
    {
        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, [0x42])]));
        using var other = new FileStream(ArchivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(new byte[] { 0x42 }, new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0));
    }

    [Fact]
    public void An_offset_of_zero_continues_right_after_the_previous_entry_as_clustered_archives_write_it()
    {
        // Two entries: the first at offset 4 (written as 5), the second as "0", meaning 4 + 3.
        byte[] directory = [2, 1, 1, 1, 1, 3, 2, 5, 0];

        var entries = PmTilesFileSource.ParseDirectory(directory);

        Assert.NotNull(entries);
        Assert.Equal([new PmTilesEntry(1, 1, 3, 4), new PmTilesEntry(2, 1, 2, 7)], entries);
    }

    [Theory]
    [InlineData(new byte[] { 2, 1, 1 })] // ids cut short
    [InlineData(new byte[] { 2, 1, 1, 1 })] // run lengths cut short
    [InlineData(new byte[] { 2, 1, 1, 1, 1, 3 })] // lengths cut short
    [InlineData(new byte[] { 2, 1, 1, 1, 1, 3, 2, 5 })] // offsets cut short
    [InlineData(new byte[] { 0x80 })] // the count itself cut short
    public void A_directory_cut_short_in_any_column_is_unreadable(byte[] directory) =>
        Assert.Null(PmTilesFileSource.ParseDirectory(directory));

    [Fact]
    public void A_run_length_or_entry_length_past_its_range_makes_the_directory_unreadable()
    {
        byte[] Entry(ulong run, ulong length) => [1, 0, .. Varint(run), .. Varint(length), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.NotNull(PmTilesFileSource.ParseDirectory(Entry(uint.MaxValue, int.MaxValue)));
        Assert.Null(PmTilesFileSource.ParseDirectory(Entry((ulong)uint.MaxValue + 1, 1)));
        Assert.Null(PmTilesFileSource.ParseDirectory(Entry(1, (ulong)int.MaxValue + 1)));
    }

    [Fact]
    public void The_longest_valid_varint_is_read()
    {
        // ulong.MaxValue takes all ten bytes.
        byte[] directory = [1, .. Varint(ulong.MaxValue), 1, 1, 1];

        Assert.Equal(ulong.MaxValue, Assert.Single(PmTilesFileSource.ParseDirectory(directory)!).TileId);
    }

    [Fact]
    public void A_tile_is_read_by_its_address_and_comes_back_decompressed()
    {
        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, [0xA0]), (15, 17_406, 11_342, [0xA1, 0xA2])]));
        var source = new PmTilesFileSource(ArchivePath);

        Assert.Equal(new byte[] { 0xA1, 0xA2 }, source.GetTile(15, 17_406, 11_342));
        Assert.Equal(new byte[] { 0xA0 }, source.GetTile(0, 0, 0));
        Assert.Null(source.GetTile(15, 17_407, 11_342));
        Assert.Equal(15, source.GetMaxZoom());
    }

    [Fact]
    public void A_tile_behind_a_leaf_directory_is_found()
    {
        var tile = Gzip([0xB0]);
        var tileId = PmTilesFileSource.TileId(14, 8_703, 5_671);
        var leaf = Gzip(Directory(new PmTilesEntry(tileId, 1, (uint)tile.Length, 0)));
        var root = Gzip(Directory(new PmTilesEntry(0, 0, (uint)leaf.Length, 0)));
        File.WriteAllBytes(ArchivePath, Assemble(root, leaf, tile));

        Assert.Equal(new byte[] { 0xB0 }, new PmTilesFileSource(ArchivePath).GetTile(14, 8_703, 5_671));
    }

    [Fact]
    public void A_truncated_header_reads_as_an_archive_without_tiles()
    {
        File.WriteAllBytes(ArchivePath, Archive([(0, 0, 0, [0xA0])])[..100]);
        var source = new PmTilesFileSource(ArchivePath);

        Assert.Null(source.GetTile(0, 0, 0));
        Assert.Null(source.GetMaxZoom());
        Assert.Null(source.GetTileBounds());
    }

    [Fact]
    public void A_tile_whose_offset_points_past_the_end_of_the_file_is_null()
    {
        var root = Gzip(Directory(new PmTilesEntry(0, 1, 16, 1_000_000)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [], new byte[16]));

        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0));
    }

    [Fact]
    public void A_gzip_bomb_is_refused_at_the_tile_cap_instead_of_inflated()
    {
        // A few KB on disk, 9 MB once inflated: past MaxTileBytes.
        var bomb = Gzip(new byte[PmTilesFileSource.MaxTileBytes + (1024 * 1024)]);
        var root = Gzip(Directory(new PmTilesEntry(0, 1, (uint)bomb.Length, 0)));
        File.WriteAllBytes(ArchivePath, Assemble(root, [], bomb));

        Assert.True(bomb.Length < 64 * 1024);
        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(0, 0, 0));
    }

    [Fact]
    public void A_leaf_directory_that_points_at_itself_ends_at_the_depth_cap()
    {
        // The leaf's only entry is a leaf pointer to the leaf itself.
        var leaf = Gzip(Directory(new PmTilesEntry(0, 0, 0, 0)));
        var selfReferencing = Gzip(Directory(new PmTilesEntry(0, 0, (uint)leaf.Length, 0)));
        var root = Gzip(Directory(new PmTilesEntry(0, 0, (uint)selfReferencing.Length, 0)));
        File.WriteAllBytes(ArchivePath, Assemble(root, selfReferencing, []));

        Assert.Null(new PmTilesFileSource(ArchivePath).GetTile(3, 1, 1));
    }

    [Fact]
    public void A_varint_longer_than_ten_bytes_makes_the_directory_unreadable()
    {
        byte[] overlong = [1, .. Enumerable.Repeat((byte)0xFF, 11), 0];

        Assert.Null(PmTilesFileSource.ParseDirectory(overlong));
    }

    [Fact]
    public void A_directory_claiming_more_entries_than_it_has_bytes_is_unreadable()
    {
        Assert.Null(PmTilesFileSource.ParseDirectory([.. Varint(1_000_000), 0, 0, 0]));
    }
}
