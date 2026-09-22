namespace LageBuch.Speech.Tests;

// A Funkrufname is announced as numbers, not as a digit sequence: "Florian Musterstadt 40/1" is
// "vierzig eins", never "vier null, eins". The digit-by-digit Funk convention governs how a single
// number is *confirmed* on the radio, not how a callsign is announced.
//
// The groups run together with only a space. A comma costs about 210 ms per gap (measured), which
// turns a briskly-spoken identifier into dictation.
//
// ScbaViewModel announces the Funkrufname rather than the Truppnummer precisely because it is the
// identifier that tells two crews apart (#417), so mangling it defeats the point of speaking at all.
public class SpeechTextCallSignTests
{
    [Theory]
    [InlineData("40/1", "40 1")]
    [InlineData("41/1", "41 1")]
    [InlineData("11/1", "11 1")]
    [InlineData("30/1", "30 1")]
    [InlineData("1", "1")]
    public void Each_group_stays_one_number(string input, string expected) =>
        Assert.Equal(expected, SpeechText.CallSign(input));

    // The one surviving piece of Funk convention: a bare 2 is "zwo", so it cannot be heard as
    // "drei". It cannot reach inside a larger number.
    [Fact]
    public void A_standalone_two_is_spoken_as_zwo() =>
        Assert.Equal("zwo 1", SpeechText.CallSign("2/1"));

    [Fact]
    public void A_two_inside_a_larger_number_is_left_alone() =>
        Assert.Equal("42 1", SpeechText.CallSign("42/1"));

    [Fact]
    public void A_three_part_call_sign_keeps_all_three_groups() =>
        Assert.Equal("1 40 1", SpeechText.CallSign("1/40/1"));

    [Theory]
    [InlineData("Florian Musterstadt 40/1", "Florian Musterstadt 40 1")]
    [InlineData("Florian Musterdorf 42/1", "Florian Musterdorf 42 1")]
    [InlineData("Florian Musterstadt 1", "Florian Musterstadt 1")]
    public void The_name_part_is_left_alone(string input, string expected) =>
        Assert.Equal(expected, SpeechText.CallSign(input));

    // "FFB 1/40/1" -- the letters are an abbreviation, the digits are a call sign.
    [Fact]
    public void An_abbreviated_brigade_prefix_is_spelled_out() =>
        Assert.Equal("Eff Eff Beh 1 40 1", SpeechText.CallSign("FFB 1/40/1"));

    [Theory]
    [InlineData("Leitstelle")]
    [InlineData("ILS")]
    public void A_call_sign_that_is_really_a_name_survives(string input) =>
        Assert.Equal(SpeechText.Normalize(input), SpeechText.CallSign(input));

    // A Stärke is read exactly the same way -- each group a count -- which is why the two share an
    // implementation and Normalize needs no heuristic to tell them apart.
    [Theory]
    [InlineData("0/1/8/9", "0, 1, 8, 9")]
    [InlineData("2/1/9/12", "zwo, 1, 9, 12")]
    [InlineData("0/1/10/11", "0, 1, 10, 11")]
    public void A_Staerke_is_read_group_by_group(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Strength(input));

    // A hyphen groups just like a slash, and it must never survive: espeak speaks a bare hyphen
    // aloud as "Strich".
    [Theory]
    [InlineData("06/34-01", "06 34 01")]
    [InlineData("Florian München 06/34-01", "Florian München 06 34 01")]
    public void A_hyphen_groups_like_a_slash(string input, string expected) =>
        Assert.Equal(expected, SpeechText.CallSign(input));

    // Leading zeros are left to the engine, which already says "null sechs" for 06.
    [Fact]
    public void A_leading_zero_is_left_for_the_engine() =>
        Assert.Equal("Florian Fürstenfeldbruck zwo 40 1", SpeechText.CallSign("Florian Fürstenfeldbruck 2/40/1"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_blank_output(string input) =>
        Assert.Equal(string.Empty, SpeechText.CallSign(input));
}
