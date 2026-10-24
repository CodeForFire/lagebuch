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
}
