namespace LageBuch.Speech.Tests;

// Normalize is the best-effort pass for text we only have rendered -- an ETB entry, a banner --
// where the structure that would have told us what a number means is already gone. The typed
// helpers (CallSign, Strength, Floor) are always preferred where the caller still has the data.
//
// Everything asserted here comes out of the real corpus: docs/samples/uebung.fwincident, the
// auto-generated sentences in LageBuch.Domain/Incident.cs, and ScbaViewModel's announcements.
public class SpeechTextNormalizeTests
{
    // German letter names, written out. Bare Latin letters ("I L S") left espeak-ng to guess, and it
    // both rushed them and drifted into English.
    [Theory]
    [InlineData("ILS", "Ih Ell Ess")]
    [InlineData("CSA-Trupp", "Tseh Ess Ah-Trupp")]
    [InlineData("LPA-Trupp", "Ell Peh Ah-Trupp")]
    [InlineData("AGT", "Ah Geh Teh")]
    [InlineData("DLK", "Deh Ell Kah")]
    [InlineData("ELW", "Eh Ell Weh")]
    public void An_abbreviation_spoken_as_letters_uses_the_German_letter_names(string input, string expected) =>
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
            "ACHTUNG: 2 Atemschutztrupp noch unter Peh Ah.",
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

    // A Funkrufname and a Stärke are read the same way, so Normalize needs no heuristic to tell
    // them apart -- which is the whole reason the four-group special case could be deleted.
    [Theory]
    [InlineData("Stärke 2/1/9/12", "Stärke zwo, 1, 9, 12")]
    [InlineData("Florian Musterstadt 40/1", "Florian Musterstadt 40, 1")]
    [InlineData("FFB 1/40/1", "Eff Eff Beh 1, 40, 1")]
    public void Every_slash_group_is_read_as_a_number(string input, string expected) =>
        Assert.Equal(expected, SpeechText.Normalize(input));

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
        Assert.Equal("Tseh Oh-Messung: 120 Peh Peh Emm", SpeechText.Normalize("CO-Messung: 120 ppm"));

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
            "Rückzugsalarm Florian Musterstadt 40, 1, Trupp 1 (Angriffstrupp): "
            + "Rückzugsdruck erreicht (45 bar)",
            SpeechText.Normalize(Raw));
    }
}
