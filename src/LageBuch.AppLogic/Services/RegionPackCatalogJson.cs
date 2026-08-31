using System.Text.Json;
using System.Text.RegularExpressions;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Reads the region-pack manifest format (#150 follow-up) — a flat JSON array, each entry
/// describing one downloadable pack. Defensive like <c>MasterDataJson</c>: malformed input, or an
/// entry missing a required field, is skipped rather than thrown — the manifest is fetched from a
/// third-party-controlled URL, so a partially bad response must degrade gracefully, not crash
/// Stammdaten.
/// </summary>
public static class RegionPackCatalogJson
{
    // RegionPackInstaller joins this straight onto a base directory (<regionsBaseDir>/<slug>) —
    // reject anything that could escape that directory (path separators, "..", empty) here rather
    // than trusting the installer alone to catch it.
    private static readonly Regex SafeSlug = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled);

    public static IReadOnlyList<RegionPackInfo> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<RegionPackInfo>();
            }

            var result = new List<RegionPackInfo>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (TryParseEntry(entry, out var region))
                {
                    result.Add(region);
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<RegionPackInfo>();
        }
    }

    private static bool TryParseEntry(JsonElement entry, out RegionPackInfo region)
    {
        region = null!;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetString(entry, "name", out var name) ||
            !TryGetString(entry, "slug", out var slug) || !SafeSlug.IsMatch(slug) ||
            !TryGetString(entry, "downloadUrl", out var downloadUrl) ||
            !TryGetString(entry, "builtAt", out var builtAt) ||
            !TryGetString(entry, "attribution", out var attribution) ||
            !entry.TryGetProperty("sizeBytes", out var sizeBytesEl) || sizeBytesEl.ValueKind != JsonValueKind.Number ||
            !entry.TryGetProperty("boundingBox", out var bbox) || bbox.ValueKind != JsonValueKind.Object ||
            !TryGetNumber(bbox, "minLat", out var minLat) ||
            !TryGetNumber(bbox, "minLon", out var minLon) ||
            !TryGetNumber(bbox, "maxLat", out var maxLat) ||
            !TryGetNumber(bbox, "maxLon", out var maxLon))
        {
            return false;
        }

        region = new RegionPackInfo(
            name,
            slug,
            downloadUrl,
            sizeBytesEl.GetInt64(),
            minLat,
            minLon,
            maxLat,
            maxLon,
            builtAt,
            attribution);
        return true;
    }

    private static bool TryGetString(JsonElement e, string prop, out string value)
    {
        value = string.Empty;
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = v.GetString()!;
        return true;
    }

    private static bool TryGetNumber(JsonElement e, string prop, out double value)
    {
        value = 0;
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        value = v.GetDouble();
        return true;
    }
}
