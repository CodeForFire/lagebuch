namespace LageBuch.Persistence.Map;

/// <summary>
/// Several Kartenpakete drawn as one map, so that a Landkreis border is a seam, not an edge.
/// </summary>
/// <remarks>
/// A pack's tiles along its edge are only partly map: the rest is empty land, because the OSM data
/// was cut to the pack's area before rendering. So a whole tile cannot simply come from the first
/// pack that has one -- the map draws each of <see cref="Layers"/> clipped to its own area. Where
/// two areas overlap, as neighbouring Landkreise do by their buffer, both show the same streets.
/// <see cref="GetTile"/> answers for a caller that draws one tile per address: the first pack's.
/// </remarks>
public sealed class CombinedMapTileSource : IMapTileSource
{
    private readonly IReadOnlyList<IMapTileSource> _sources;

    public CombinedMapTileSource(IReadOnlyList<MapTileLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Layers = layers;
        _sources = [.. layers.Select(l => l.Tiles)];
    }

    /// <summary>The packs, each with its area, in the order the map draws them.</summary>
    public IReadOnlyList<MapTileLayer> Layers { get; }

    public byte[]? GetTile(int zoom, int x, int y)
    {
        foreach (var source in _sources)
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
        var all = _sources.Select(s => s.GetTileBounds()).OfType<(int Zoom, int MinX, int MaxX, int MinY, int MaxY)>().ToList();
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

    public int? GetMaxZoom() => _sources.Select(s => s.GetMaxZoom()).Max();
}
