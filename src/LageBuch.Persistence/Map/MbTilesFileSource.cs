using System.Diagnostics.CodeAnalysis;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Persistence.Map;

/// <summary>
/// Reads the <c>map.mbtiles</c> of a Kartenpaket: a SQLite file with the standard
/// <c>tiles(zoom_level, tile_column, tile_row, tile_data)</c> table. MBTiles numbers rows in the
/// TMS scheme (row 0 is south), so every read flips the row into XYZ.
/// </summary>
/// <remarks>
/// Opens a connection per call rather than holding one. A held connection would make the source
/// disposable and keep the file locked on Windows, so a pack could not be removed or updated
/// while a map shows it. The map caches decoded tiles, so a call happens only on a cache miss.
/// </remarks>
public sealed class MbTilesFileSource : IMapTileSource
{
    /// <summary>The deepest zoom a tile address is accepted for; nothing renders finer than this.</summary>
    public const int MaxSupportedZoom = 22;

    private readonly string _path;

    // A pack is never written to once installed, so its zoom range is read once.
    private int? _maxZoom;
    private bool _maxZoomRead;

    public MbTilesFileSource(string mbtilesFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mbtilesFilePath);
        _path = mbtilesFilePath;
    }

    [SuppressMessage("Design", "CA1031", Justification = "A damaged or missing pack file must cost a blank tile, not the map: null is the documented 'no tile here' result.")]
    public byte[]? GetTile(int zoom, int x, int y)
    {
        if (zoom is < 0 or > MaxSupportedZoom)
        {
            return null;
        }

        var span = 1 << zoom;
        if (x < 0 || y < 0 || x >= span || y >= span)
        {
            return null;
        }

        try
        {
            using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level = $z AND tile_column = $x AND tile_row = $y;";
            cmd.Parameters.AddWithValue("$z", zoom);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", span - 1 - y);
            return cmd.ExecuteScalar() as byte[];
        }
        catch (Exception)
        {
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A damaged pack file reads as a pack without tiles: null is the documented result.")]
    public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds()
    {
        try
        {
            using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
            using var cmd = cn.CreateCommand();
            cmd.CommandText =
                "SELECT zoom_level, MIN(tile_column), MAX(tile_column), MIN(tile_row), MAX(tile_row) " +
                "FROM tiles WHERE zoom_level = (SELECT MIN(zoom_level) FROM tiles) GROUP BY zoom_level;";
            using var reader = cmd.ExecuteReader();

            // Stryker disable once Block : without a row GetInt32 throws, and the catch below returns the same null.
            if (!reader.Read())
            {
                return null;
            }

            var zoom = reader.GetInt32(0);
            if (zoom is < 0 or > MaxSupportedZoom)
            {
                return null;
            }

            // Flipping TMS into XYZ inverts the order: the highest TMS row is the lowest XYZ row.
            var lastRow = (1 << zoom) - 1;
            return (zoom, reader.GetInt32(1), reader.GetInt32(2), lastRow - reader.GetInt32(4), lastRow - reader.GetInt32(3));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public int? GetMaxZoom()
    {
        if (!_maxZoomRead)
        {
            _maxZoom = ReadMaxZoom();
            _maxZoomRead = true;
        }

        return _maxZoom;
    }

    [SuppressMessage("Design", "CA1031", Justification = "A damaged pack file reads as a pack without tiles: null is the documented result.")]
    private int? ReadMaxZoom()
    {
        try
        {
            using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT MAX(zoom_level) FROM tiles;";
            return cmd.ExecuteScalar() is long max && max is >= 0 and <= MaxSupportedZoom ? (int)max : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
