namespace LageBuch.Persistence.Map;

/// <summary>Raster tiles of an installed Kartenpaket, addressed in the XYZ scheme (row 0 is north).</summary>
public interface IMapTileSource
{
    /// <summary>The PNG or JPEG bytes of one tile, or null when the pack has none there.</summary>
    byte[]? GetTile(int zoom, int x, int y);

    /// <summary>
    /// The inclusive tile range at the pack's lowest zoom, or null for a pack without tiles. A
    /// map opens on the middle of it when there is nothing better to centre on.
    /// </summary>
    (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds();

    /// <summary>
    /// The pack's highest zoom, or null for a pack without tiles. Past it the map enlarges the
    /// ancestor tile rather than drawing nothing.
    /// </summary>
    int? GetMaxZoom();
}
