namespace LageBuch.AppLogic.Services;

/// <summary>
/// Lists the region packs (map tiles + elevation) publicly available for download (#150 follow-up)
/// — the map-side counterpart to Stammdaten's Einsatzgebiet, which used to require hand-preparing
/// region.mbtiles/region.dem with no guidance at all.
/// </summary>
public interface IRegionPackCatalogService
{
    /// <summary>
    /// Never throws — a Stammdaten editor must stay usable offline, so a fetch/parse failure
    /// yields an empty list rather than propagating an exception.
    /// </summary>
    Task<IReadOnlyList<RegionPackInfo>> GetAvailableRegionsAsync(CancellationToken ct = default);
}

/// <summary>One published, downloadable region pack.</summary>
public sealed record RegionPackInfo(
    string Name,
    string Slug,
    string DownloadUrl,
    long SizeBytes,
    double MinLat,
    double MinLon,
    double MaxLat,
    double MaxLon,
    string BuiltAt,
    string Attribution);
