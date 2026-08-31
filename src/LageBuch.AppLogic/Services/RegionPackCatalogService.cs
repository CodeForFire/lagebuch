namespace LageBuch.AppLogic.Services;

/// <summary>
/// Fetches the region-pack manifest over HTTP (#150 follow-up). The one place this offline-first
/// app makes an unprompted network call — but only when the operator opens the Einsatzgebiet
/// section, and it degrades to an empty list rather than surfacing any error, so Stammdaten stays
/// fully usable without a connection.
/// </summary>
public sealed class RegionPackCatalogService(HttpClient httpClient, string manifestUrl) : IRegionPackCatalogService
{
    public async Task<IReadOnlyList<RegionPackInfo>> GetAvailableRegionsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await httpClient.GetStringAsync(manifestUrl, ct);
            return RegionPackCatalogJson.Parse(json);
        }
        catch (HttpRequestException)
        {
            return Array.Empty<RegionPackInfo>();
        }
    }
}
