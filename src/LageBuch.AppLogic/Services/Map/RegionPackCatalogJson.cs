using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Reads the Kartenpaket catalogue, <c>packs.json</c> of <c>CodeForFire/lagebuch-geodata</c>
/// (format version 2, its <c>FORMAT.md</c>). The catalogue comes from the network, so an entry
/// missing a field, carrying a slug that could name a path, a bounding box off the globe or a
/// checksum that is no SHA-256 is skipped rather than trusted.
/// </summary>
public static partial class RegionPackCatalogJson
{
    /// <summary>The pack format this build reads. A catalogue or pack of any other version is refused.</summary>
    public const int FormatVersion = 2;

    /// <summary>A catalogue with more entries than this is not a list of Landkreise.</summary>
    public const int MaxEntries = 500;

    /// <summary>No tile address is deeper than this; see <c>MbTilesFileSource.MaxSupportedZoom</c>.</summary>
    public const int MaxZoomLevel = 22;

    private const int MaxTextLength = 300;

    /// <summary>
    /// Whether <paramref name="slug"/> can safely become a folder name: FORMAT.md's
    /// <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, at most 32 characters. No separator, no "..", nothing empty.
    /// </summary>
    public static bool IsSafeSlug([NotNullWhen(true)] string? slug) => slug is { Length: <= 32 } && SafeSlug().IsMatch(slug);

    /// <summary>Whether <paramref name="value"/> is a SHA-256 written as 64 lower-case hex digits.</summary>
    public static bool IsSha256([NotNullWhen(true)] string? value) => value is not null && Sha256Hex().IsMatch(value);

    /// <summary>The catalogue's packs, or null when <paramref name="json"/> is not a catalogue at all.</summary>
    /// <exception cref="RegionPackException">The catalogue is of a format version this build does not read.</exception>
    public static IReadOnlyList<RegionPackInfo>? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("formatVersion", out var format) || format.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("packs", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (!format.TryGetInt32(out var version) || version != FormatVersion)
            {
                throw new RegionPackException("Die Kartenpakete brauchen eine neuere Lagebuch-Version. Lagebuch aktualisieren.");
            }

            var packs = new List<RegionPackInfo>();
            foreach (var entry in entries.EnumerateArray().Take(MaxEntries))
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
            || !TryGetInt(entry, "version", 1, int.MaxValue, out var version)
            || !TryGetText(entry, "downloadUrl", out var downloadUrl)
            || !TryGetText(entry, "sha256", out var sha256) || !IsSha256(sha256)
            || !TryGetText(entry, "builtAt", out var builtAt)
            || !TryGetText(entry, "attribution", out var attribution)
            || !entry.TryGetProperty("sizeBytes", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var sizeBytes) || sizeBytes <= 0
            || !TryGetInt(entry, "minZoom", 0, MaxZoomLevel, out var minZoom)
            || !TryGetInt(entry, "maxZoom", minZoom, MaxZoomLevel, out var maxZoom)
            || !entry.TryGetProperty("bounds", out var box) || box.ValueKind != JsonValueKind.Object
            || !TryGetNumber(box, "south", -90, 90, out var south)
            || !TryGetNumber(box, "north", -90, 90, out var north)
            || !TryGetNumber(box, "west", -180, 180, out var west)
            || !TryGetNumber(box, "east", -180, 180, out var east)
            || south >= north || west >= east)
        {
            return false;
        }

        // Optional: without an area the pack is drawn whole, as it was before areas were published.
        var boundaryUrl = TryGetText(entry, "boundaryUrl", out var b) ? b : null;
        pack = new RegionPackInfo(name, slug, version, downloadUrl, sizeBytes, sha256, west, south, east, north, minZoom, maxZoom, builtAt, attribution, boundaryUrl);
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

    private static bool TryGetInt(JsonElement e, string property, int min, int max, out int value)
    {
        value = 0;
        return e.TryGetProperty(property, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out value)
            && value >= min && value <= max;
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
    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSlug();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Hex();
}
