namespace LageBuch.Speech.Tests;

// Funkrufnamen are read digit by digit, the way they go out over the radio -- "vier null, eins",
// never "vierzig Schrägstrich eins". ScbaViewModel announces the Funkrufname rather than the
// Truppnummer precisely because it is the identifier that tells two crews apart (#417), so
// mangling it defeats the whole point of speaking the alarm.
public class SpeechTextCallSignTests
{
    [Theory]
    [InlineData("40/1", "vier null, eins")]
    [InlineData("41/1", "vier eins, eins")]
    [InlineData("11/1", "eins eins, eins")]
    [InlineData("30/1", "drei null, eins")]
    [InlineData("1", "eins")]
    public void Digit_groups_are_read_one_digit_at_a_time(string input, string expected) =>
        Assert.Equal(expected, SpeechText.CallSign(input));

    [Fact]
    public void Two_is_spoken_as_zwo_the_way_it_is_on_the_radio() =>
        Assert.Equal("vier zwo, eins", SpeechText.CallSign("42/1"));

    [Fact]
    public void A_three_part_call_sign_keeps_all_three_groups() =>
        Assert.Equal("eins, vier null, eins", SpeechText.CallSign("1/40/1"));

    [Theory]
    [InlineData("Florian Musterstadt 40/1", "Florian Musterstadt vier null, eins")]
    [InlineData("Florian Musterdorf 42/1", "Florian Musterdorf vier zwo, eins")]
    [InlineData("Florian Musterstadt 1", "Florian Musterstadt eins")]
    public void The_name_part_is_left_alone(string input, string expected) =>
        Assert.Equal(expected, SpeechText.CallSign(input));

    // "FFB 1/40/1" -- the letters are an abbreviation, the digits are a call sign.
    [Fact]
    public void An_abbreviated_brigade_prefix_is_spelled_out() =>
        Assert.Equal("F F B eins, vier null, eins", SpeechText.CallSign("FFB 1/40/1"));

    [Theory]
    [InlineData("Leitstelle")]
    [InlineData("ILS")]
    public void A_call_sign_that_is_really_a_name_survives(string input) =>
        Assert.Equal(SpeechText.Normalize(input), SpeechText.CallSign(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_blank_output(string input) =>
        Assert.Equal(string.Empty, SpeechText.CallSign(input));
}
