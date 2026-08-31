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

    [Fact]
    public void GetTileBounds_returns_null_when_the_file_has_no_tiles()
    {
        var emptyPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mbtiles");
        using (var cn = SqliteConnectionFactory.OpenReadWrite(emptyPath))
        using (var create = cn.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            create.ExecuteNonQuery();
        }
        try
        {
            var source = new MbTilesFileSource(emptyPath);

            Assert.Null(source.GetTileBounds());
        }
        finally
        {
            File.Delete(emptyPath);
        }
    }

    [Fact]
    public void GetTileBounds_flips_stored_tms_rows_back_to_xyz_and_uses_the_lowest_zoom_present()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            // Zoom 3 (already seeded: XYZ x=1,y=2 -> TMS row 5) gets a second tile to give it a
            // real column/row spread. Zoom 5 is also seeded, deliberately with a wider spread, to
            // prove the lowest zoom present (3) wins, not the widest bounds.
            InsertTile(cn, zoom: 3, column: 3, tmsRow: 1, data: new byte[] { 0xCD }); // XYZ (x=3,y=6)
            InsertTile(cn, zoom: 5, column: 0, tmsRow: 0, data: new byte[] { 0xEE });
            InsertTile(cn, zoom: 5, column: 31, tmsRow: 31, data: new byte[] { 0xFF });
        }

        var source = new MbTilesFileSource(_path);
        var bounds = source.GetTileBounds();

        Assert.NotNull(bounds);
        Assert.Equal(3, bounds!.Value.Zoom);
        Assert.Equal(1, bounds.Value.MinX);
        Assert.Equal(3, bounds.Value.MaxX);
        // Seeded tiles: XYZ (x=1,y=2) -> TMS row 5; and TMS row 1 at zoom 3 -> XYZ row (8-1-1)=6.
        Assert.Equal(2, bounds.Value.MinY);
        Assert.Equal(6, bounds.Value.MaxY);
    }

    [Fact]
    public void GetMaxZoom_returns_null_when_the_file_has_no_tiles()
    {
        var emptyPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mbtiles");
        using (var cn = SqliteConnectionFactory.OpenReadWrite(emptyPath))
        using (var create = cn.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            create.ExecuteNonQuery();
        }
        try
        {
            var source = new MbTilesFileSource(emptyPath);

            Assert.Null(source.GetMaxZoom());
        }
        finally
        {
            File.Delete(emptyPath);
        }
    }

    [Fact]
    public void GetMaxZoom_returns_the_highest_zoom_level_present()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
            InsertTile(cn, zoom: 7, column: 0, tmsRow: 0, data: new byte[] { 0x11 });

        var source = new MbTilesFileSource(_path);

        Assert.Equal(7, source.GetMaxZoom());
    }
}
