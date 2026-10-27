namespace LageBuch.Persistence.Map;

/// <summary>
/// Several Kartenpakete drawn as one map, so that a Landkreis border is a seam, not an edge. A tile
/// comes from the first pack that has it; where two packs overlap, as neighbouring Landkreise with
/// their 3 km margin do, the earlier one wins.
/// </summary>
public sealed class CombinedMapTileSource : IMapTileSource
{
    private readonly IReadOnlyList<IMapTileSource> _sources;

    public CombinedMapTileSource(IReadOnlyList<IMapTileSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
    }

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
