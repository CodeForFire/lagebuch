using System.Buffers.Binary;
using LageBuch.Persistence.Map.Elevation;
using static LageBuch.Persistence.Tests.GeoTiffFixtures;

namespace LageBuch.Persistence.Tests;

/// <summary>
/// Each check <see cref="GeoTiffElevation"/> makes of a downloaded file, met by a file that only
/// that check refuses -- and the edge of each cap, which must still be accepted.
/// </summary>
public sealed class GeoTiffElevationGuardTests : IDisposable
{
    private static readonly Grid Small = new(MinX: 660000, MaxY: 5340000, CellSize: 5, Width: 40, Height: 30);

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-geotiff-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static float Ramp(double east, double north) => (float)(500 + ((east - 660000) / 10) + ((north - 5339000) / 20));

    private static readonly Dictionary<string, Tamper> Refused = new()
    {
        { "big-endian", new Tamper { ByteOrder = "MM" } },
        { "not little-endian", new Tamper { ByteOrder = "IX" } },
        { "BigTIFF magic", new Tamper { Magic = 43 } },
        { "directory past the end", new Tamper { DirectoryOffset = 1_000_000 } },
        { "more tags than the cap", new Tamper { ExtraTags = 65 - 17 } },
        { "16-bit samples", new Tamper { Shorts = new Dictionary<ushort, ushort> { [258] = 16 } } },
        { "integer samples", new Tamper { Shorts = new Dictionary<ushort, ushort> { [339] = 1 } } },
        { "three bands", new Tamper { Shorts = new Dictionary<ushort, ushort> { [277] = 3 } } },
        { "planar bands", new Tamper { Shorts = new Dictionary<ushort, ushort> { [284] = 2 } } },
        { "LZW", new Tamper { Shorts = new Dictionary<ushort, ushort> { [259] = 5 } } },
        { "horizontal predictor", new Tamper { Shorts = new Dictionary<ushort, ushort> { [317] = 2 } } },
        { "no tile offsets", new Tamper { Omit = [324] } },
        { "no tile byte counts", new Tamper { Omit = [325] } },
        { "no pixel scale", new Tamper { Omit = [33550] } },
        { "no tie point", new Tamper { Omit = [33922] } },
        { "no geokeys", new Tamper { Omit = [34735] } },
        { "pixel scale with one value", new Tamper { ScaleCount = 1 } },
        { "tie point values past the end", new Tamper { TagPastEnd = true } },
    };

    private static readonly Dictionary<string, Tamper> Accepted = new()
    {
        { "exactly as many tags as the cap", new Tamper { ExtraTags = 64 - 17 } },
        { "a tag of a type the reader skips", new Tamper { RationalTag = true } },
        { "pixel scale without its z value", new Tamper { ScaleCount = 2 } },
        { "the 256-cell tiles FORMAT.md fixes, high but narrow", new Tamper { TileHeight = 256 } },
        { "COMPRESSION_ADOBE_DEFLATE", new Tamper { Shorts = new Dictionary<ushort, ushort> { [259] = 32946 } } },
    };

    public static TheoryData<string> RefusedFiles() => [.. Refused.Keys];

    public static TheoryData<string> AcceptedFiles() => [.. Accepted.Keys];

    [Theory]
    [MemberData(nameof(RefusedFiles))]
    public void A_file_that_breaks_one_rule_is_unreadable(string rule)
    {
        Assert.False(Open(Build(Small, Ramp, tileSide: 16, tamper: Refused[rule])).IsReadable, rule);
    }

    [Theory]
    [MemberData(nameof(AcceptedFiles))]
    public void A_file_inside_every_rule_is_read(string rule)
    {
        var elevation = Open(Build(Small, Ramp, tileSide: 16, tamper: Accepted[rule]));

        Assert.True(elevation.IsReadable, rule);
        Assert.Equal(Ramp(Small.East(3), Small.North(4)), Assert.NotNull(elevation.HeightAt(Small.East(3), Small.North(4))), precision: 3);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(GeoTiffElevation.MaxGridCells, 1, true)]
    [InlineData(1, GeoTiffElevation.MaxGridCells, true)]
    [InlineData(GeoTiffElevation.MaxGridCells + 1, 1, false)]
    [InlineData(1, GeoTiffElevation.MaxGridCells + 1, false)]
    public void A_grid_side_must_be_between_one_cell_and_the_cap(int width, int height, bool readable)
    {
        Assert.Equal(readable, Open(Build(Small with { Width = width, Height = height }, (_, _) => 500f)).IsReadable);
    }

    [Theory]
    [InlineData(512, 16, false)]
    [InlineData(16, 512, false)]
    [InlineData(256, 16, true)]
    [InlineData(16, 256, true)]
    public void A_tile_may_be_at_most_256_cells_either_way(int width, int height, bool readable)
    {
        // Tiles really that size, so only the cap can refuse them.
        var elevation = Open(Build(Small, Ramp, tileSide: width, tamper: new Tamper { TileHeight = height }));

        Assert.Equal(readable, elevation.IsReadable);
    }

    [Fact]
    public void A_grid_without_a_cell_size_is_refused()
    {
        Assert.False(Open(Build(Small with { CellSize = 0 }, Ramp, tileSide: 16)).IsReadable);
    }

    [Fact]
    public void A_tie_point_on_another_raster_cell_than_the_corner_moves_the_grid_by_that_many_cells()
    {
        // Cell (2, 3) at the stated position: the grid's corner lies two cells west and three north of it.
        var elevation = Open(Build(Small, Ramp, tileSide: 16, tamper: new Tamper { TieRaster = (2, 3) }));

        var bounds = Assert.NotNull(elevation.Bounds);
        Assert.Equal(Small.MinX - 10, bounds.MinX, precision: 6);
        Assert.Equal(Small.MaxY + 15, bounds.MaxY, precision: 6);
    }

    [Fact]
    public void A_NoData_value_the_file_declares_is_honoured()
    {
        var elevation = Open(Build(Small, (e, _) => e < Small.East(20) ? 0f : 520f, tileSide: 16, noData: "0"));

        Assert.Null(elevation.HeightAt(Small.East(5), Small.North(5)));
        Assert.Equal(520, Assert.NotNull(elevation.HeightAt(Small.East(30), Small.North(5))), precision: 4);
    }

    [Theory]
    [InlineData(GeoTiffElevation.LowestHeight)]
    [InlineData(GeoTiffElevation.HighestHeight)]
    public void The_ends_of_the_plausible_range_are_still_heights(float height)
    {
        var elevation = Open(Build(Small, (_, _) => height, tileSide: 16));

        Assert.Equal(height, Assert.NotNull(elevation.HeightAt(Small.East(5), Small.North(5))), precision: 3);
    }

    [Fact]
    public void The_grid_s_outer_edges_still_have_heights_and_a_hair_beyond_has_none()
    {
        // One tile exactly the grid's size: past its last cell lies no further tile to fall into.
        var grid = Small with { Width = 32, Height = 32 };
        var elevation = Open(Build(grid, Ramp, tileSide: 32));
        var (west, north) = (grid.MinX, grid.MaxY);
        var (east, south) = (grid.MinX + (32 * 5), grid.MaxY - (32 * 5));

        Assert.NotNull(elevation.HeightAt(west, north));
        Assert.NotNull(elevation.HeightAt(east, south));
        Assert.Equal(Ramp(grid.East(31), grid.North(31)), Assert.NotNull(elevation.HeightAt(east - 1, south + 1)), precision: 3);
        Assert.Null(elevation.HeightAt(west - 1, north - 5));
        Assert.Null(elevation.HeightAt(west + 5, north + 1));
        Assert.Null(elevation.HeightAt(east + 1, south + 5));
        Assert.Null(elevation.HeightAt(east - 5, south - 1));
    }

    [Fact]
    public void An_uncompressed_tile_of_the_wrong_size_costs_that_tile()
    {
        var bytes = Build(Small, Ramp, tileSide: 16, deflate: false);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ValueOffset(bytes, 325)), 100);

        var elevation = Open(bytes);

        Assert.Null(elevation.HeightAt(Small.East(1), Small.North(1)));
        Assert.NotNull(elevation.HeightAt(Small.East(20), Small.North(1)));
    }

    [Fact]
    public void A_tile_of_garbage_where_DEFLATE_data_belongs_costs_that_tile()
    {
        var bytes = Build(Small, Ramp, tileSide: 16);
        var first = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(ValueOffset(bytes, 324)));
        bytes.AsSpan(first, 8).Fill(0xFF);

        var elevation = Open(bytes);

        Assert.Null(elevation.HeightAt(Small.East(1), Small.North(1)));
        Assert.NotNull(elevation.HeightAt(Small.East(20), Small.North(1)));
    }

    [Fact]
    public void An_empty_tile_is_a_hole_and_never_read()
    {
        var bytes = Build(Small, Ramp, tileSide: 16);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ValueOffset(bytes, 325)), 0);

        Assert.Null(Open(bytes).HeightAt(Small.East(1), Small.North(1)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void A_tile_may_claim_at_most_twice_its_raw_size(int beyond, bool readable)
    {
        // Noise does not compress, so the file is long enough to hold the claim either way.
        var bytes = Build(Small, (e, n) => (float)(450 + (50 * Math.Sin((e * 12.9898) + (n * 78.233)))), tileSide: 16);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ValueOffset(bytes, 325)), (uint)((16 * 16 * 4 * 2) + beyond));

        var height = Open(bytes).HeightAt(Small.East(1), Small.North(1));

        Assert.Equal(readable, height is not null);
    }

    [Fact]
    public void The_directory_is_read_once_however_often_and_from_however_many_threads_heights_are_asked()
    {
        var elevation = Open(Build(Small, Ramp, tileSide: 16));

        Parallel.For(0, 32, _ => elevation.HeightAt(Small.East(5), Small.North(5)));
        _ = elevation.Bounds;
        _ = elevation.IsReadable;

        Assert.Equal(1, elevation.LayoutReads);
    }

    [Fact]
    public void A_tile_read_once_is_kept_up_to_the_cache_s_size()
    {
        var (elevation, cell) = ManyTiles();

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < GeoTiffElevation.CacheTiles; i++)
            {
                elevation.HeightAt(cell(i).East, cell(i).North);
            }
        }

        Assert.Equal(GeoTiffElevation.CacheTiles, elevation.TileReads);
    }

    [Fact]
    public void Past_the_cache_s_size_the_tile_used_least_recently_goes()
    {
        var (elevation, cell) = ManyTiles();
        for (var i = 0; i < GeoTiffElevation.CacheTiles; i++)
        {
            elevation.HeightAt(cell(i).East, cell(i).North);
        }

        // Tile 0 is used again, so tile 1 is now the least recent: one more tile drops tile 1, not 0.
        elevation.HeightAt(cell(0).East, cell(0).North);
        elevation.HeightAt(cell(GeoTiffElevation.CacheTiles).East, cell(GeoTiffElevation.CacheTiles).North);
        var before = elevation.TileReads;
        elevation.HeightAt(cell(0).East, cell(0).North);
        elevation.HeightAt(cell(GeoTiffElevation.CacheTiles).East, cell(GeoTiffElevation.CacheTiles).North);
        Assert.Equal(before, elevation.TileReads);

        elevation.HeightAt(cell(1).East, cell(1).North);
        Assert.Equal(before + 1, elevation.TileReads);
    }

    [Fact]
    public void Rectangles_that_only_touch_a_grid_s_edge_are_not_covered_by_it()
    {
        var elevation = Open(Build(Small, Ramp, tileSide: 16));
        var set = new ElevationSet([elevation]);
        var (minX, minY, maxX, maxY) = Assert.NotNull(elevation.Bounds);

        Assert.False(set.Covers(minX - 10, minY, minX, maxY));
        Assert.False(set.Covers(maxX, minY, maxX + 10, maxY));
        Assert.False(set.Covers(minX, minY - 10, maxX, minY));
        Assert.False(set.Covers(minX, maxY, maxX, maxY + 10));
        Assert.True(set.Covers(minX - 10, minY, minX + 1, maxY));
    }

    // A grid of 8 × 8-cell tiles, more of them than the cache holds, and the centre of the i-th tile.
    private (GeoTiffElevation Elevation, Func<int, (double East, double North)> Cell) ManyTiles()
    {
        var grid = Small with { Width = 8 * 16, Height = 8 * 16 };
        var elevation = Open(Build(grid, Ramp, tileSide: 8));
        return (elevation, i => (grid.East(((i % 16) * 8) + 4), grid.North(((i / 16) * 8) + 4)));
    }

    private GeoTiffElevation Open(byte[] bytes)
    {
        var path = Path.Join(_dir, $"{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, bytes);
        return new GeoTiffElevation(path);
    }

    // Where the value, or the pointer to the values, of a directory entry sits.
    private static int ValueOffset(byte[] bytes, ushort tag)
    {
        var directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(directory));
        for (var i = 0; i < count; i++)
        {
            var entry = directory + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry)) == tag)
            {
                var n = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 4));
                return n * 4 <= 4 ? entry + 8 : (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8));
            }
        }

        throw new InvalidOperationException($"No tag {tag}.");
    }
}
