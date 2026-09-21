namespace LageBuch.Speech.Tests;

// Normalize is the best-effort pass for text we only have rendered -- an ETB entry, a banner --
// where the structure that would have told us what a number means is already gone. The typed
// helpers (CallSign, Strength, Floor) are always preferred where the caller still has the data.
//
// Everything asserted here comes out of the real corpus: docs/samples/uebung.fwincident, the
// auto-generated sentences in LageBuch.Domain/Incident.cs, and ScbaViewModel's announcements.
public class SpeechTextNormalizeTests
{
    [Theory]
    [InlineData("ILS", "I L S")]
    [InlineData("CSA-Trupp", "C S A-Trupp")]
    [InlineData("LPA-Trupp", "L P A-Trupp")]
    [InlineData("AGT", "A G T")]
    [InlineData("DLK", "D L K")]
    [InlineData("ELW", "E L W")]
    public void An_abbreviation_that_is_spoken_as_letters_is_spaced_out(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Normalize(input));

    [Theory]
    [InlineData("EL", "Einsatzleiter")]
    [InlineData("EAL", "Einsatzabschnittsleiter")]
    [InlineData("ZF", "Zugführer")]
    [InlineData("GF", "Gruppenführer")]
    [InlineData("RD", "Rettungsdienst")]
    [InlineData("ETB", "Einsatztagebuch")]
    [InlineData("KdoW", "Kommandowagen")]
    public void An_abbreviation_with_a_natural_spoken_form_is_expanded(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Normalize(input));

    [Fact]
    public void An_abbreviation_is_only_matched_as_a_whole_word() =>
        Assert.Equal("Beispiel Gefahr", SpeechText.Normalize("Beispiel Gefahr"));

    // "unter PA" is how the under-air warning reads; "PA" must not be swallowed by "Pause".
    [Fact]
    public void The_under_air_warning_keeps_its_abbreviation() =>
        Assert.Equal(
            "ACHTUNG: 2 Atemschutztrupp noch unter P A.",
            SpeechText.Normalize("ACHTUNG: 2 Atemschutztrupp(s) noch unter PA."));

    [Theory]
    [InlineData("09:17", "9 Uhr 17")]
    [InlineData("12:45", "12 Uhr 45")]
    [InlineData("00:05", "0 Uhr 05")]
    public void A_clock_time_gains_the_word_Uhr(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Normalize(input));

    [Fact]
    public void A_full_timestamp_becomes_a_spoken_date() =>
        Assert.Equal("22. Juni 2026, 9 Uhr 17", SpeechText.Normalize("22.06.2026 09:17"));

    [Theory]
    [InlineData("EG", "Erdgeschoss")]
    [InlineData("2. OG", "Obergeschoss 2")]
    [InlineData("1. OG", "Obergeschoss 1")]
    [InlineData("1. UG", "Untergeschoss 1")]
    public void Floor_labels_are_spoken_in_full(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Normalize(input));

    // The en-dash in a floor range means "bis", not a pause -- and it must win over the generic
    // dash-to-comma rule below.
    [Fact]
    public void A_floor_range_is_read_as_a_range() =>
        Assert.Equal(
            "Erdgeschoss bis Obergeschoss 2",
            SpeechText.Normalize("EG–2. OG"));

    [Fact]
    public void An_apartment_is_spoken_in_full() =>
        Assert.Equal("Wohnung 1", SpeechText.Normalize("Whg. 1"));

    // A four-part group is a Stärke: each part is a count, so "12" stays "12", not "eins zwo".
    [Fact]
    public void A_four_part_group_is_read_as_counts() =>
        Assert.Equal("Stärke 2, 1, 9, 12", SpeechText.Normalize("Stärke 2/1/9/12"));

    // Anything shorter is a Funkrufname, and those go digit by digit.
    [Fact]
    public void A_shorter_group_is_read_as_a_call_sign() =>
        Assert.Equal(
            "Florian Musterstadt vier null, eins",
            SpeechText.Normalize("Florian Musterstadt 40/1"));

    [Theory]
    [InlineData("Müller — Schmidt", "Müller, Schmidt")]
    [InlineData("Trupp 1 · Angriffstrupp", "Trupp 1, Angriffstrupp")]
    public void Separator_glyphs_become_a_pause(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Normalize(input));

    [Fact]
    public void A_handover_arrow_is_spoken() =>
        Assert.Equal("Müller an Schmidt", SpeechText.Normalize("Müller → Schmidt"));

    [Fact]
    public void German_quotation_marks_are_dropped() =>
        Assert.Equal("zuvor: Lage erkundet", SpeechText.Normalize("zuvor: „Lage erkundet“"));

    [Fact]
    public void An_abbreviated_example_marker_is_expanded() =>
        Assert.Equal("zum Beispiel B3P", SpeechText.Normalize("z. B. B3P"));

    [Fact]
    public void A_co_reading_keeps_its_unit_pronounceable() =>
        Assert.Equal("C O-Messung: 120 p p m", SpeechText.Normalize("CO-Messung: 120 ppm"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_blank_output(string? input) =>
        Assert.Equal(string.Empty, SpeechText.Normalize(input));

    [Fact]
    public void Whitespace_is_collapsed_so_the_engine_does_not_stumble() =>
        Assert.Equal("Rückzug jetzt", SpeechText.Normalize("Rückzug    \n  jetzt"));

    // The full Rückzugsalarm, end to end -- this is the sentence the feature exists for.
    [Fact]
    public void The_retreat_alarm_reads_as_a_whole()
    {
        const string Raw =
            "Rückzugsalarm Florian Musterstadt 40/1 · Trupp 1 (Angriffstrupp): "
            + "Rückzugsdruck erreicht (45 bar)";

        Assert.Equal(
            "Rückzugsalarm Florian Musterstadt vier null, eins, Trupp 1 (Angriffstrupp): "
            + "Rückzugsdruck erreicht (45 bar)",
            SpeechText.Normalize(Raw));
    }
}
