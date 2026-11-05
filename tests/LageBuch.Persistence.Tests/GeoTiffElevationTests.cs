using System.Buffers.Binary;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;
using static LageBuch.Persistence.Tests.GeoTiffFixtures;

namespace LageBuch.Persistence.Tests;

public sealed class GeoTiffElevationTests : IDisposable
{
    // A 5 m grid west of Fürstenfeldbruck, two 256-cell tiles wide and one and a bit high.
    private static readonly Grid Small = new(MinX: 660000, MaxY: 5340000, CellSize: 5, Width: 300, Height: 260);

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-geotiff-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Rises 1 m every 10 m to the east and 0.5 m every 10 m to the north: every cell different.
    private static float Ramp(double east, double north) => (float)(500 + ((east - 660000) / 10) + ((north - 5338000) / 20));

    [Fact]
    public void A_cell_centre_reads_its_own_height()
    {
        var elevation = Open(Build(Small, Ramp));

        Assert.True(elevation.IsReadable);
        Assert.Equal(Ramp(Small.East(7), Small.North(3)), Assert.NotNull(elevation.HeightAt(Small.East(7), Small.North(3))), precision: 4);
        Assert.Equal(Ramp(Small.East(290), Small.North(259)), Assert.NotNull(elevation.HeightAt(Small.East(290), Small.North(259))), precision: 4);
    }

    [Fact]
    public void Between_cell_centres_the_height_is_interpolated()
    {
        var elevation = Open(Build(Small, Ramp));
        var (east, north) = (Small.East(10) + 1.25, Small.North(20) - 3.75);

        // A plane is reproduced exactly by bilinear interpolation.
        Assert.Equal(Ramp(east, north), Assert.NotNull(elevation.HeightAt(east, north)), precision: 3);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Every_storage_the_format_allows_reads_the_same(bool deflate, bool floatPredictor)
    {
        var elevation = Open(Build(Small, Ramp, deflate: deflate, floatPredictor: floatPredictor));

        Assert.Equal(Ramp(Small.East(123), Small.North(45)), Assert.NotNull(elevation.HeightAt(Small.East(123), Small.North(45))), precision: 4);
    }

    [Fact]
    public void A_NoData_cell_is_left_out_of_the_average_and_never_read_as_a_height()
    {
        // Everything west of column 50 is NoData.
        var elevation = Open(Build(Small, (e, n) => e < Small.East(50) ? NoData : 520f));

        Assert.Equal(520, Assert.NotNull(elevation.HeightAt(Small.East(50) - 2, Small.North(10))), precision: 4);
        Assert.Null(elevation.HeightAt(Small.East(20), Small.North(10)));
    }

    [Fact]
    public void Outside_the_grid_there_is_no_height()
    {
        var elevation = Open(Build(Small, Ramp));

        Assert.Null(elevation.HeightAt(Small.MinX - 10, Small.North(5)));
        Assert.Null(elevation.HeightAt(Small.East(5), Small.MaxY + 10));
        Assert.NotNull(elevation.HeightAt(Small.MinX + 0.1, Small.MaxY - 0.1));
    }

    [Fact]
    public void Its_bounds_are_the_grid_s_outer_edges()
    {
        var bounds = Assert.NotNull(Open(Build(Small, Ramp)).Bounds);

        Assert.Equal(660000, bounds.MinX, precision: 6);
        Assert.Equal(5340000 - 1300, bounds.MinY, precision: 6);
        Assert.Equal(660000 + 1500, bounds.MaxX, precision: 6);
        Assert.Equal(5340000, bounds.MaxY, precision: 6);
    }

    [Fact]
    public void A_grid_on_another_projection_than_EPSG_25832_is_refused_rather_than_misplaced()
    {
        var elevation = Open(Build(Small, Ramp, crs: 32632));

        Assert.False(elevation.IsReadable);
        Assert.Null(elevation.HeightAt(Small.East(1), Small.North(1)));
        Assert.Null(elevation.Bounds);
    }

    [Fact]
    public void A_file_that_is_no_TIFF_or_is_missing_is_unreadable()
    {
        Assert.False(Open("PK\u0003\u0004 not a tiff"u8.ToArray()).IsReadable);
        Assert.False(Open([]).IsReadable);
        Assert.False(new GeoTiffElevation(Path.Join(_dir, "missing.tif")).IsReadable);
    }

    [Fact]
    public void A_tile_offset_past_the_end_of_the_file_costs_that_tile_not_the_reader()
    {
        var bytes = Build(Small, Ramp);
        var offsets = TagValueOffset(bytes, 324);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offsets), uint.MaxValue - 16);

        var elevation = Open(bytes);

        Assert.True(elevation.IsReadable);
        Assert.Null(elevation.HeightAt(Small.East(5), Small.North(5)));
        Assert.Equal(Ramp(Small.East(280), Small.North(5)), Assert.NotNull(elevation.HeightAt(Small.East(280), Small.North(5))), precision: 4);
    }

    [Fact]
    public void A_tile_whose_data_inflates_to_the_wrong_size_costs_that_tile_only()
    {
        var bytes = Build(Small, Ramp);
        var counts = TagValueOffset(bytes, 325);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(counts), 40); // the first tile cut short

        var elevation = Open(bytes);

        Assert.Null(elevation.HeightAt(Small.East(5), Small.North(5)));
        Assert.NotNull(elevation.HeightAt(Small.East(280), Small.North(5)));
    }

    [Theory]
    [InlineData(1e30f)]
    [InlineData(-1e30f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NaN)]
    [InlineData(9001f)]
    public void An_impossible_height_reads_as_no_height(float bad)
    {
        var elevation = Open(Build(Small, (e, _) => e < Small.East(50) ? bad : 520f));

        Assert.Null(elevation.HeightAt(Small.East(20), Small.North(10)));
        Assert.Equal(520, Assert.NotNull(elevation.HeightAt(Small.East(60), Small.North(10))), precision: 4);
    }

    [Fact]
    public void Tiles_larger_than_the_format_s_256_cells_are_refused_so_the_cache_stays_small()
    {
        Assert.False(Open(Build(Small with { Width = 600, Height = 600 }, Ramp, tileSide: 512)).IsReadable);
    }

    [Fact]
    public void A_grid_beyond_the_size_cap_is_refused()
    {
        var bytes = Build(Small, Ramp);
        WriteShortTag(bytes, 256, GeoTiffElevation.MaxGridCells + 1);

        Assert.False(Open(bytes).IsReadable);
    }

    [Fact]
    public void More_tiles_than_the_cache_holds_still_read_right()
    {
        // 16-cell tiles: the 300 × 260 grid has 19 × 17 of them, more than CacheTiles.
        var elevation = Open(Build(Small, Ramp, tileSide: 16));

        for (var pass = 0; pass < 2; pass++)
        {
            for (var row = 0; row < Small.Height; row += 13)
            {
                for (var col = 0; col < Small.Width; col += 11)
                {
                    Assert.Equal(Ramp(Small.East(col), Small.North(row)), Assert.NotNull(elevation.HeightAt(Small.East(col), Small.North(row))), precision: 4);
                }
            }
        }
    }

    [Fact]
    public void Many_threads_at_once_read_the_same_heights()
    {
        var elevation = Open(Build(Small, Ramp, tileSide: 16));

        Parallel.For(0, 64, i =>
        {
            for (var k = 0; k < 400; k++)
            {
                var (col, row) = (((i * 37) + (k * 13)) % Small.Width, ((i * 11) + (k * 7)) % Small.Height);
                var expected = Ramp(Small.East(col), Small.North(row));
                Assert.Equal(expected, Assert.NotNull(elevation.HeightAt(Small.East(col), Small.North(row))), precision: 4);
            }
        });
    }

    [Fact]
    public void An_elevation_set_answers_from_the_first_pack_that_has_a_height()
    {
        var west = Open(Build(Small, (_, _) => 510f));
        var east = Open(Build(Small with { MinX = Small.MinX + 1000 }, (_, _) => 530f));
        var set = new ElevationSet([west, east]);

        Assert.Equal(510, Assert.NotNull(set.HeightAt(Small.East(250), Small.North(5))), precision: 4);
        Assert.Equal(530, Assert.NotNull(set.HeightAt(Small.East(250) + 1000, Small.North(5))), precision: 4);
        Assert.Null(set.HeightAt(Small.MinX + 5000, Small.North(5)));
        Assert.True(set.Covers(Small.MinX + 2400, Small.MaxY - 100, Small.MinX + 2600, Small.MaxY));
        Assert.False(set.Covers(Small.MinX + 2600, Small.MaxY - 100, Small.MinX + 2700, Small.MaxY));
    }

    [Fact]
    public void An_elevation_set_takes_a_position_in_degrees()
    {
        var (east, north) = Utm32.FromGeo(new GeoPoint(48.1785, 11.2555));
        var grid = new Grid((Math.Floor(east / 5) * 5) - 500, (Math.Floor(north / 5) * 5) + 500, 5, 200, 200);
        var set = new ElevationSet([Open(Build(grid, (e, _) => (float)(500 + ((e - grid.MinX) / 100))))]);

        Assert.Equal(500 + ((east - grid.MinX) / 100), Assert.NotNull(set.HeightAt(new GeoPoint(48.1785, 11.2555))), precision: 3);
    }

    private GeoTiffElevation Open(byte[] bytes)
    {
        var path = Path.Join(_dir, $"{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, bytes);
        return new GeoTiffElevation(path);
    }

    // Where the value (or the pointer to the values) of a directory entry sits.
    private static int TagValueOffset(byte[] bytes, ushort tag)
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

    private static void WriteShortTag(byte[] bytes, ushort tag, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(TagValueOffset(bytes, tag)), (ushort)value);
}
