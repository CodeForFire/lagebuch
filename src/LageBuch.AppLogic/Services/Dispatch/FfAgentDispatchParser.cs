using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// Reads the <c>&lt;einsatz version="1"&gt;</c> XML that FF-Agent's "Alarm Webhook und Mail" sends,
/// bare, as the HTML mail body, or as a whole saved mail (.eml, see <see cref="MimeMail"/>). In the
/// body the XML is HTML-escaped, every line ends in a <c>&lt;br&gt;</c>, and the Funkrufnamen inside
/// <c>&lt;gefordert&gt;</c> are separated by real <c>&lt;br&gt;</c> tags.
/// <para>
/// The content comes out of a folder anything can write to, so it is hostile input: it is
/// size-capped before it is looked at, no DTD is processed and nothing is resolved, and every field
/// is cut to <see cref="MaxFieldLength"/>. The caller (<c>mitteiler</c>) and the patient are never
/// read.
/// </para>
/// </summary>
public static partial class FfAgentDispatchParser
{
    /// <summary>The longest content looked at; a real alarm mail is around 20 000 characters.</summary>
    public const int MaxContentLength = 256_000;

    /// <summary>The longest any single field is kept.</summary>
    public const int MaxFieldLength = 500;

    /// <summary>The most Einsatzmittel kept from one alarm.</summary>
    public const int MaxUnits = 50;

    private const string EscapedStart = "&lt;einsatz";
    private const string EscapedEnd = "&lt;/einsatz&gt;";
    private const string RawStart = "<einsatz";
    private const string RawEnd = "</einsatz>";

    public static bool TryParse(string content, TimeZoneInfo zone, [MaybeNullWhen(false)] out DispatchAlarm alarm)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(zone);
        alarm = null;

        // A saved mail (.eml) carries the HTML body base64- or quoted-printable-encoded; anything
        // else is the body or the XML itself.
        if (content.Length > MaxContentLength
            || ExtractXml(MimeMail.TryGetText(content, out var mailText) ? mailText : content) is not { } xml)
        {
            return false;
        }

        XElement root;
        try
        {
            root = Load(xml);
        }
        catch (XmlException)
        {
            // Not well-formed, or a DTD it refuses to process: either way this file is no alarm,
            // and the scanner simply does not offer it.
            return false;
        }

        if (root.Name.LocalName != "einsatz" || Text(root.Element("kennung")?.Element("einsatznummer")) is not { } number)
        {
            return false;
        }

        var art = root.Element("einsatzart");
        var meldung = root.Element("meldung");
        var ort = root.Element("einsatzort");
        var mittel = root.Element("einsatzmittel");
        var (street, houseNumber) = SplitStreet(Text(ort?.Element("strasse")));
        var (postalCode, place) = SplitPlace(Text(ort?.Element("ort")));
        var wgs84 = ort?.Element("koordinaten")?.Element("wgs84");
        var units = Units(mittel?.Element("gefordert"));

        alarm = new DispatchAlarm(
            IncidentNumber: number,
            Keyword: Text(art?.Element("stichwort")),
            IncidentType: Text(art?.Element("einsatztyp")),
            Report: Text(meldung?.Element("meldebild")),
            Hint: Text(meldung?.Element("einsatzhinweis")),
            Organisation: Text(root.Element("organisation")),
            Street: street,
            HouseNumber: houseNumber,
            PostalCode: postalCode,
            Place: place,
            Building: Text(ort?.Element("objekt")),
            Location: GeoPoint.TryCreate(Coordinate(wgs84?.Attribute("lat")), Coordinate(wgs84?.Attribute("lng"))),
            AlarmedAt: AlarmTime((string?)root.Element("zeiten")?.Element("alarmierung")?.Attribute("iso"), zone),
            IsTest: Text(root.Element("test")) is not null,
            RequestedUnits: units.Count > 0 ? units : Units(mittel?.Element("fahrzeuge")));
        return true;
    }

    // The <einsatz> element alone, as XML text: unescaped out of the mail body when it came in one.
    private static string? ExtractXml(string content)
    {
        var escaped = Slice(content, EscapedStart, EscapedEnd);
        if (escaped is not null)
        {
            // Tags first, entities second: the XML's own angle brackets are still &lt; here, so
            // every real tag is the mail's -- a <br> between two Funkrufnamen, or markup a mail
            // client wrapped around the lines.
            return WebUtility.HtmlDecode(StripTags(escaped)).Replace(' ', ' ');
        }

        return Slice(content, RawStart, RawEnd);
    }

    // The mail's tags out, each <br> as a line break, in one pass. A regex over "<[^>]*>" rescans
    // the rest of the text from every '<' that never closes, which hostile content makes quadratic.
    // Once no '>' follows, nothing after it can be a tag, so the rest is kept as text.
    private static string StripTags(string html)
    {
        var text = new StringBuilder(html.Length);
        var at = 0;
        while (true)
        {
            var open = html.IndexOf('<', at);
            var close = open == -1 ? -1 : html.IndexOf('>', open);
            if (close == -1)
            {
                return text.Append(html, at, html.Length - at).ToString();
            }

            text.Append(html, at, open - at);
            if (WholeBreakTag().IsMatch(html.AsSpan(open, close - open + 1)))
            {
                text.Append('\n');
            }

            at = close + 1;
        }
    }

    private static string? Slice(string content, string start, string end)
    {
        var from = content.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
        {
            return null;
        }

        var to = content.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? null : content[from..(to + end.Length)];
    }

    private static XElement Load(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxContentLength,
        };
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, settings);
        return XElement.Load(reader);
    }

    // Trimmed, whitespace runs folded to one space, capped; null when nothing is left. CDATA in
    // these mails is padded with a space on either side.
    private static string? Text(XElement? element) => Clean(element?.Value);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var folded = Whitespace().Replace(value.Trim(), " ");
        return folded[..Math.Min(folded.Length, MaxFieldLength)];
    }

    // One Funkrufname per line; the bare XML keeps the <br> between them as text inside the CDATA.
    private static List<string> Units(XElement? element) =>
        BreakTag().Replace(element?.Value ?? string.Empty, "\n")
            .Split('\n')
            .Select(Clean)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxUnits)
            .ToList();

    // "Hauptstraße 12a" -> ("Hauptstraße", "12a"); a street without a trailing number stays whole.
    private static (string? Street, string? HouseNumber) SplitStreet(string? value)
    {
        if (value is null)
        {
            return (null, null);
        }

        var match = StreetWithNumber().Match(value);
        return match.Success ? (match.Groups["street"].Value, match.Groups["number"].Value) : (value, null);
    }

    // "82256 Fürstenfeldbruck ost" -> ("82256", "Fürstenfeldbruck ost").
    private static (string? PostalCode, string? Place) SplitPlace(string? value)
    {
        if (value is null)
        {
            return (null, null);
        }

        var match = PlaceWithPostalCode().Match(value);
        return match.Success ? (match.Groups["code"].Value, match.Groups["place"].Value) : (null, value);
    }

    private static double? Coordinate(XAttribute? attribute) =>
        double.TryParse((string?)attribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    // "2026-11-08 17:17" carries no offset: it is the Leitstelle's wall clock, which is this one's.
    private static DateTimeOffset? AlarmTime(string? iso, TimeZoneInfo zone)
    {
        if (!DateTime.TryParseExact(iso?.Trim(), ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        // At the very edges of the calendar the offset pushes the instant past what a
        // DateTimeOffset holds; such a time is no alarm time.
        var offset = zone.GetUtcOffset(local);
        var utcTicks = local.Ticks - offset.Ticks;
        return utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks
            ? null
            : new DateTimeOffset(local, offset);
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakTag();

    // A whole tag, '<' to '>', that is a line break.
    [GeneratedRegex(@"^<br\s*/?>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeBreakTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?<street>.+?)\s+(?<number>\d+\s?[a-zA-Z]?(?:\s?[-/]\s?\d+\s?[a-zA-Z]?)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex StreetWithNumber();

    [GeneratedRegex(@"^(?<code>\d{5})\s+(?<place>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceWithPostalCode();
}
