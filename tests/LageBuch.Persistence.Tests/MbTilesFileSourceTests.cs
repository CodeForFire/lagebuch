using LageBuch.Persistence.Wasserfoerderung;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Persistence.Tests;

public class MbTilesFileSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mbtiles");

    public MbTilesFileSourceTests()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        using (var create = cn.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            create.ExecuteNonQuery();
        }

        // Zoom 3 has 2^3 = 8 rows (TMS 0..7). We seed XYZ tile (z=3,x=1,y=2), which is
        // TMS row (8-1-2)=5, with a one-byte marker payload so the test can tell tiles apart.
        InsertTile(cn, zoom: 3, column: 1, tmsRow: 5, data: new byte[] { 0xAB });
    }

    private static void InsertTile(Microsoft.Data.Sqlite.SqliteConnection cn, int zoom, int column, int tmsRow, byte[] data)
    {
        using var insert = cn.CreateCommand();
        insert.CommandText =
            "INSERT INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES ($z, $x, $y, $d);";
        insert.Parameters.AddWithValue("$z", zoom);
        insert.Parameters.AddWithValue("$x", column);
        insert.Parameters.AddWithValue("$y", tmsRow);
        insert.Parameters.AddWithValue("$d", data);
        insert.ExecuteNonQuery();
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void GetTile_flips_xyz_row_to_the_stored_tms_row()
    {
        var source = new MbTilesFileSource(_path);

        var tile = source.GetTile(zoom: 3, x: 1, y: 2);

        Assert.Equal(new byte[] { 0xAB }, tile);
    }

    [Fact]
    public void GetTile_returns_null_for_a_missing_tile()
    {
        var source = new MbTilesFileSource(_path);

        Assert.Null(source.GetTile(zoom: 3, x: 6, y: 6));
    }

    [Fact]
    public void GetTile_returns_null_for_a_zoom_the_file_does_not_have()
    {
        var source = new MbTilesFileSource(_path);

        Assert.Null(source.GetTile(zoom: 10, x: 1, y: 2));
    }
}
