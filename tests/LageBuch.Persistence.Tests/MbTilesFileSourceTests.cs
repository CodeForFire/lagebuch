using LageBuch.Persistence.Map;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Persistence.Tests;

public sealed class MbTilesFileSourceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    private string PackPath => Path.Join(_dir, "region.mbtiles");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_tile_is_read_from_the_tms_row_its_xyz_row_flips_to()
    {
        // Zoom 3 has 8 rows; XYZ row 2 is stored as TMS row 8 - 1 - 2 = 5.
        CreatePack((3, 1, 5, [0xAB]));

        Assert.Equal(new byte[] { 0xAB }, new MbTilesFileSource(PackPath).GetTile(3, 1, 2));
    }

    [Fact]
    public void A_tile_the_pack_lacks_is_null()
    {
        CreatePack((3, 1, 5, [0xAB]));
        var source = new MbTilesFileSource(PackPath);

        Assert.Null(source.GetTile(3, 6, 6));
        Assert.Null(source.GetTile(10, 1, 2));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(23, 0, 0)]
    [InlineData(3, -1, 0)]
    [InlineData(3, 8, 0)]
    [InlineData(3, 0, 8)]
    public void A_tile_address_off_the_world_is_null_without_asking_the_file(int zoom, int x, int y)
    {
        // A hostile pack can hold a row at exactly the TMS address an unchecked lookup would ask for.
        CreatePack((zoom, x, unchecked((1 << zoom) - 1 - y), [1]));

        Assert.Null(new MbTilesFileSource(PackPath).GetTile(zoom, x, y));
    }

    [Fact]
    public void The_corner_tiles_of_the_coarsest_and_the_deepest_zoom_are_read()
    {
        var deepest = MbTilesFileSource.MaxSupportedZoom;
        var lastRow = (1 << deepest) - 1;
        CreatePack((0, 0, 0, [0xA0]), (deepest, 0, lastRow, [0xA1]), (deepest, lastRow, 0, [0xA2]));
        var source = new MbTilesFileSource(PackPath);

        Assert.Equal(new byte[] { 0xA0 }, source.GetTile(0, 0, 0));
        Assert.Equal(new byte[] { 0xA1 }, source.GetTile(deepest, 0, 0));
        Assert.Equal(new byte[] { 0xA2 }, source.GetTile(deepest, lastRow, lastRow));
    }

    [Fact]
    public void The_bounds_are_the_xyz_range_of_the_lowest_zoom()
    {
        // TMS rows 4..6 at zoom 3 are XYZ rows 7-6=1 .. 7-4=3.
        CreatePack((3, 2, 4, [1]), (3, 5, 6, [1]), (4, 0, 0, [1]));

        Assert.Equal((3, 2, 5, 1, 3), new MbTilesFileSource(PackPath).GetTileBounds());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MbTilesFileSource.MaxSupportedZoom)]
    public void The_bounds_and_the_max_zoom_are_read_at_either_end_of_the_supported_zooms(int zoom)
    {
        CreatePack((zoom, 0, 0, [1]));
        var source = new MbTilesFileSource(PackPath);
        var lastRow = (1 << zoom) - 1;

        Assert.Equal((zoom, 0, 0, lastRow, lastRow), source.GetTileBounds());
        Assert.Equal(zoom, source.GetMaxZoom());
    }

    [Fact]
    public void A_pack_whose_zooms_lie_past_the_supported_ones_has_no_bounds_and_no_max_zoom()
    {
        CreatePack((MbTilesFileSource.MaxSupportedZoom + 1, 0, 0, [1]));
        var source = new MbTilesFileSource(PackPath);

        Assert.Null(source.GetTileBounds());
        Assert.Null(source.GetMaxZoom());
    }

    [Fact]
    public void The_max_zoom_is_read_from_the_file_only_once()
    {
        CreatePack((11, 0, 0, [1]));
        var source = new MbTilesFileSource(PackPath);
        Assert.Equal(11, source.GetMaxZoom());

        AddTiles((15, 0, 0, [1]));

        Assert.Equal(11, source.GetMaxZoom());
    }

    [Fact]
    public void A_pack_without_tiles_has_no_bounds_and_no_max_zoom()
    {
        CreatePack();
        var source = new MbTilesFileSource(PackPath);

        Assert.Null(source.GetTileBounds());
        Assert.Null(source.GetMaxZoom());
    }

    [Fact]
    public void The_max_zoom_is_the_deepest_zoom_stored()
    {
        CreatePack((11, 0, 0, [1]), (15, 0, 0, [1]));

        Assert.Equal(15, new MbTilesFileSource(PackPath).GetMaxZoom());
    }

    [Fact]
    public void A_file_that_is_no_database_reads_as_a_pack_without_tiles()
    {
        File.WriteAllText(PackPath, "not a database");
        var source = new MbTilesFileSource(PackPath);

        Assert.Null(source.GetTile(3, 1, 2));
        Assert.Null(source.GetTileBounds());
        Assert.Null(source.GetMaxZoom());
    }

    [Fact]
    public void Reading_a_tile_leaves_the_pack_deletable()
    {
        CreatePack((3, 1, 5, [0xAB]));
        _ = new MbTilesFileSource(PackPath).GetTile(3, 1, 2);

        File.Delete(PackPath);

        Assert.False(File.Exists(PackPath));
    }

    private void CreatePack(params (int Zoom, int Column, int TmsRow, byte[] Data)[] tiles)
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(PackPath))
        using (var create = cn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            create.ExecuteNonQuery();
        }

        AddTiles(tiles);
    }

    private void AddTiles(params (int Zoom, int Column, int TmsRow, byte[] Data)[] tiles)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(PackPath);
        foreach (var (zoom, column, row, data) in tiles)
        {
            using var insert = cn.CreateCommand();
            insert.CommandText = "INSERT INTO tiles VALUES ($z, $x, $y, $d);";
            insert.Parameters.AddWithValue("$z", zoom);
            insert.Parameters.AddWithValue("$x", column);
            insert.Parameters.AddWithValue("$y", row);
            insert.Parameters.AddWithValue("$d", data);
            insert.ExecuteNonQuery();
        }
    }
}
