using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Fetches the region-pack manifest over HTTP (#150 follow-up). The one place this offline-first
/// app makes an unprompted network call — but only when the operator opens the Einsatzgebiet
/// section, and it degrades to an empty list rather than surfacing any error, so Stammdaten stays
/// fully usable without a connection.
/// </summary>
[SuppressMessage("Design", "CA1054", Justification = "manifestUrl is a configured string passed straight to HttpClient; System.Uri adds no value over the string constructors it already accepts.")]
public sealed class RegionPackCatalogService(HttpClient httpClient, string manifestUrl) : IRegionPackCatalogService
{
    public async Task<IReadOnlyList<RegionPackInfo>> GetAvailableRegionsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await httpClient.GetStringAsync(new Uri(manifestUrl), ct);
            return RegionPackCatalogJson.Parse(json);
        }
        catch (HttpRequestException)
        {
            return Array.Empty<RegionPackInfo>();
        }
    }
}
