using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Map;

/// <summary>
/// Finds addresses in a Kartenpaket's <c>addresses.sqlite</c> (FORMAT.md: one <c>addresses</c>
/// table, indexed on street and house number), offline. A query reads the way it is said:
/// "Hauptstr 12a Maisach" -- a street, a house number, and words that narrow the place.
/// "Str.", "Strasse" and "Straße" are one word, in the query and in the data.
/// </summary>
/// <remarks>
/// The file came from the network: it is opened read-only, every value is bound as a parameter,
/// a row with a position off the globe is skipped, and a file that is no address table reads as
/// nothing found rather than as a failure of the map.
/// </remarks>
public sealed partial class AddressIndex
{
    /// <summary>A query longer than this is no address.</summary>
    public const int MaxQueryLength = 100;

    // Hits kept after the street and place are compared word by word. The cap counts hits, not
    // rows read: the SQL pattern only narrows by the first letters of a word in the street, and a
    // short one such as "Am" matches thousands of other streets that sort before the one asked for.
    private const int CandidateLimit = 2000;

    private readonly string _path;

    public AddressIndex(string addressesFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressesFilePath);
        _path = addressesFilePath;
    }

    /// <summary>
    /// The addresses matching <paramref name="query"/>, best first, at most <paramref name="limit"/>:
    /// the exact house number before "12a" for "12", an address in the named place before one OSM
    /// gives no place at all, and otherwise a street named as typed before one holding the query as
    /// a later word, then by street and number. The street is found by any of its words, so
    /// "Druden" finds "Am Drudenbogen".
    /// Empty when nothing matches, the query names no street, or the file is no address table.
    /// </summary>
    public IReadOnlyList<AddressHit> Search(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (query.Length > MaxQueryLength || !TryParse(query, out var street, out var houseNumber, out var place))
        {
            return [];
        }

        // The SQL prefix is the street before its "-straße" ending, so "Allinger Straße" still finds
        // "Allinger Str."; the full comparison follows in Matches. A word may start the street or
        // follow a space or hyphen, which no index can narrow: a pack's table is read through, and
        // the streets starting with the query come first, so the candidate cap never drops one.
        street = street.Replace('-', ' ');
        var stem = StreetEnding().Replace(street, string.Empty);
        if (stem.Length < 2)
        {
            stem = street;
        }

        var (prefix, otherCase) = LikePrefixes(stem);
        var wantedStreet = Normalize(street);
        var hits = new List<(AddressHit Hit, int Rank)>();
        try
        {
            using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
            using var cmd = cn.CreateCommand();
            cmd.CommandText =
                "SELECT street, housenumber, postcode, city, suburb, lat, lon FROM addresses " +
                "WHERE (' ' || replace(street, '-', ' ') LIKE $word ESCAPE '\\' OR ' ' || replace(street, '-', ' ') LIKE $otherCaseWord ESCAPE '\\') " +
                "AND housenumber LIKE $number ESCAPE '\\' " +
                "ORDER BY (replace(street, '-', ' ') LIKE $street ESCAPE '\\' OR replace(street, '-', ' ') LIKE $otherCase ESCAPE '\\') DESC, street, length(housenumber), housenumber;";
            cmd.Parameters.AddWithValue("$street", EscapeLike(prefix) + "%");
            cmd.Parameters.AddWithValue("$otherCase", EscapeLike(otherCase) + "%");
            cmd.Parameters.AddWithValue("$word", "% " + EscapeLike(prefix) + "%");
            cmd.Parameters.AddWithValue("$otherCaseWord", "% " + EscapeLike(otherCase) + "%");
            cmd.Parameters.AddWithValue("$number", EscapeLike(houseNumber ?? string.Empty) + "%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Row(reader) is { } hit && Matches(hit, wantedStreet, place))
                {
                    var exactNumber = houseNumber is null || string.Equals(hit.HouseNumber, houseNumber, StringComparison.OrdinalIgnoreCase);
                    var placeUnknown = place.Count > 0 && hit is { City: "", Suburb: "", Postcode: "" };
                    hits.Add((hit, (exactNumber ? 0 : 2) + (placeUnknown ? 1 : 0)));
                    if (hits.Count == CandidateLimit)
                    {
                        break;
                    }
                }
            }
        }
        catch (SqliteException)
        {
            // Missing, damaged or not an address table: nothing is found, the map works on.
            return [];
        }

        // OrderBy is stable, so within a rank the SQL's order stays.
        return hits.OrderBy(h => h.Rank).Take(limit).Select(h => h.Hit).ToList();
    }

    /// <summary>Lower case, and every way of writing "Straße" as one: "Hauptstraße" and "Hauptstr." both read "hauptstr".</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.ToLower(CultureInfo.GetCultureInfo("de-DE"))
            .Replace("straße", "str", StringComparison.Ordinal)
            .Replace("strasse", "str", StringComparison.Ordinal)
            .Replace("str.", "str", StringComparison.Ordinal)
            .Trim();
    }

    // The words before the first one that starts with a digit are the street; that word is the
    // house number; the words after it narrow the place by postcode, Ort or Ortsteil.
    private static bool TryParse(string query, out string street, out string? houseNumber, out IReadOnlyList<string> place)
    {
        var words = query.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || char.IsAsciiDigit(words[0][0]))
        {
            street = string.Empty;
            houseNumber = null;
            place = [];
            return false;
        }

        var number = Array.FindIndex(words, 1, w => char.IsAsciiDigit(w[0]));

        street = string.Join(' ', number < 0 ? words : words[..number]);
        houseNumber = number < 0 ? null : words[number];
        place = number < 0 ? [] : words[(number + 1)..].Select(Normalize).ToList();
        return true;
    }

    // The street matches from the start of any of its words: "Am Drudenbogen" and
    // "Adalbert-Stifter-Straße" read " am drudenbogen" and " adalbert stifter str".
    // An address OSM tags with no place at all is not ruled out by the place words: unknown is no
    // mismatch, and its hit shows no Ort, so the Lagebuchführer sees what was matched.
    private static bool Matches(AddressHit hit, string street, IReadOnlyList<string> place) =>
        Words(hit.Street).Contains(" " + street, StringComparison.Ordinal)
        && (hit is { City: "", Suburb: "", Postcode: "" } || place.All(word =>
            Normalize(hit.City).StartsWith(word, StringComparison.Ordinal)
            || Normalize(hit.Suburb).StartsWith(word, StringComparison.Ordinal)
            || hit.Postcode.StartsWith(word, StringComparison.Ordinal)));

    private static string Words(string street) => " " + Normalize(street).Replace('-', ' ');

    // The columns are declared, but a hostile file can hold anything in them: a cell of the wrong
    // type costs that row.
    private static AddressHit? Row(SqliteDataReader reader)
    {
        if (reader.GetValue(0) is not string street || reader.GetValue(1) is not string houseNumber
            || reader.GetValue(5) is not double lat || reader.GetValue(6) is not double lon
            || GeoPoint.TryCreate(lat, lon) is not { } position)
        {
            return null;
        }

        return new AddressHit(street, houseNumber, reader.GetValue(2) as string ?? string.Empty, reader.GetValue(3) as string ?? string.Empty, reader.GetValue(4) as string ?? string.Empty, position);
    }

    // SQLite's LIKE folds the case of ASCII letters only, so the prefix stops before any other
    // letter, and the first letter is bound in both cases: "ölmühlweg" reads "Ölm" or "ölm".
    private static (string Prefix, string OtherCase) LikePrefixes(string stem)
    {
        var other = stem.AsSpan(1).IndexOfAnyExceptInRange('\0', '\x7f');
        var prefix = other < 0 ? stem : stem[..(other + 1)];
        var german = CultureInfo.GetCultureInfo("de-DE");
        return (char.ToUpper(prefix[0], german) + prefix[1..], char.ToLower(prefix[0], german) + prefix[1..]);
    }

    private static string EscapeLike(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '%' or '_' or '\\')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    [GeneratedRegex(@"(?:stra(?:ß|ss)e|str\.?)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreetEnding();
}
