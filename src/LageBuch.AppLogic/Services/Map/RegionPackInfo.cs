using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// One Kartenpaket from <c>CodeForFire/lagebuch-geodata</c>: a Landkreis' base map, terrain
/// heights, water points and addresses, as the published <c>packs.json</c> lists it and as an
/// installed pack remembers it.
/// </summary>
/// <remarks>
/// <see cref="Slug"/> is the folder the pack is installed under, and
/// <see cref="RegionPackCatalogJson.IsSafeSlug"/> holds for it. Only an <c>https</c>
/// <see cref="DownloadUrl"/> is ever fetched, a download larger than <see cref="SizeBytes"/> is
/// refused, and one whose SHA-256 is not <see cref="Sha256"/> (lower-case hex) is never unpacked.
/// <see cref="Version"/> rises with every rebuild. <see cref="BuiltAt"/> is the build time as the
/// catalogue writes it; <see cref="Attribution"/> is the licence line every map drawn from the
/// pack has to show. <see cref="BoundaryUrl"/> is where the pack's area is published, fetched at
/// install when the zip does not carry it; null when the catalogue names none.
/// </remarks>
[SuppressMessage("Design", "CA1054", Justification = "Catalogue data, kept as the string it arrived as; RegionPackLibrary parses and checks it before any request.")]
[SuppressMessage("Design", "CA1056", Justification = "Catalogue data, kept as the string it arrived as; RegionPackLibrary parses and checks it before any request.")]
public sealed record RegionPackInfo(
    string Name,
    string Slug,
    int Version,
    string DownloadUrl,
    long SizeBytes,
    string Sha256,
    double West,
    double South,
    double East,
    double North,
    int MinZoom,
    int MaxZoom,
    string BuiltAt,
    string Attribution,
    string? BoundaryUrl = null)
{
    /// <summary>Whether <paramref name="point"/> lies inside the pack's bounding box, edges included.</summary>
    public bool Covers(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.Latitude >= South && point.Latitude <= North
            && point.Longitude >= West && point.Longitude <= East;
    }
}
