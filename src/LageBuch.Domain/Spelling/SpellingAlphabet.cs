namespace LageBuch.Domain.Spelling;

/// <summary>
/// Spells text with a Buchstabiertafel (#592), for street names and other proper nouns that have
/// to be spelled over the radio. Pure: no state, no culture lookups beyond upper-casing.
/// </summary>
public static class SpellingAlphabet
{
    private static readonly string[] Letters =
    [
        "A", "Ä", "B", "C", "Ch", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "Ö", "P", "Q", "R",
        "S", "Sch", "ß", "T", "U", "Ü", "V", "W", "X", "Y", "Z",
    ];

    // Case-insensitive, so "SCH", "sch" and "Sch" all find "Sch" and "ä" finds "Ä".
    private static readonly Dictionary<string, string> Traditional = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "Anton",
        ["Ä"] = "Ärger",
        ["B"] = "Berta",
        ["C"] = "Cäsar",
        ["Ch"] = "Charlotte",
        ["D"] = "Dora",
        ["E"] = "Emil",
        ["F"] = "Friedrich",
        ["G"] = "Gustav",
        ["H"] = "Heinrich",
        ["I"] = "Ida",
        ["J"] = "Julius",
        ["K"] = "Kaufmann",
        ["L"] = "Ludwig",
        ["M"] = "Martha",
        ["N"] = "Nordpol",
        ["O"] = "Otto",
        ["Ö"] = "Ökonom",
        ["P"] = "Paula",
        ["Q"] = "Quelle",
        ["R"] = "Richard",
        ["S"] = "Samuel",
        ["Sch"] = "Schule",
        ["ß"] = "Eszett",
        ["T"] = "Theodor",
        ["U"] = "Ulrich",
        ["Ü"] = "Übermut",
        ["V"] = "Viktor",
        ["W"] = "Wilhelm",
        ["X"] = "Xanthippe",
        ["Y"] = "Ypsilon",
        ["Z"] = "Zacharias",
    };

    private static readonly Dictionary<string, string> Din5009 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "Aachen",
        ["Ä"] = "Umlaut Aachen",
        ["B"] = "Berlin",
        ["C"] = "Chemnitz",
        ["D"] = "Düsseldorf",
        ["E"] = "Essen",
        ["F"] = "Frankfurt",
        ["G"] = "Goslar",
        ["H"] = "Hamburg",
        ["I"] = "Ingelheim",
        ["J"] = "Jena",
        ["K"] = "Köln",
        ["L"] = "Leipzig",
        ["M"] = "München",
        ["N"] = "Nürnberg",
        ["O"] = "Offenbach",
        ["Ö"] = "Umlaut Offenbach",
        ["P"] = "Potsdam",
        ["Q"] = "Quickborn",
        ["R"] = "Rostock",
        ["S"] = "Salzwedel",
        ["ß"] = "Eszett",
        ["T"] = "Tübingen",
        ["U"] = "Unna",
        ["Ü"] = "Umlaut Unna",
        ["V"] = "Völklingen",
        ["W"] = "Wuppertal",
        ["X"] = "Xanten",
        ["Y"] = "Ypsilon",
        ["Z"] = "Zwickau",
    };

    private static readonly Dictionary<string, string> Nato = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "Alfa",
        ["B"] = "Bravo",
        ["C"] = "Charlie",
        ["D"] = "Delta",
        ["E"] = "Echo",
        ["F"] = "Foxtrot",
        ["G"] = "Golf",
        ["H"] = "Hotel",
        ["I"] = "India",
        ["J"] = "Juliett",
        ["K"] = "Kilo",
        ["L"] = "Lima",
        ["M"] = "Mike",
        ["N"] = "November",
        ["O"] = "Oscar",
        ["P"] = "Papa",
        ["Q"] = "Quebec",
        ["R"] = "Romeo",
        ["S"] = "Sierra",
        ["T"] = "Tango",
        ["U"] = "Uniform",
        ["V"] = "Victor",
        ["W"] = "Whiskey",
        ["X"] = "X-ray",
        ["Y"] = "Yankee",
        ["Z"] = "Zulu",

        // The ICAO alphabet has no umlauts or ß: they are spelled the way they are transliterated.
        ["Ä"] = "Alfa Echo",
        ["Ö"] = "Oscar Echo",
        ["Ü"] = "Uniform Echo",
        ["ß"] = "Sierra Sierra",
    };

    // Digits are said, not spelled. German radio says "zwo" so it cannot be misheard as "drei".
    private static readonly string[] GermanDigits =
        ["null", "eins", "zwo", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun"];

    private static readonly string[] IcaoDigits =
        ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Niner"];

    /// <summary>
    /// The table for <paramref name="kind"/> in alphabetical order, the way it is printed on a
    /// Buchstabierkarte. Combinations a table has no word for are left out.
    /// </summary>
    public static IReadOnlyList<SpelledLetter> Table(SpellingAlphabetKind kind)
    {
        var words = WordsFor(kind);
        return Letters
            .Where(words.ContainsKey)
            .Select(letter => new SpelledLetter(letter, words[letter]))
            .ToList();
    }

    /// <summary>
    /// Spells <paramref name="text"/> letter by letter. Ch and Sch are one unit where the table has
    /// a word for them; digits are said as numbers; a run of whitespace becomes one word break
    /// (<see cref="SpelledLetter.IsWordBreak"/>); anything else stands as it is.
    /// </summary>
    public static IReadOnlyList<SpelledLetter> Spell(string? text, SpellingAlphabetKind kind)
    {
        var result = new List<SpelledLetter>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var words = WordsFor(kind);
        var digits = kind == SpellingAlphabetKind.Nato ? IcaoDigits : GermanDigits;
        var trimmed = text.Trim();
        var i = 0;
        while (i < trimmed.Length)
        {
            var c = trimmed[i];
            if (char.IsWhiteSpace(c))
            {
                // Stryker disable once Equality : the text is trimmed, so a run of whitespace always
                // ends on a letter before the end; i never reaches trimmed.Length here.
                while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                {
                    i++;
                }

                result.Add(new SpelledLetter(string.Empty, string.Empty));
                continue;
            }

            if (c is >= '0' and <= '9')
            {
                result.Add(new SpelledLetter(c.ToString(), digits[c - '0']));
                i++;
                continue;
            }

            var length = MatchLength(trimmed, i, words);
            var source = trimmed.Substring(i, Math.Max(length, 1));
            result.Add(length > 0
                ? new SpelledLetter(source, words[source])
                : new SpelledLetter(source, source));
            i += Math.Max(length, 1);
        }

        return result;
    }

    /// <summary>The spelling as it is read out: "Berta – Ludwig – Ulrich / eins – drei".</summary>
    public static string Format(IReadOnlyList<SpelledLetter> letters)
    {
        ArgumentNullException.ThrowIfNull(letters);
        var words = new List<string>();
        var current = new List<string>();
        foreach (var letter in letters)
        {
            if (letter.IsWordBreak)
            {
                Flush();
            }
            else
            {
                current.Add(letter.Word);
            }
        }

        Flush();
        return string.Join(" / ", words);

        void Flush()
        {
            if (current.Count > 0)
            {
                words.Add(string.Join(" – ", current));
                current.Clear();
            }
        }
    }

    private static Dictionary<string, string> WordsFor(SpellingAlphabetKind kind) => kind switch
    {
        SpellingAlphabetKind.Traditional => Traditional,
        SpellingAlphabetKind.Din5009 => Din5009,
        SpellingAlphabetKind.Nato => Nato,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unbekannte Buchstabiertafel."),
    };

    // The longest unit the table knows, starting at index: Sch before Ch before a single letter.
    // 0 when the table has no word for the character there.
    private static int MatchLength(string text, int index, Dictionary<string, string> words)
    {
        for (var length = 3; length >= 1; length--)
        {
            if (index + length <= text.Length && words.ContainsKey(text.Substring(index, length)))
            {
                return length;
            }
        }

        return 0;
    }
}
