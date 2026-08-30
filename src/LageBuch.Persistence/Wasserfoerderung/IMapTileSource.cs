namespace LageBuch.Persistence.Wasserfoerderung;

/// <summary>Reads raster map tiles for the operator's configured Einsatzgebiet (#150, Plan B).</summary>
public interface IMapTileSource
{
    /// <summary>Raw PNG/JPEG bytes for the XYZ/slippy-map tile, or null when it isn't present.</summary>
    byte[]? GetTile(int zoom, int x, int y);
}
