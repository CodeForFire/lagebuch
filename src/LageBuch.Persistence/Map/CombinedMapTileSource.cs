namespace LageBuch.Persistence.Map;

/// <summary>
/// Several Kartenpakete drawn as one map, so that a Landkreis border is a seam, not an edge.
/// </summary>
/// <remarks>
/// A pack's vector tiles are cut to its area when it is built, so along its edge a tile holds
/// only what lies inside. A whole tile therefore cannot come from the first pack that has one --
/// the map draws every one of <see cref="Layers"/> on top of the last. Where two areas overlap,
/// as neighbouring Landkreise do by their buffer, both draw the same streets.
/// <see cref="GetTile"/> answers for a caller that draws one tile per address: the first pack's.
/// </remarks>
public sealed class CombinedMapTileSource : IMapTileSource
{
    public CombinedMapTileSource(IReadOnlyList<IMapTileSource> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Layers = layers;
    }

    /// <summary>The packs, in the order the map draws them.</summary>
    public IReadOnlyList<IMapTileSource> Layers { get; }

    public byte[]? GetTile(int zoom, int x, int y)
    {
        foreach (var source in Layers)
        {
            if (source.GetTile(zoom, x, y) is { } tile)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>The range covering every pack, at the lowest zoom any of them has.</summary>
    public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds()
    {
        var all = Layers.Select(s => s.GetTileBounds()).OfType<(int Zoom, int MinX, int MaxX, int MinY, int MaxY)>().ToList();
        if (all.Count == 0)
        {
            return null;
        }

        var zoom = all.Min(b => b.Zoom);

        // A coarser tile's index is the finer one shifted by the zoom difference.
        return (
            zoom,
            all.Min(b => b.MinX >> (b.Zoom - zoom)),
            all.Max(b => b.MaxX >> (b.Zoom - zoom)),
            all.Min(b => b.MinY >> (b.Zoom - zoom)),
            all.Max(b => b.MaxY >> (b.Zoom - zoom)));
    }

    public int? GetMaxZoom() => Layers.Select(s => s.GetMaxZoom()).Max();
}
