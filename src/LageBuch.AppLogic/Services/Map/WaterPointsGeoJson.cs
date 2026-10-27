using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Reads a Kartenpaket's <c>water.geojson</c>: an RFC 7946 FeatureCollection of Points. The file
/// came from the network, so a feature that is no Point, lies off the globe or carries an
/// <c>emergency</c> tag outside FORMAT.md's four is skipped, and the values OSM holds are taken
/// as text and never trusted to be well-formed.
/// </summary>
public static class WaterPointsGeoJson
{
    /// <summary>A Landkreis holds a few thousand; a file larger than this is not a list of hydrants.</summary>
    public const long MaxFileBytes = 32 * 1024 * 1024;

    /// <summary>The most water points read from one pack.</summary>
    public const int MaxFeatures = 200_000;

    private const int MaxLabelPartLength = 24;

    /// <summary>The pack's water points, or null when <paramref name="json"/> is not a FeatureCollection at all.</summary>
    public static IReadOnlyList<WaterPoint>? Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "FeatureCollection", StringComparison.Ordinal)
                || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var points = new List<WaterPoint>();
            foreach (var feature in features.EnumerateArray().Take(MaxFeatures))
            {
                if (TryParseFeature(feature, out var point))
                {
                    points.Add(point);
                }
            }

            return points;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseFeature(JsonElement feature, [MaybeNullWhen(false)] out WaterPoint point)
    {
        point = null;
        if (feature.ValueKind != JsonValueKind.Object
            || !feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object
            || Text(geometry, "type") != "Point"
            || !geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2
            || coordinates[0].ValueKind != JsonValueKind.Number || !coordinates[0].TryGetDouble(out var longitude)
            || coordinates[1].ValueKind != JsonValueKind.Number || !coordinates[1].TryGetDouble(out var latitude)
            || !feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // GeoJSON writes longitude first.
        var position = GeoPoint.TryCreate(latitude, longitude);
        WaterPointKind? kind = Text(properties, "emergency") switch
        {
            "fire_hydrant" => WaterPointKind.Hydrant,
            "suction_point" => WaterPointKind.Saugstelle,
            "water_tank" => WaterPointKind.Loeschwasserbehaelter,
            "fire_water_pond" => WaterPointKind.Loeschteich,
            _ => null,
        };
        if (position is null || kind is not { } k)
        {
            return false;
        }

        point = new WaterPoint(position, k, Label(k, properties));
        return true;
    }

    // "UH 100" for an Unterflurhydrant DN 100, the way it is written on the Hinweisschild; for
    // anything else its name or Nummer. A value OSM spells some other way is shown as it is.
    private static string Label(WaterPointKind kind, JsonElement properties)
    {
        var name = Text(properties, "ref") ?? Text(properties, "name");
        if (kind != WaterPointKind.Hydrant)
        {
            return name ?? string.Empty;
        }

        var type = Text(properties, "fire_hydrant:type") switch
        {
            "underground" => "UH",
            "pillar" => "ÜH",
            "wall" => "WH",
            null or "unknown" => null,
            var other => other,
        };
        var parts = new[] { type, Text(properties, "fire_hydrant:diameter") }.OfType<string>().ToList();
        return parts.Count > 0 ? string.Join(' ', parts) : name ?? string.Empty;
    }

    private static string? Text(JsonElement e, string property)
    {
        if (!e.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = v.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxLabelPartLength ? text[..MaxLabelPartLength] : text;
    }
}
