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

    public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds()
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(mbtilesFilePath);
        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "SELECT zoom_level, MIN(tile_column), MAX(tile_column), MIN(tile_row), MAX(tile_row) " +
            "FROM tiles WHERE zoom_level = (SELECT MIN(zoom_level) FROM tiles) GROUP BY zoom_level;";

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var zoom = reader.GetInt32(0);
        var minX = reader.GetInt32(1);
        var maxX = reader.GetInt32(2);
        var minTmsRow = reader.GetInt32(3);
        var maxTmsRow = reader.GetInt32(4);

        // Stored rows are TMS (row 0 = south); flipping back to XYZ (row 0 = north) inverts the
        // ordering, so the max TMS row becomes the min XYZ row and vice versa.
        var maxRow = (1 << zoom) - 1;
        return (zoom, minX, maxX, maxRow - maxTmsRow, maxRow - minTmsRow);
    }
}
