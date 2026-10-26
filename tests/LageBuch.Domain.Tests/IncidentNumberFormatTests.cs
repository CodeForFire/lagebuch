using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class IncidentNumberFormatTests
{
    private static readonly DateTimeOffset Started = new(2026, 7, 15, 22, 17, 0, TimeSpan.FromHours(2));

    private static IncidentNumberFormat Parse(string template)
    {
        Assert.True(IncidentNumberFormat.TryParse(template, out var format));
        return format;
    }

    [Fact]
    public void Prefill_fills_in_the_date_and_leaves_the_running_number_as_a_gap_with_the_caret_on_it()
    {
        var (text, caret) = Parse("B 1.2 {JJMMTT} {NNN}").Prefill(Started);

        Assert.Equal("B 1.2 260715 ", text);
        Assert.Equal(text.Length, caret);
    }

    [Fact]
    public void Prefill_puts_the_caret_on_a_gap_in_the_middle()
    {
        var (text, caret) = Parse("{NNNN}/{JJJJ}").Prefill(Started);

        Assert.Equal("/2026", text);
        Assert.Equal(0, caret);
    }

    [Fact]
    public void Prefill_without_a_gap_puts_the_caret_at_the_end()
    {
        var (text, caret) = Parse("E-{TT}.{MM}.{JJJJ}").Prefill(Started);

        Assert.Equal("E-15.07.2026", text);
        Assert.Equal(text.Length, caret);
    }

    [Fact]
    public void Prefill_reads_the_english_Y_and_D_aliases()
    {
        Assert.Equal("B 260715 ", Parse("B {YYMMDD} {NNN}").Prefill(Started).Text);
    }

    [Fact]
    public void Prefill_takes_the_date_in_the_incidents_own_offset()
    {
        var lateUtc = new DateTimeOffset(2026, 7, 15, 23, 30, 0, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(2));

        Assert.Equal("260716 ", Parse("{JJMMTT} {NNN}").Prefill(lateUtc).Text);
    }

    [Theory]
    [InlineData("B 1.2 260715 123")]
    [InlineData("B 1.2 260714 7")]
    [InlineData("B 1.2  260715   1234")]
    public void Matches_a_number_of_the_right_shape(string number)
    {
        Assert.True(Parse("B 1.2 {JJMMTT} {NNN}").Matches(number));
    }

    [Theory]
    [InlineData("B 1.2 260715 ")]
    [InlineData("B 1.3 260715 123")]
    [InlineData("B 1.2 26071 123")]
    [InlineData("B 1.2 260715 12a")]
    [InlineData("1.2 260715 123")]
    [InlineData("B1.2 260715 123")]
    public void Does_not_match_a_number_of_another_shape(string number)
    {
        Assert.False(Parse("B 1.2 {JJMMTT} {NNN}").Matches(number));
    }

    [Fact]
    public void Matches_a_literal_that_itself_contains_digits_after_a_gap()
    {
        var format = Parse("{NNN}12{JJ}");

        Assert.True(format.Matches("4561226"));
        Assert.False(format.Matches("45626"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("B 1.2 260715")]
    [InlineData("B {JJ")]
    [InlineData("B {} {NNN}")]
    [InlineData("B {XYZ}")]
    [InlineData("B {JJN}")]
    [InlineData("B } {NNN}")]
    public void A_blank_or_malformed_template_is_no_format(string? template)
    {
        Assert.False(IncidentNumberFormat.TryParse(template, out _));
    }

    [Fact]
    public void The_template_is_kept_trimmed()
    {
        Assert.Equal("B {NNN}", Parse("  B {NNN} ").Template);
    }

    [Fact]
    public void A_long_number_against_many_gaps_completes()
    {
        var format = Parse(string.Concat(Enumerable.Repeat("{N}", 20)) + "x");

        Assert.False(format.Matches(new string('1', 5000)));
    }
}
