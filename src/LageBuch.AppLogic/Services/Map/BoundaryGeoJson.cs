using System.Text.Json;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Reads a Kartenpaket's area, <c>boundary.geojson</c>: the Landkreis plus its buffer, as one
/// Polygon or MultiPolygon in WGS84 (FORMAT.md). A pack is refused unless it carries one; the
/// area is what its tiles, water points and addresses are complete in.
/// </summary>
public static class BoundaryGeoJson
{
    /// <summary>A Landkreis simplified to ~20 m is a few hundred points; a file larger than this is not one.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>The most points read from one boundary, all rings together.</summary>
    public const int MaxPoints = 100_000;

    /// <summary>
    /// The area's rings -- outer rings and holes alike, to be filled even-odd -- or null when
    /// <paramref name="json"/> is no Feature, FeatureCollection or geometry holding a Polygon or
    /// MultiPolygon, or holds a point off the globe.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<GeoPoint>>? Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var geometry = Geometry(doc.RootElement);
            if (geometry is not { } g || !g.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<JsonElement>? polygons = Text(g, "type") switch
            {
                "Polygon" => [coordinates],
                "MultiPolygon" => coordinates.EnumerateArray().ToList(),
                _ => null,
            };
            if (polygons is null)
            {
                return null;
            }

            var rings = new List<IReadOnlyList<GeoPoint>>();
            var points = 0;
            foreach (var polygon in polygons)
            {
                if (polygon.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var ring in polygon.EnumerateArray())
                {
                    if (ReadRing(ring, MaxPoints - points) is not { } read)
                    {
                        return null;
                    }

                    points += read.Count;
                    rings.Add(read);
                }
            }

            return rings.Count > 0 ? rings : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A Feature, the first Feature of a FeatureCollection, or a bare geometry.
    private static JsonElement? Geometry(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        switch (Text(root, "type"))
        {
            case "Feature":
                return root.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Object ? geometry : null;
            case "FeatureCollection":
                return root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array && features.GetArrayLength() > 0
                    ? Geometry(features[0])
                    : null;
            default:
                return root;
        }
    }

    private static List<GeoPoint>? ReadRing(JsonElement ring, int budget)
    {
        if (ring.ValueKind != JsonValueKind.Array || ring.GetArrayLength() < 3 || ring.GetArrayLength() > budget)
        {
            return null;
        }

        var points = new List<GeoPoint>(ring.GetArrayLength());
        foreach (var position in ring.EnumerateArray())
        {
            // GeoJSON writes longitude first.
            if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2
                || position[0].ValueKind != JsonValueKind.Number || !position[0].TryGetDouble(out var longitude)
                || position[1].ValueKind != JsonValueKind.Number || !position[1].TryGetDouble(out var latitude)
                || GeoPoint.TryCreate(latitude, longitude) is not { } point)
            {
                return null;
            }

            points.Add(point);
        }

        return points;
    }

    private static string? Text(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
