using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// One Kartenpaket: a Landkreis' raster tiles, as the published catalogue lists it and as an
/// installed pack remembers it in its <c>pack.json</c>.
/// </summary>
/// <remarks>
/// <see cref="Slug"/> is the folder the pack is installed under, and
/// <see cref="RegionPackCatalogJson.IsSafeSlug"/> holds for it. Only an <c>https</c>
/// <see cref="DownloadUrl"/> is ever fetched, and a download larger than <see cref="SizeBytes"/>
/// is refused. <see cref="BuiltAt"/> is the render date as the catalogue writes it, shown as it
/// is; <see cref="Attribution"/> is the licence line every map drawn from the pack has to show.
/// </remarks>
[SuppressMessage("Design", "CA1054", Justification = "Catalogue data, kept as the string it arrived as; RegionPackLibrary parses and checks it before any request.")]
[SuppressMessage("Design", "CA1056", Justification = "Catalogue data, kept as the string it arrived as; RegionPackLibrary parses and checks it before any request.")]
public sealed record RegionPackInfo(
    string Name,
    string Slug,
    string DownloadUrl,
    long SizeBytes,
    double MinLatitude,
    double MinLongitude,
    double MaxLatitude,
    double MaxLongitude,
    string BuiltAt,
    string Attribution)
{
    /// <summary>Whether <paramref name="point"/> lies inside the pack's bounding box, edges included.</summary>
    public bool Covers(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.Latitude >= MinLatitude && point.Latitude <= MaxLatitude
            && point.Longitude >= MinLongitude && point.Longitude <= MaxLongitude;
    }
}
