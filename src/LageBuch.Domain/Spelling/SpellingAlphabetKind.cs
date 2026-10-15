namespace LageBuch.Domain.Spelling;

/// <summary>The Buchstabiertafeln the Buchstabier-Helfer offers (#592).</summary>
public enum SpellingAlphabetKind
{
    /// <summary>
    /// The traditional German table (DIN 5009 up to 2022: Anton, Berta, Cäsar …), which BOS radio
    /// still uses, with its own words for Ä, Ch, Ö, Sch, ß and Ü.
    /// </summary>
    Traditional,

    /// <summary>
    /// DIN 5009:2022 with city names (Aachen, Berlin, Chemnitz …). It has no words of its own for
    /// Ch and Sch: letter combinations are spelled letter by letter.
    /// </summary>
    Din5009,

    /// <summary>The international ICAO/NATO alphabet (Alfa, Bravo, Charlie …).</summary>
    Nato,
}
