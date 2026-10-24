using LageBuch.Domain.Spelling;

namespace LageBuch.Domain.Tests;

public class SpellingAlphabetTests
{
    private static string Spell(string text, SpellingAlphabetKind kind) =>
        SpellingAlphabet.Format(SpellingAlphabet.Spell(text, kind));

    [Fact]
    public void Spells_a_street_traditionally_with_eszett_and_digits_said_as_numbers()
    {
        Assert.Equal(
            "Berta – Ludwig – Ulrich – Martha – Emil – Nordpol – Samuel – Theodor – Richard – Anton – Eszett – Emil / eins – drei",
            Spell("Blumenstraße 13", SpellingAlphabetKind.Traditional));
    }

    [Fact]
    public void Traditional_spells_sch_and_ch_as_one_unit_regardless_of_case()
    {
        Assert.Equal("Schule – Ulrich – Charlotte", Spell("SCHUCH", SpellingAlphabetKind.Traditional));
        Assert.Equal("Schule – Emil – Nordpol – Kaufmann", Spell("schenk", SpellingAlphabetKind.Traditional));
    }

    [Fact]
    public void Traditional_has_its_own_words_for_umlauts()
    {
        Assert.Equal("Ärger – Ökonom – Übermut", Spell("äöü", SpellingAlphabetKind.Traditional));
    }

    [Fact]
    public void Din_5009_spells_letter_combinations_letter_by_letter()
    {
        Assert.Equal(
            "Salzwedel – Chemnitz – Hamburg – Umlaut Unna – Leipzig – Essen",
            Spell("Schüle", SpellingAlphabetKind.Din5009));
    }

    [Fact]
    public void Din_5009_says_zwo_like_the_traditional_table()
    {
        Assert.Equal("zwo", Spell("2", SpellingAlphabetKind.Din5009));
    }

    [Fact]
    public void Nato_transliterates_umlauts_and_eszett_and_uses_icao_digits()
    {
        Assert.Equal(
            "Golf – Romeo – Uniform Echo – Sierra Sierra – Echo / Niner",
            Spell("Grüße 9", SpellingAlphabetKind.Nato));
    }

    [Fact]
    public void A_character_without_a_word_stands_as_it_is()
    {
        Assert.Equal("Anton – - – Berta – .", Spell("A-B.", SpellingAlphabetKind.Traditional));
    }

    [Fact]
    public void Blank_text_spells_to_nothing()
    {
        Assert.Empty(SpellingAlphabet.Spell("   ", SpellingAlphabetKind.Nato));
        Assert.Empty(SpellingAlphabet.Spell(null, SpellingAlphabetKind.Nato));
    }

    [Theory]
    [InlineData(SpellingAlphabetKind.Traditional, 32)]
    [InlineData(SpellingAlphabetKind.Din5009, 30)]
    [InlineData(SpellingAlphabetKind.Nato, 30)]
    public void Each_table_covers_every_letter_plus_its_own_additions(SpellingAlphabetKind kind, int rows)
    {
        var table = SpellingAlphabet.Table(kind);

        Assert.Equal(rows, table.Count);
        Assert.Equal("A", table[0].Source);
        Assert.Equal("Z", table[^1].Source);
    }

    // Every word of every table, in the order of a Buchstabierkarte: one wrong word is one a
    // listener mishears, so each is pinned rather than sampled.
    [Theory]
    [InlineData(
        SpellingAlphabetKind.Traditional,
        "A=Anton Ä=Ärger B=Berta C=Cäsar Ch=Charlotte D=Dora E=Emil F=Friedrich G=Gustav H=Heinrich I=Ida J=Julius K=Kaufmann L=Ludwig M=Martha N=Nordpol O=Otto Ö=Ökonom P=Paula Q=Quelle R=Richard S=Samuel Sch=Schule ß=Eszett T=Theodor U=Ulrich Ü=Übermut V=Viktor W=Wilhelm X=Xanthippe Y=Ypsilon Z=Zacharias")]
    [InlineData(
        SpellingAlphabetKind.Din5009,
        "A=Aachen Ä=Umlaut Aachen B=Berlin C=Chemnitz D=Düsseldorf E=Essen F=Frankfurt G=Goslar H=Hamburg I=Ingelheim J=Jena K=Köln L=Leipzig M=München N=Nürnberg O=Offenbach Ö=Umlaut Offenbach P=Potsdam Q=Quickborn R=Rostock S=Salzwedel ß=Eszett T=Tübingen U=Unna Ü=Umlaut Unna V=Völklingen W=Wuppertal X=Xanten Y=Ypsilon Z=Zwickau")]
    [InlineData(
        SpellingAlphabetKind.Nato,
        "A=Alfa Ä=Alfa Echo B=Bravo C=Charlie D=Delta E=Echo F=Foxtrot G=Golf H=Hotel I=India J=Juliett K=Kilo L=Lima M=Mike N=November O=Oscar Ö=Oscar Echo P=Papa Q=Quebec R=Romeo S=Sierra ß=Sierra Sierra T=Tango U=Uniform Ü=Uniform Echo V=Victor W=Whiskey X=X-ray Y=Yankee Z=Zulu")]
    public void Each_table_says_the_right_word_for_every_letter(SpellingAlphabetKind kind, string expected)
    {
        Assert.Equal(expected, string.Join(" ", SpellingAlphabet.Table(kind).Select(l => $"{l.Source}={l.Word}")));
    }

    [Theory]
    [InlineData(SpellingAlphabetKind.Traditional, "null – eins – zwo – drei – vier – fünf – sechs – sieben – acht – neun")]
    [InlineData(SpellingAlphabetKind.Din5009, "null – eins – zwo – drei – vier – fünf – sechs – sieben – acht – neun")]
    [InlineData(SpellingAlphabetKind.Nato, "Zero – One – Two – Three – Four – Five – Six – Seven – Eight – Niner")]
    public void Every_digit_is_said_as_its_number(SpellingAlphabetKind kind, string expected)
    {
        Assert.Equal(expected, Spell("0123456789", kind));
    }

    [Fact]
    public void A_run_of_whitespace_is_one_word_break_with_nothing_to_say()
    {
        var letters = SpellingAlphabet.Spell("A   B", SpellingAlphabetKind.Traditional);

        Assert.Equal(
            [new SpelledLetter("A", "Anton"), new SpelledLetter(string.Empty, string.Empty), new SpelledLetter("B", "Berta")],
            letters);
    }

    [Fact]
    public void A_word_break_with_no_word_before_it_adds_no_empty_word()
    {
        var letters = new[] { new SpelledLetter(string.Empty, string.Empty), new SpelledLetter("A", "Anton") };

        Assert.Equal("Anton", SpellingAlphabet.Format(letters));
    }

    [Fact]
    public void An_unknown_table_is_refused()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => SpellingAlphabet.Table((SpellingAlphabetKind)99));

        Assert.Contains("Unbekannte Buchstabiertafel.", error.Message, StringComparison.Ordinal);
    }
}
