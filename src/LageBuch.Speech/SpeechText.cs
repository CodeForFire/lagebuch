using System.Globalization;
using System.Text.RegularExpressions;

namespace LageBuch.Speech;

/// <summary>
/// Turns Lagebuch's written German into German a speech engine reads correctly.
/// </summary>
/// <remarks>
/// <para>
/// Two levels, and the difference matters. The typed helpers -- <see cref="CallSign"/>,
/// <see cref="Strength"/>, <see cref="Floor"/> -- are used where the caller still holds structured
/// data, and they are always right. <see cref="Normalize"/> is the best-effort pass for text that
/// only exists rendered (an ETB entry, a banner), where the structure that would have said what a
/// number means is already gone.
/// </para>
/// <para>
/// The engine is left to read ordinary numerals: espeak-ng and Supertonic both say "240" and "45"
/// correctly in German, so spelling those out would only add ways to be wrong. What the engines
/// cannot guess is which digits are a <em>quantity</em> and which are an <em>identifier</em> --
/// "40/1" is a Funkrufname read "vier null, eins", while "0/1/8/9" is a Stärke read as four counts.
/// That distinction is this class's real job.
/// </para>
/// </remarks>
public static class SpeechText
{
    // Funk convention: digits go one at a time, and 2 is "zwo" so it cannot be heard as "drei".
    private static readonly string[] DigitWords =
        ["null", "eins", "zwo", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun"];

    private static readonly string[] MonthWords =
    [
        string.Empty, "Januar", "Februar", "März", "April", "Mai", "Juni",
        "Juli", "August", "September", "Oktober", "November", "Dezember",
    ];

    private static readonly Regex DigitGroup = new(@"\d+(?:/\d+)+", RegexOptions.Compiled);

    private static readonly Regex Timestamp =
        new(@"\b(\d{2})\.(\d{2})\.(\d{4})[ ,]+(\d{1,2}):(\d{2})\b", RegexOptions.Compiled);

    private static readonly Regex DateOnly = new(@"\b(\d{2})\.(\d{2})\.(\d{4})\b", RegexOptions.Compiled);

    private static readonly Regex ClockTime = new(@"\b(\d{1,2}):(\d{2})\b", RegexOptions.Compiled);

    private static readonly Regex FloorLabel = new(@"\b(\d{1,2})\.\s?([OU])G\b", RegexOptions.Compiled);

    private static readonly Regex FloorRange =
        new(@"(EG|\d{1,2}\.\s?[OU]G)\s*[–—-]\s*(EG|\d{1,2}\.\s?[OU]G)", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.:;])", RegexOptions.Compiled);

    private static readonly Regex RepeatedComma = new(@",(\s*,)+", RegexOptions.Compiled);

    /// <summary>
    /// Speaks a Funkrufname: the name part unchanged, every digit group one digit at a time.
    /// "Florian Musterstadt 40/1" becomes "Florian Musterstadt vier null, eins".
    /// </summary>
    public static string CallSign(string? callSign)
    {
        if (string.IsNullOrWhiteSpace(callSign))
        {
            return string.Empty;
        }

        var spoken = callSign
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => IsDigitGroup(token) ? SpeakDigitGroup(token) : Normalize(token));

        return Tidy(string.Join(' ', spoken));
    }

    /// <summary>
    /// Speaks a Stärke such as "0/1/8/9". Each part is a head count, not an identifier, so it stays
    /// a numeral the engine reads as a number -- "0/1/10/11" must be "zehn, elf", never "eins null".
    /// </summary>
    public static string Strength(string? strengthText) =>
        string.IsNullOrWhiteSpace(strengthText)
            ? string.Empty
            : Tidy(string.Join(", ", strengthText.Split('/', StringSplitOptions.TrimEntries)));

    /// <summary>
    /// Speaks a floor label: "EG" becomes "Erdgeschoss", "2. OG" becomes "Obergeschoss 2".
    /// </summary>
    public static string Floor(string? label) =>
        string.IsNullOrWhiteSpace(label) ? string.Empty : Tidy(ExpandFloors(label));

    /// <summary>
    /// Best-effort pass over already-rendered German. Prefer the typed helpers wherever the
    /// structured data is still to hand.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var s = text;

        // Ranges first: the en-dash in "EG–2. OG" means "bis", and would otherwise be eaten by the
        // generic separator rule below.
        s = FloorRange.Replace(s, "$1 bis $2");
        s = ExpandFloors(s);

        s = Timestamp.Replace(s, m => $"{SpeakDate(m, 1, 2, 3)}, {SpeakClock(m, 4, 5)}");
        s = DateOnly.Replace(s, m => SpeakDate(m, 1, 2, 3));
        s = ClockTime.Replace(s, m => SpeakClock(m, 1, 2));

        // A four-part group is a Stärke (ZF/GF/Mann/Gesamt); anything shorter is a Funkrufname.
        s = DigitGroup.Replace(
            s,
            m => m.Value.Count(c => c == '/') == 3 ? Strength(m.Value) : SpeakDigitGroup(m.Value));

        s = s.Replace("z. B.", "zum Beispiel", StringComparison.Ordinal);
        s = s.Replace("Whg.", "Wohnung", StringComparison.Ordinal);

        // "Atemschutztrupp(s)" -- the written plural marker is noise when spoken.
        s = s.Replace("(s)", string.Empty, StringComparison.Ordinal);

        s = ReplaceAbbreviations(s);

        s = s.Replace("→", " an ", StringComparison.Ordinal);
        s = s.Replace("„", string.Empty, StringComparison.Ordinal)
             .Replace("“", string.Empty, StringComparison.Ordinal);

        // The three separator glyphs all mean "short pause" once spoken.
        s = s.Replace("—", ",", StringComparison.Ordinal)
             .Replace("–", ",", StringComparison.Ordinal)
             .Replace("·", ",", StringComparison.Ordinal);

        return Tidy(s);
    }

    private static bool IsDigitGroup(string token) =>
        token.Length > 0 && token.All(c => char.IsAsciiDigit(c) || c == '/');

    private static string SpeakDigitGroup(string group) =>
        string.Join(
            ", ",
            group.Split('/', StringSplitOptions.RemoveEmptyEntries)
                 .Select(part => string.Join(' ', part.Select(d => DigitWords[d - '0']))));

    private static string SpeakDate(Match m, int day, int month, int year)
    {
        var d = int.Parse(m.Groups[day].Value, CultureInfo.InvariantCulture);
        var mo = int.Parse(m.Groups[month].Value, CultureInfo.InvariantCulture);
        var name = mo is >= 1 and <= 12 ? MonthWords[mo] : m.Groups[month].Value;
        return $"{d}. {name} {m.Groups[year].Value}";
    }

    // The hour loses its leading zero ("09" -> "9"); the minute keeps it, because "9 Uhr 5" and
    // "9 Uhr 05" are read differently and the written form is the one the operator saw.
    private static string SpeakClock(Match m, int hour, int minute) =>
        $"{int.Parse(m.Groups[hour].Value, CultureInfo.InvariantCulture)} Uhr {m.Groups[minute].Value}";

    private static string ExpandFloors(string text)
    {
        // "Obergeschoss 2", not "zweites Obergeschoss". A German ordinal is declined, and the
        // correct ending depends on what precedes it: "im zweiten Obergeschoss" after a
        // preposition, "zweites Obergeschoss" standing alone. The corpus has both -- "Rauch aus
        // dem 2. OG" and "Hauptstraße 12, 2. OG, Whg. 1" -- so any fixed ending is wrong half the
        // time. The uninflected form is never wrong, and the engine reads the numeral correctly.
        var s = FloorLabel.Replace(
            text,
            m =>
            {
                var storey = m.Groups[2].Value == "O" ? "Obergeschoss" : "Untergeschoss";
                return $"{storey} {m.Groups[1].Value}";
            });

        return Regex.Replace(s, @"\bEG\b", "Erdgeschoss");
    }

    private static string ReplaceAbbreviations(string text)
    {
        var s = text;
        foreach (var (abbreviation, spoken) in SpeechAbbreviations.Spelled
                     .Concat(SpeechAbbreviations.Expanded)
                     .OrderByDescending(pair => pair.Key.Length))
        {
            s = Regex.Replace(s, $@"(?<![\w-]){Regex.Escape(abbreviation)}(?![\w])", spoken);
        }

        return s;
    }

    private static string Tidy(string text)
    {
        var s = Whitespace.Replace(text, " ");
        s = SpaceBeforePunctuation.Replace(s, "$1");
        s = RepeatedComma.Replace(s, ",");
        return s.Trim().Trim(',').Trim();
    }
}
