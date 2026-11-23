using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Reads a Kartenpaket's <c>water.geojson</c>: an RFC 7946 FeatureCollection of Points. The file
/// came from the network, so a feature that is no Point, lies off the globe or carries an
/// <c>emergency</c> tag outside FORMAT.md's four is skipped, and the values OSM holds are taken
/// as text and never trusted to be well-formed.
/// </summary>
public static partial class WaterPointsGeoJson
{
    /// <summary>A Landkreis holds a few thousand; a file larger than this is not a list of hydrants.</summary>
    public const long MaxFileBytes = 32 * 1024 * 1024;

    /// <summary>The most water points read from one pack.</summary>
    public const int MaxFeatures = 200_000;

    private const int MaxLabelPartLength = 24;

    private const int MaxDetailLength = 60;

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

        point = new WaterPoint(position, k, Label(k, properties))
        {
            Details = Details(properties),
            Symbol = Symbol(k, properties),
            NominalDiameter = Diameter(Text(properties, "fire_hydrant:diameter")),
            VolumeCubicMetres = Volume(Text(properties, "water_tank:volume")),
            Lage = Lage(Text(properties, "fire_hydrant:position")),
            Outlets = Outlets(Text(properties, "couplings:diameters", MaxDetailLength), Text(properties, "couplings")),
            IsPrivate = Text(properties, "access") is "private" or "no",
        };
        return true;
    }

    // The marker's glyph. A hydrant of no known Bauart, or a Steigrohr, gets the Unterflurhydrant's,
    // the commonest; its card still says only "Hydrant".
    private static WaterPointSymbol Symbol(WaterPointKind kind, JsonElement properties) => kind switch
    {
        WaterPointKind.Hydrant => Text(properties, "fire_hydrant:type") switch
        {
            "pillar" => WaterPointSymbol.Ueberflurhydrant,
            "wall" => WaterPointSymbol.Wandhydrant,
            _ => WaterPointSymbol.Unterflurhydrant,
        },
        WaterPointKind.Saugstelle => Text(properties, "water_source") == "groundwater" ? WaterPointSymbol.Loeschwasserbrunnen : WaterPointSymbol.Saugstelle,
        WaterPointKind.Loeschwasserbehaelter => WaterPointSymbol.Zisterne,
        _ => WaterPointSymbol.OffenesGewaesser,
    };

    // A tank's volume in m³ from "250 m³", "300m3" or a bare "100". OSM documents the key in m³,
    // but mappers often give litres: a bare figure beyond a thousand is no Löschwasserbehälter's m³
    // and is read as litres. Anything else reads as unknown; the card still shows it as it is.
    private static int? Volume(string? value)
    {
        if (value is null || VolumeValue().Match(value) is not { Success: true } match
            || !double.TryParse(match.Groups["number"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var cubicMetres = match.Groups["unit"].Success || amount <= 1000 ? amount : amount / 1000;
        var rounded = (int)Math.Round(cubicMetres, MidpointRounding.AwayFromZero);
        return rounded > 0 ? rounded : null;
    }

    // The well-known Lagen, short enough for the marker; anything written out stays on the card only.
    private static string? Lage(string? value) => value switch
    {
        "lane" or "street" => "Fahrbahn",
        "sidewalk" => "Gehweg",
        "green" => "Grünfläche",
        "parking_lot" => "Parkplatz",
        _ => null,
    };

    // The outlets by Storz size, largest first and counted, "A, 2×B", from OSM's ";"-list of
    // letters or millimetres ("B;B;A", "1A / 2B", "75;52"). A list in any other form is kept as
    // written. Without a list, only how many couplings OSM counts, "2 Abgänge".
    private static string? Outlets(string? diameters, string? couplings)
    {
        if (diameters is not null)
        {
            return OutletSizes(diameters);
        }

        return int.TryParse(couplings, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is > 0 and < 10
            ? count == 1 ? "1 Abgang" : $"{count} Abgänge"
            : null;
    }

    private static string? OutletSizes(string diameters)
    {
        var counts = new SortedDictionary<char, int>();
        foreach (var token in OutletSeparator().Split(diameters))
        {
            if (token.Length == 0)
            {
                continue;
            }

            var match = OutletValue().Match(token);
            char? size = !match.Success ? null
                : match.Groups["letter"].Success ? char.ToUpperInvariant(match.Groups["letter"].Value[0])
                : match.Groups["mm"].Value switch { "110" => 'A', "75" => 'B', "52" => 'C', "25" => 'D', _ => null };
            if (size is not { } s)
            {
                return diameters.Length > MaxLabelPartLength ? diameters[..MaxLabelPartLength] : diameters;
            }

            var n = match.Groups["n"].Success ? match.Groups["n"].Value[0] - '0' : 1;
            counts[s] = counts.GetValueOrDefault(s) + n;
        }

        return counts.Count == 0 ? null : string.Join(", ", counts.Select(c => c.Value == 1 ? $"{c.Key}" : $"{c.Value}×{c.Key}"));
    }

    // The Nennweite from "100", "DN 100", "DN100" or "100 mm"; anything else is unknown.
    private static int? Diameter(string? value) =>
        value is not null && DiameterValue().Match(value) is { Success: true } match
            && int.TryParse(match.Groups["dn"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var dn)
            ? dn
            : null;

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

    // FORMAT.md's allowlist, in the order a Gruppenführer reads a Hinweisschild. The well-known
    // OSM values are said in German; anything else is shown as OSM spells it.
    private static List<WaterPointDetail> Details(JsonElement properties)
    {
        string? Tag(string key) => Text(properties, key, MaxDetailLength);

        // A bare number gets its unit; "100 mm" or "DN100" is shown as it is.
        var diameter = Tag("fire_hydrant:diameter");
        var pressure = Tag("fire_hydrant:pressure");
        var osm = Tag("osm");
        (string Label, string? Value)[] rows =
        [
            (WaterPointDetail.TypeLabel, HydrantType(Tag("fire_hydrant:type"))),
            ("Durchmesser", diameter is not null && diameter.All(char.IsAsciiDigit) ? $"DN {diameter}" : diameter),
            ("Druck", pressure is not null && double.TryParse(pressure, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? $"{pressure} bar" : pressure),
            ("Förderleistung", Tag("flow_rate")),
            ("Volumen", Tag("water_tank:volume")),
            ("Kupplungen", Tag("couplings")),
            ("Kupplungsart", Tag("couplings:type")),
            ("Abgänge", Tag("couplings:diameters") is { } diameters ? OutletSizes(diameters) : null),
            ("Lage", HydrantPosition(Tag("fire_hydrant:position"))),
            ("Wasser aus", WaterSource(Tag("water_source"))),
            ("Nummer", Tag("ref")),
            ("Name", Tag("name")),
            ("Betreiber", Tag("operator")),
            ("Zugang", Access(Tag("access"))),
            ("Zuletzt geprüft (OSM)", Tag("check_date") ?? Tag("survey:date")),
            ("OSM", osm is not null && OsmId().IsMatch(osm) ? osm : null),
        ];
        var details = new List<WaterPointDetail>();
        foreach (var (label, value) in rows)
        {
            if (value is not null)
            {
                details.Add(new WaterPointDetail(label, value));
            }
        }

        return details;
    }

    private static string? HydrantType(string? value) => value switch
    {
        "underground" => "Unterflur",
        "pillar" => "Überflur",
        "wall" => "Wand",
        "pipe" => "Steigrohr",
        "unknown" => null,
        _ => value,
    };

    private static string? Access(string? value) => value switch
    {
        "private" => "privat",
        "no" => "kein",
        "customers" => "Kunden",
        "yes" or "permissive" => null,
        _ => value,
    };

    private static string? HydrantPosition(string? value) => value switch
    {
        "sidewalk" => "Gehweg",
        "lane" => "Fahrbahn",
        "parking_lot" => "Parkplatz",
        "green" => "Grünfläche",
        _ => value,
    };

    private static string? WaterSource(string? value) => value switch
    {
        "main" => "Trinkwassernetz",
        "pond" => "Teich",
        "river" => "Fluss",
        "stream" => "Bach",
        "lake" => "See",
        "groundwater" => "Grundwasser",
        "water_tank" => "Behälter",
        _ => value,
    };

    private static string? Text(JsonElement e, string property, int maxLength = MaxLabelPartLength)
    {
        if (!e.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = v.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > maxLength ? text[..maxLength] : text;
    }

    [GeneratedRegex(@"\A(?:node|way|relation)/[0-9]{1,15}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OsmId();

    [GeneratedRegex(@"\A(?<number>[0-9]{1,6}(?:[.,][0-9]{1,3})?)\s*(?<unit>m3|m³|cbm)?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VolumeValue();

    [GeneratedRegex(@"\s*[;,/]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex OutletSeparator();

    [GeneratedRegex(@"\A\s*(?:(?:(?<n>[1-9])\s*[x×]?\s*)?(?<letter>[A-D])|(?:(?<n>[1-9])\s*[x×]\s*)?(?<mm>[0-9]{2,3}))\s*\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OutletValue();

    [GeneratedRegex(@"\A(?:DN\s*)?(?<dn>[0-9]{2,4})(?:\s*mm)?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DiameterValue();
}
