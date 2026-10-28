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
    public void The_tile_id_follows_the_spec_hilbert_curve(int zoom, int x, int y, ulong id) =>
        Assert.Equal(id, PmTilesFileSource.TileId(zoom, x, y));

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
