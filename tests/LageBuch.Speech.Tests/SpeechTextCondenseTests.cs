namespace LageBuch.Speech.Tests;

// Condense is lossy on purpose. The ETB text is the record and never changes; this is only what
// gets *said*, and listening has a constraint reading does not -- you cannot go back over a
// sentence you just heard.
public class SpeechTextCondenseTests
{
    // "FF Musterstadt (Florian Musterstadt 40/1)" says Musterstadt twice, which is the complaint.
    [Fact]
    public void A_label_already_contained_in_the_call_sign_is_dropped() =>
        Assert.Equal(
            "Einheit aufgenommen: Florian Musterstadt 40/1",
            SpeechText.Condense("Einheit aufgenommen: FF Musterstadt (Florian Musterstadt 40/1)"));

    // A label that shares nothing with the call sign is carrying information, so it stays.
    [Fact]
    public void A_label_that_adds_information_survives() =>
        Assert.Equal(
            "Einheit aufgenommen: Bergwacht (Florian Musterstadt 40/1)",
            SpeechText.Condense("Einheit aufgenommen: Bergwacht (Florian Musterstadt 40/1)"));

    // Short words must not count as a match, or "an"/"im"/"FF" would collapse any label at all.
    [Fact]
    public void A_short_word_in_common_is_not_a_match() =>
        Assert.Equal(
            "FF Aich (Florian Puch 42/1)",
            SpeechText.Condense("FF Aich (Florian Puch 42/1)"));

    [Fact]
    public void A_parenthetical_that_is_not_a_call_sign_is_left_alone() =>
        Assert.Equal(
            "Trupp 1 (Angriffstrupp) bereitgestellt",
            SpeechText.Condense("Trupp 1 (Angriffstrupp) bereitgestellt"));

    // Stärke is ZF/GF/Mann/Gesamt. Aloud, the total is the number that matters.
    [Theory]
    [InlineData("Stärke 0/1/8/9", "mit 9 Mann")]
    [InlineData("Stärke 2/1/9/12", "mit 12 Mann")]
    public void A_Staerke_becomes_a_head_count(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Condense(input));

    [Fact]
    public void The_commas_around_a_Staerke_turn_into_a_connective() =>
        Assert.Equal(
            "Florian Musterstadt 40/1 mit 9 Mann davon 4 AGT",
            SpeechText.Condense("Florian Musterstadt 40/1, Stärke 0/1/8/9, davon 4 AGT"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_blank_output(string? input) =>
        Assert.Equal(string.Empty, SpeechText.Condense(input));

    [Fact]
    public void A_line_with_nothing_to_shorten_is_untouched() =>
        Assert.Equal(
            "Rückmeldung an ILS fällig",
            SpeechText.Condense("Rückmeldung an ILS fällig"));

    // The whole point, end to end: the written entry from Incident.cs as it reaches the ear.
    [Fact]
    public void The_unit_added_entry_reads_as_asked()
    {
        const string Written =
            "Einheit aufgenommen: FF Musterstadt (Florian Musterstadt 40/1), Stärke 0/1/8/9, "
            + "davon 4 AGT — Status: Im Einsatz";

        Assert.Equal(
            "Einheit aufgenommen: Florian Musterstadt 40/1 mit 9 Mann davon 4 AGT — Status: Im Einsatz",
            SpeechText.Condense(Written));

        Assert.Equal(
            "Einheit aufgenommen. Florian Musterstadt 40 1 mit 9 Mann davon 4 Ah Geh Teh. "
            + "Status. Im Einsatz",
            SpeechText.Spoken(Written));
    }

    // Spoken is Condense then Normalize, and Normalize alone must stay faithful -- a caller that
    // needs every fact still has one.
    [Fact]
    public void Normalize_alone_keeps_the_Staerke() =>
        Assert.Equal(
            "Stärke 0, 1, 8, 9",
            SpeechText.Normalize("Stärke 0/1/8/9"));
}
