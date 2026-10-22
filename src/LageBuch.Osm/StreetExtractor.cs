using System.Text;
using System.Text.Json;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Osm;

/// <summary>
/// Turns an Overpass answer to <see cref="OverpassQuery"/> into a Gemeinde's Ortsteile and
/// Straßen (#461). Pure and deterministic: the same answer always yields the same list, in the
/// same order, so a regenerated Stammdaten file only changes where OpenStreetMap did.
/// </summary>
/// <remarks>
/// What OpenStreetMap holds decides every rule here (probed for Fürstenfeldbruck, 2026-10):
/// <list type="bullet">
/// <item>Street names come from named roads. <c>addr:street</c> on the addresses carries typos
/// ("Balduin-Helm-Straß"), so an address only confirms a name a road already has -- it is how a
/// footpath with houses on it gets in.</item>
/// <item>PLZ and Ort come from the addresses, which nearly all carry them.</item>
/// <item>Ortsteile exist only as <c>place</c> nodes: points, with no boundary to test a street
/// against. A street's Ortsteil is therefore taken only from <c>addr:suburb</c> or
/// <c>addr:hamlet</c> on its own addresses, and stays blank otherwise. Guessing it from the
/// nearest place node would put wrong Ortsteile into an Einsatz.</item>
/// <item>A Weiler is often addressed by its own name (<c>addr:place</c>, „Rothschwaig 1"), so
/// such a name is a street here too: it is what gets typed into STRASSE.</item>
/// </list>
/// </remarks>
public static class StreetExtractor
{
    private const string NoGemeinde = "Overpass kennt keine Gemeinde mit diesem Schlüssel.";

    // Named, but nowhere a vehicle is sent to: such a way only counts when an address names it.
    private static readonly HashSet<string> NotDrivable = new(StringComparer.Ordinal)
    {
        "path", "footway", "cycleway", "bridleway", "steps", "track", "corridor", "platform",
        "construction", "proposed", "elevator", "via_ferrata", "raceway",
    };

    // The place values an Ortsteil is tagged with. A town, city or municipality is the Gemeinde
    // itself or a neighbour, never a part of it.
    private static readonly HashSet<string> DistrictPlaces = new(StringComparer.Ordinal)
    {
        "suburb", "quarter", "neighbourhood", "village", "hamlet", "isolated_dwelling",
    };

    /// <exception cref="OverpassException">The answer is not JSON, reports a runtime error, or
    /// holds no Gemeinde -- the boundary the query asked for does not exist.</exception>
    public static StreetList Extract(string overpassJson)
    {
        ArgumentNullException.ThrowIfNull(overpassJson);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(overpassJson);
        }
        catch (JsonException e)
        {
            throw new OverpassException("Die Antwort von Overpass ist kein gültiges JSON.", e);
        }

        using (doc)
        {
            return Extract(doc.RootElement);
        }
    }

    private static StreetList Extract(JsonElement root)
    {
        // Overpass answers a query that ran out of time or memory with 200 and a remark, and
        // whatever it had collected so far. A partial street list must not pass for a whole one.
        if (Text(root, "remark") is { Length: > 0 } remark && remark.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            throw new OverpassException($"Overpass hat die Abfrage nicht vollständig beantwortet: {remark}");
        }

        string? gemeinde = null;
        var drivable = new HashSet<string>(StringComparer.Ordinal);
        var anyRoad = new HashSet<string>(StringComparer.Ordinal);
        var placeNodes = new List<string>();
        var addresses = new List<Address>();

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("elements", out var elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            throw new OverpassException(NoGemeinde);
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Text(tags, "name");
            if (Text(tags, "boundary") == "administrative")
            {
                gemeinde ??= name;
                continue;
            }

            if (Text(tags, "highway") is { Length: > 0 } highway && name.Length > 0)
            {
                anyRoad.Add(name);
                if (!NotDrivable.Contains(highway))
                {
                    drivable.Add(name);
                }
            }

            if (Text(element, "type") == "node" && DistrictPlaces.Contains(Text(tags, "place")) && name.Length > 0)
            {
                placeNodes.Add(name);
            }

            AddAddress(tags, addresses);
        }

        if (gemeinde is null)
        {
            throw new OverpassException(NoGemeinde);
        }

        return Build(gemeinde, drivable, anyRoad, placeNodes, addresses);
    }

    private static void AddAddress(JsonElement tags, List<Address> addresses)
    {
        var district = Text(tags, "addr:suburb");
        if (district.Length == 0)
        {
            district = Text(tags, "addr:hamlet");
        }

        var postalCode = Text(tags, "addr:postcode");
        var place = Text(tags, "addr:city");

        if (Text(tags, "addr:street") is { Length: > 0 } street)
        {
            addresses.Add(new Address(street, IsPlace: false, district, postalCode, place));
        }
        else if (Text(tags, "addr:place") is { Length: > 0 } hamlet)
        {
            addresses.Add(new Address(hamlet, IsPlace: true, district, postalCode, place));
        }
    }

    private static StreetList Build(
        string gemeinde,
        HashSet<string> drivable,
        HashSet<string> anyRoad,
        List<string> placeNodes,
        List<Address> addresses)
    {
        var byName = addresses.ToLookup(a => a.Name, StringComparer.Ordinal);

        // A road without addresses still lies in the Gemeinde's PLZ and Ort when it has only one.
        var fallback = new Parts(
            string.Empty,
            Single(addresses.Select(a => a.PostalCode)),
            Single(addresses.Select(a => a.Place)));

        var names = new HashSet<string>(drivable, StringComparer.Ordinal);
        names.UnionWith(addresses.Where(a => a.IsPlace || anyRoad.Contains(a.Name)).Select(a => a.Name));

        var streets = names
            .SelectMany(name => Rows(byName[name], fallback).Select(p => new Street(name, p.District, p.PostalCode, p.Place)))
            .OrderBy(s => SortKey(s.Name), StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => SortKey(s.District), StringComparer.Ordinal)
            .ThenBy(s => s.PostalCode, StringComparer.Ordinal)
            .ThenBy(s => SortKey(s.Place), StringComparer.Ordinal)
            .ToList();

        var districts = placeNodes
            .Concat(streets.Select(s => s.District))
            .Where(d => d.Length > 0 && !string.Equals(d, gemeinde, StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Order(StringComparer.Ordinal).First())
            .OrderBy(SortKey, StringComparer.Ordinal)
            .ThenBy(d => d, StringComparer.Ordinal)
            .ToList();

        return new StreetList(districts, streets);
    }

    /// <summary>
    /// One row per distinct Ortsteil/PLZ/Ort the street's addresses name, with a PLZ or Ort they
    /// leave out taken from the Gemeinde's only one. A combination that only leaves out what another
    /// one says -- an address without a suburb tag on a street whose other addresses have one -- is
    /// the same place described less fully, not a second one.
    /// </summary>
    private static IEnumerable<Parts> Rows(IEnumerable<Address> addresses, Parts fallback)
    {
        var parts = addresses
            .Select(a => new Parts(
                a.District,
                a.PostalCode.Length > 0 ? a.PostalCode : fallback.PostalCode,
                a.Place.Length > 0 ? a.Place : fallback.Place))
            .Distinct()
            .ToList();
        if (parts.Count == 0)
        {
            return [fallback];
        }

        return parts.Where(p => !parts.Any(other => other != p && p.IsCoveredBy(other)));
    }

    private static string Single(IEnumerable<string> values)
    {
        var distinct = values.Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : string.Empty;
    }

    // A tag's value trimmed ("Fürstenfeldbruck " is in the data), or "" when absent or not text.
    private static string Text(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.String
        && v.GetString() is { } text
            ? text.Trim()
            : string.Empty;

    // German order without depending on the machine's collation: ICU and NLS disagree on details,
    // and a list that reorders between a Linux and a Windows run is a diff nobody can review.
    private static string SortKey(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var c in value.ToUpperInvariant())
        {
            builder.Append(c switch
            {
                'Ä' => "A",
                'Ö' => "O",
                'Ü' => "U",
                'ß' or 'ẞ' => "SS",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    private sealed record Address(string Name, bool IsPlace, string District, string PostalCode, string Place);

    private sealed record Parts(string District, string PostalCode, string Place)
    {
        public bool IsCoveredBy(Parts other) =>
            Covers(District, other.District) && Covers(PostalCode, other.PostalCode) && Covers(Place, other.Place);

        private static bool Covers(string mine, string theirs) => mine.Length == 0 || mine == theirs;
    }
}
