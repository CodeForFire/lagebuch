using LageBuch.Persistence.Sqlite;

namespace LageBuch.Persistence.Wasserfoerderung;

/// <summary>
/// Reads an MBTiles file (a SQLite database with a standard <c>tiles</c> table) for the
/// operator's configured Einsatzgebiet (#150, Plan B). MBTiles stores rows in TMS scheme
/// (row 0 = south); everything else in this app uses XYZ/slippy-map scheme (row 0 = north), so
/// every read flips the row.
/// </summary>
public sealed class MbTilesFileSource(string mbtilesFilePath) : IMapTileSource
{
    public byte[]? GetTile(int zoom, int x, int y)
    {
        var tmsRow = (1 << zoom) - 1 - y;

        using var cn = SqliteConnectionFactory.OpenReadOnly(mbtilesFilePath);
        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "SELECT tile_data FROM tiles WHERE zoom_level = $z AND tile_column = $x AND tile_row = $y;";
        cmd.Parameters.AddWithValue("$z", zoom);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", tmsRow);

        var result = cmd.ExecuteScalar();
        return result as byte[];
    }
}
