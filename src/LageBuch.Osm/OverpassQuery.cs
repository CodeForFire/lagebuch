using System.Globalization;

namespace LageBuch.Osm;

/// <summary>
/// The Overpass QL that fetches everything <see cref="StreetExtractor"/> needs for one Gemeinde, in
/// a single request: the boundary itself (for its name), every named road, every address and every
/// Ortsteil-like place node inside it. Only tags are returned, never geometry -- nothing here is
/// placed on a map, which keeps a Gemeinde's response to a few megabytes.
/// </summary>
public static class OverpassQuery
{
    /// <summary>
    /// The query for the Gemeinde with this amtlicher Gemeindeschlüssel, the eight digits OSM tags
    /// German municipalities with (<c>de:amtlicher_gemeindeschluessel</c>), e.g. 09179121 for
    /// Fürstenfeldbruck. Unlike a name it is unambiguous: there are several Gemeinden called Puch.
    /// </summary>
    /// <exception cref="ArgumentException">The key is not exactly eight digits.</exception>
    public static string ForMunicipalityKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // It is interpolated into the query text, so nothing but digits may reach it.
        if (key.Length != 8 || !key.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Der Gemeindeschlüssel hat genau acht Ziffern.", nameof(key));
        }

        return Build($"""rel["boundary"="administrative"]["de:amtlicher_gemeindeschluessel"="{key}"]""");
    }

    /// <summary>
    /// The query for the administrative boundary relation with this OSM id -- for a Gemeinde
    /// outside Germany, or one whose key is not tagged.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is not positive.</exception>
    public static string ForRelation(long relationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(relationId);
        return Build(string.Create(CultureInfo.InvariantCulture, $"rel({relationId})"));
    }

    private static string Build(string boundary) => $$"""
        [out:json][timeout:180];
        {{boundary}}->.boundary;
        .boundary map_to_area->.gemeinde;
        (
          .boundary;
          way["highway"]["name"](area.gemeinde);
          nwr["addr:street"](area.gemeinde);
          nwr["addr:place"](area.gemeinde);
          node["place"~"^(suburb|quarter|neighbourhood|village|hamlet|isolated_dwelling)$"](area.gemeinde);
        );
        out tags;
        """;
}
