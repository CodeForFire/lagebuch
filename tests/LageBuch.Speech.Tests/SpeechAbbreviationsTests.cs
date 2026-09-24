namespace LageBuch.Speech.Tests;

// These pin the *spellings*, because the spelling is the whole mechanism: the letter names are
// pseudo-words fed to a German voice, and one wrong grapheme makes an abbreviation unintelligible
// rather than merely odd.
//
// The expectations are not opinion. Each was checked against the same phonemizer the Piper voices
// use, and the IPA is quoted beside it:
//
//     packaging/speech/check-pronunciation.sh --letters
public class SpeechAbbreviationsTests
{
    [Theory]
    [InlineData('A', "Ah")] // ˈɑː
    [InlineData('C', "Zeh")] // tsˈeː
    [InlineData('E', "Eh")] // ˈeː
    [InlineData('F', "Eff")] // ˈɛf
    [InlineData('J', "Jott")] // jˈɔt
    [InlineData('Q', "Kuh")] // kˈuː
    [InlineData('S', "Ess")] // ˈɛs
    [InlineData('V', "Fau")] // fˈaʊ
    [InlineData('W', "Weh")] // vˈeː
    [InlineData('X', "Iks")] // ˈɪks
    [InlineData('Z', "Zett")] // tsˈɛt
    public void A_letter_is_spelled_as_its_German_name(char letter, string expected) =>
        Assert.Equal(expected, SpeechAbbreviations.GermanLetters[letter]);

    // The bug this table exists to prevent. "Tseh" looks like /tseː/ but German has no "ts" onset,
    // so espeak-ng reads it /tˈeːzˈeː/ -- two syllables, "te-se" -- and CSA came out "Te-Se Ess Ah".
    // The letter z already is the affricate, so C is "Zeh" and Z is "Zett".
    [Fact]
    public void No_letter_name_spells_the_affricate_as_ts()
    {
        var offenders = SpeechAbbreviations.GermanLetters
            .Where(pair => pair.Value.StartsWith("Ts", StringComparison.Ordinal))
            .Select(pair => $"{pair.Key} = {pair.Value}")
            .ToList();

        Assert.True(offenders.Count == 0, $"Write these with a leading 'Z': {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Every_letter_of_the_alphabet_has_a_name() =>
        Assert.All(
            Enumerable.Range('A', 26).Select(c => (char)c),
            c => Assert.True(SpeechAbbreviations.GermanLetters.ContainsKey(c), $"missing {c}"));

    [Theory]
    [InlineData("CSA", "Zeh Ess Ah")]
    [InlineData("ILS", "Ih Ell Ess")]
    [InlineData("LPA", "Ell Peh Ah")]
    [InlineData("AGT", "Ah Geh Teh")]
    public void An_abbreviation_spells_out_letter_by_letter(string input, string expected) =>
        Assert.Equal(expected, SpeechAbbreviations.SpellOut(input));

    // ppm is lower case in the corpus; the letter names are the same either way.
    [Fact]
    public void Case_does_not_change_the_letter_name() =>
        Assert.Equal("Peh Peh Emm", SpeechAbbreviations.SpellOut("ppm"));

    // A digit or a hyphen has no letter name and must survive rather than vanish.
    [Fact]
    public void A_character_with_no_letter_name_passes_through() =>
        Assert.Equal("Beh 3", SpeechAbbreviations.SpellOut("B3"));

    [Fact]
    public void Every_spelled_abbreviation_matches_its_letters() =>
        Assert.All(
            SpeechAbbreviations.Spelled,
            pair => Assert.Equal(SpeechAbbreviations.SpellOut(pair.Key), pair.Value));

    // The two tables must not disagree about the same abbreviation -- Normalize consults both, and
    // an entry in each would make which one wins depend on ordering.
    [Fact]
    public void No_abbreviation_is_both_spelled_and_expanded() =>
        Assert.Empty(SpeechAbbreviations.Spelled.Keys.Intersect(SpeechAbbreviations.Expanded.Keys));
}
