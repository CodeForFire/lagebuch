using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Reads the Kartenpaket catalogue: a JSON array with one object per pack. The catalogue comes
/// from the network, so an entry missing a field, carrying a slug that could name a path, or a
/// bounding box off the globe is skipped rather than trusted.
/// </summary>
public static partial class RegionPackCatalogJson
{
    /// <summary>A catalogue with more entries than this is not a list of Landkreise.</summary>
    public const int MaxEntries = 500;

    private const int MaxTextLength = 300;

    /// <summary>
    /// Whether <paramref name="slug"/> can safely become a folder name: lower-case letters, digits,
    /// '-' and '_', starting with a letter or digit. No separator, no "..", nothing empty.
    /// </summary>
    public static bool IsSafeSlug([NotNullWhen(true)] string? slug) => slug is not null && SafeSlug().IsMatch(slug);

    /// <summary>The catalogue's packs, or null when <paramref name="json"/> is not a catalogue at all.</summary>
    public static IReadOnlyList<RegionPackInfo>? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var packs = new List<RegionPackInfo>();
            foreach (var entry in doc.RootElement.EnumerateArray().Take(MaxEntries))
            {
                if (TryParseEntry(entry, out var pack))
                {
                    packs.Add(pack);
                }
            }

            return packs;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseEntry(JsonElement entry, [MaybeNullWhen(false)] out RegionPackInfo pack)
    {
        pack = null;
        if (entry.ValueKind != JsonValueKind.Object
            || !TryGetText(entry, "name", out var name)
            || !TryGetText(entry, "slug", out var slug) || !IsSafeSlug(slug)
            || !TryGetText(entry, "downloadUrl", out var downloadUrl)
            || !TryGetText(entry, "builtAt", out var builtAt)
            || !TryGetText(entry, "attribution", out var attribution)
            || !entry.TryGetProperty("sizeBytes", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var sizeBytes) || sizeBytes <= 0
            || !entry.TryGetProperty("boundingBox", out var box) || box.ValueKind != JsonValueKind.Object
            || !TryGetNumber(box, "minLat", -90, 90, out var minLat)
            || !TryGetNumber(box, "maxLat", -90, 90, out var maxLat)
            || !TryGetNumber(box, "minLon", -180, 180, out var minLon)
            || !TryGetNumber(box, "maxLon", -180, 180, out var maxLon)
            || minLat >= maxLat || minLon >= maxLon)
        {
            return false;
        }

        pack = new RegionPackInfo(name, slug, downloadUrl, sizeBytes, minLat, minLon, maxLat, maxLon, builtAt, attribution);
        return true;
    }

    private static bool TryGetText(JsonElement e, string property, [MaybeNullWhen(false)] out string value)
    {
        value = null;
        if (!e.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = v.GetString()?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryGetNumber(JsonElement e, string property, double min, double max, out double value)
    {
        value = 0;
        return e.TryGetProperty(property, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDouble(out value)
            && double.IsFinite(value) && value >= min && value <= max;
    }

    // \z, not $: in .NET '$' also matches before a trailing newline, which would let "ffb\n" through.
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSlug();
}
