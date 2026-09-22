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
/// Ordinary numerals are left to the engine: espeak-ng and Supertonic both say "240" and "45"
/// correctly in German, so spelling them out would only add ways to be wrong. A slash-separated
/// group is split into its parts and each part left as a numeral -- "40/1" is "vierzig, eins", the
/// way a Funkrufname is announced, and "0/1/8/9" is "null, eins, acht, neun". Both are the same
/// operation, which is why <see cref="CallSign"/> and <see cref="Strength"/> share one
/// implementation.
/// </para>
/// </remarks>
public static class SpeechText
{
    // The one surviving piece of Funk convention: a group that is exactly "2" is said "zwo", so it
    // cannot be heard as "drei". It cannot apply inside a larger number -- 42 is "zweiundvierzig",
    // whose "zwei" is a syllable, not a digit -- so this is a whole-group rule, not a digit rule.
    private const string Zwo = "zwo";

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

    private static readonly Regex ListSeparator = new(@"\s*/\s*", RegexOptions.Compiled);

    // "FF Musterstadt (Florian Musterstadt 40/1)" -- a label followed by a parenthesised Funkrufname.
    private static readonly Regex LabelledCallSign =
        new(@"([^\s,;:()][^,;:()]*?)\s*\(([^)]*\d+/\d+[^)]*)\)", RegexOptions.Compiled);

    // ", Stärke 0/1/8/9," -- ZF/GF/Mann/Gesamt; the last group is the total.
    private static readonly Regex StrengthPhrase =
        new(@",?\s*Stärke\s+\d+/\d+/\d+/(\d+)\s*,?", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.:;])", RegexOptions.Compiled);

    private static readonly Regex RepeatedComma = new(@",(\s*,)+", RegexOptions.Compiled);

    private static readonly Regex RedundantStop = new(@"[,;.]?\s*\.(\s*\.)*", RegexOptions.Compiled);

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
            .Select(token => IsDigitGroup(token) ? SpeakGroups(token) : Normalize(token));

        return Tidy(string.Join(' ', spoken));
    }

    /// <summary>
    /// Speaks a Stärke such as "0/1/8/9" -- "null, eins, acht, neun". Each part is a head count, so
    /// it stays a numeral the engine reads as a number: "0/1/10/11" is "zehn, elf", never "eins
    /// null". Identical to <see cref="CallSign"/>'s handling of a group; kept as its own name
    /// because the call sites mean different things by it.
    /// </summary>
    public static string Strength(string? strengthText) =>
        string.IsNullOrWhiteSpace(strengthText) ? string.Empty : Tidy(SpeakGroups(strengthText));

    /// <summary>
    /// Speaks a floor label: "EG" becomes "Erdgeschoss", "2. OG" becomes "Obergeschoss 2".
    /// </summary>
    public static string Floor(string? label) =>
        string.IsNullOrWhiteSpace(label) ? string.Empty : Tidy(ExpandFloors(label));

    /// <summary>
    /// What a caller should use to speak an ETB line: <see cref="Condense"/> then
    /// <see cref="Normalize"/>.
    /// </summary>
    public static string Spoken(string? text) => Normalize(Condense(text));

    /// <summary>
    /// Shortens a written line for the ear. <b>Lossy on purpose</b>, and deliberately not part of
    /// <see cref="Normalize"/> so that a caller choosing to lose detail has to say so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ETB text itself never changes -- it is the record, and the PDF keeps every field. But
    /// listening has a constraint reading does not: you cannot go back over a sentence you just
    /// heard. Detail that helps on the page is noise in the ear, so the spoken rendering is allowed
    /// to be shorter than the written one.
    /// </para>
    /// <para>
    /// It works on the rendered string rather than on structured data, which means it also applies
    /// to entries written long before this existed -- by the time an ETB line is read back, the
    /// ForceUnit it came from is long gone.
    /// </para>
    /// </remarks>
    public static string Condense(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // "FF Musterstadt (Florian Musterstadt 40/1)" says Musterstadt twice. When the label's words
        // already appear inside the Funkrufname, the label is redundant and only the call sign is
        // spoken -- it is the identifier that tells two crews apart anyway. A label that shares
        // nothing with the call sign is kept, because then it is carrying information.
        var s = LabelledCallSign.Replace(
            text,
            m => SharesAWord(m.Groups[1].Value, m.Groups[2].Value) ? m.Groups[2].Value : m.Value);

        // "Stärke 0/1/8/9" is four numbers where one will do aloud: the last group is the total.
        // The comma before it becomes "mit" and the one after is dropped, so the clause reads
        // "... 40/1 mit 9 Mann davon 4 AGT" rather than a list of bare numerals.
        s = StrengthPhrase.Replace(s, m => $" mit {m.Groups[1].Value} Mann ");

        return Tidy(s);
    }

    /// <summary>
    /// Best-effort pass over already-rendered German, preserving every fact. Prefer the typed
    /// helpers wherever the structured data is still to hand.
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

        // A Funkrufname and a Stärke are read the same way -- each group as a number -- so nothing
        // here has to tell them apart.
        s = DigitGroup.Replace(s, m => SpeakGroups(m.Value));

        s = s.Replace("z. B.", "zum Beispiel", StringComparison.Ordinal);
        s = s.Replace("Whg.", "Wohnung", StringComparison.Ordinal);

        // "Atemschutztrupp(s)" -- the written plural marker is noise when spoken.
        s = s.Replace("(s)", string.Empty, StringComparison.Ordinal);

        s = ReplaceAbbreviations(s);

        s = s.Replace("→", " an ", StringComparison.Ordinal);
        s = s.Replace("„", string.Empty, StringComparison.Ordinal)
             .Replace("“", string.Empty, StringComparison.Ordinal);

        // Pauses, measured with espeak-ng on identical words (bytes of 22050 Hz audio, ~44100/s):
        //   none 81622 | "/" 81622 | "," 94154 | ":" 97916 | "." 101224
        //
        // A slash adds *nothing* -- "Müller / Schmidt / Huber" runs together exactly as if it were
        // written without any separator at all. Every digit group has already been consumed by
        // SpeakGroups above, so a slash surviving to here is always a list separator.
        s = ListSeparator.Replace(s, ", ");

        // A colon becomes a full stop. The extra 75 ms is not the point: a sentence boundary also
        // resets the intonation contour, and that reset is what "grouping" actually sounds like.
        // Ordered after ClockTime on purpose, or 09:17 would be split here.
        s = s.Replace(":", ".", StringComparison.Ordinal);

        // The em-dash separates whole clauses ("... 4 AGT — Status: Im Einsatz"), so it earns a full
        // stop too. The middle dot only ever joins a Funkrufname to its Trupp, where a full stop
        // would cut a single thought in half.
        s = s.Replace("—", ".", StringComparison.Ordinal)
             .Replace("–", ",", StringComparison.Ordinal)
             .Replace("·", ",", StringComparison.Ordinal);

        return Tidy(s);
    }

    // Case-insensitive because a label is written "FF Musterstadt" and the call sign
    // "Florian Musterstadt"; only words of real length count, or "an"/"in" would match anything.
    private static bool SharesAWord(string label, string callSign)
    {
        var words = callSign.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return label
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 3)
            .Any(w => words.Contains(w, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsDigitGroup(string token) =>
        token.Length > 0 && token.All(c => char.IsAsciiDigit(c) || c == '/');

    // "40/1" -> "vierzig, eins". The comma is a short pause between the groups, which keeps a
    // three-part Funkrufname from running together into one blur.
    private static string SpeakGroups(string groups) =>
        string.Join(
            ", ",
            groups.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(part => part == "2" ? Zwo : part));

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

        // Turning separators into full stops can leave a comma butting against one, or two stops in
        // a row where a clause ended in punctuation already.
        s = RedundantStop.Replace(s, ".");
        return s.Trim().Trim(',').Trim();
    }
}
