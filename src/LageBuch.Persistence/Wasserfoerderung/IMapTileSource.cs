namespace LageBuch.Persistence.Wasserfoerderung;

/// <summary>Reads raster map tiles for the operator's configured Einsatzgebiet (#150, Plan B).</summary>
public interface IMapTileSource
{
    /// <summary>Raw PNG/JPEG bytes for the XYZ/slippy-map tile, or null when it isn't present.</summary>
    byte[]? GetTile(int zoom, int x, int y);

    /// <summary>
    /// The XYZ tile-index bounds (inclusive) at the lowest zoom level this source has any tiles
    /// at, or null when it has none — lets a caller derive a sensible initial map view from the
    /// tiles actually present, instead of an unrelated fixed fallback (#150 follow-up).
    /// </summary>
    (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds();
}
