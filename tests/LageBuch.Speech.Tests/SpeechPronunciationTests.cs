namespace LageBuch.Speech.Tests;

// Angriffstrupp came out as "Angriffschtrupp". The compound is Angriffs + Trupp -- the s closes the
// first element -- but the phonemizer sees "st" at a syllable onset and applies the ordinary German
// /ʃt/ rule, which it cannot know does not apply across a seam it cannot see.
//
// Half of what matters here is the words that must NOT change: the same rule is correct for
// Einsatzstelle, and over-applying it would break more than it fixed.
public class SpeechPronunciationTests
{
    [Theory]
    [InlineData("Angriffstrupp", "Angriffs Trupp")]
    [InlineData("Sicherheitstrupp", "Sicherheits Trupp")]
    [InlineData("angriffstrupp", "angriffs Trupp")]
    public void A_Fugen_s_before_Trupp_gets_a_word_boundary(string input, string expected) =>
        Assert.Equal(expected, SpeechPronunciation.Apply(input));

    [Theory]
    [InlineData("Einsatzstelle")] // the s really does belong to "Stelle"; /ʃt/ is right
    [InlineData("Einsatzstichwort")] // likewise
    [InlineData("Wassertrupp")] // no Fugen-s, nothing to split
    [InlineData("Schlauchtrupp")]
    [InlineData("Strahlenschutztrupp")] // "ztr", no s before the t
    [InlineData("CSA-Trupp")] // already separated
    [InlineData("LPA-Trupp")]
    [InlineData("Trupp 1")]
    public void A_word_that_is_already_right_is_left_alone(string input) =>
        Assert.Equal(input, SpeechPronunciation.Apply(input));

    [Fact]
    public void The_rule_applies_inside_a_sentence() =>
        Assert.Equal(
            "Trupp 1 (Angriffs Trupp) bereitgestellt",
            SpeechPronunciation.Apply("Trupp 1 (Angriffstrupp) bereitgestellt"));

    [Fact]
    public void Text_with_nothing_to_respell_survives() =>
        Assert.Equal("Rückmeldung an ILS fällig", SpeechPronunciation.Apply("Rückmeldung an ILS fällig"));

    [Fact]
    public void Normalize_applies_it() =>
        Assert.Equal(
            "Trupp 1 (Angriffs Trupp). Rückzugsdruck erreicht",
            SpeechText.Normalize("Trupp 1 (Angriffstrupp): Rückzugsdruck erreicht"));

    // The alarm the feature exists for, start to finish.
    [Fact]
    public void The_retreat_alarm_says_Angriffs_Trupp() =>
        Assert.Equal(
            "Rückzugsalarm Florian Musterstadt 40, 1, Trupp 1 (Angriffs Trupp). "
            + "Rückzugsdruck erreicht (45 bar)",
            SpeechText.Spoken(
                "Rückzugsalarm Florian Musterstadt 40/1 · Trupp 1 (Angriffstrupp): "
                + "Rückzugsdruck erreicht (45 bar)"));
}
