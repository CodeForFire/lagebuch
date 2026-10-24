using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class TextSnippetMatcherTests
{
    private static readonly TextSnippet[] Snippets =
    {
        new("stl", "Stromleitung beschädigt, EVU alarmiert", "Sonderfall"),
        new("std", "Straße gesperrt, Umleitung über [ORT]", "Anfahrt"),
        new("lgb", "Lage bestätigt: [X]", "Erkundung"),
        new("abg", "Einsatz beendet, Rückfahrt", "Abgeschlossen"),
    };

    [Theory]
    [InlineData("::", 2, 0, "")]
    [InlineData("::st", 4, 0, "st")]
    [InlineData("Baum auf Fahrbahn ::st", 22, 18, "st")]
    [InlineData("Uhrzeit 14::st", 14, 10, "st")]
    [InlineData("::Stl", 5, 0, "Stl")]
    [InlineData("::grö2", 6, 0, "grö2")]
    public void A_double_colon_and_letters_before_the_caret_are_a_request(string text, int caret, int start, string query)
    {
        Assert.True(TextSnippetMatcher.TryGetQuery(text, caret, out var s, out var q));
        Assert.Equal(start, s);
        Assert.Equal(query, q);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(":", 1)]
    [InlineData("::st ", 5)] // a space ends it: prose again
    [InlineData(":::st", 5)] // the third colon is the prose's
    [InlineData("::st-x", 6)]
    [InlineData("Uhrzeit 14:30", 13)]
    [InlineData("::st", 1)] // caret inside the trigger
    public void Anything_else_is_not_a_request(string text, int caret)
    {
        Assert.False(TextSnippetMatcher.TryGetQuery(text, caret, out var start, out var query));
        Assert.Equal(-1, start);
        Assert.Equal(string.Empty, query);
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("::st", 5)] // a caret past the end, as a stale selection can report
    [InlineData("::st", -1)]
    public void A_missing_text_or_a_caret_outside_it_is_no_request_and_no_crash(string? text, int caret) =>
        Assert.False(TextSnippetMatcher.TryGetQuery(text, caret, out _, out _));

    [Fact]
    public void The_request_is_what_ends_at_the_caret_not_at_the_end_of_the_text()
    {
        // Typing ::st in the middle of an entry: the text after the caret is not part of it.
        Assert.True(TextSnippetMatcher.TryGetQuery("Lage ::st weiter", 9, out var start, out var query));
        Assert.Equal(5, start);
        Assert.Equal("st", query);
    }

    [Fact]
    public void Kuerzel_matches_come_first_then_text_and_kategorie_matches()
    {
        // "st" starts stl and std, and occurs in "bestätigt" (lgb); abg has it nowhere.
        Assert.Equal(new[] { "stl", "std", "lgb" }, TextSnippetMatcher.Match(Snippets, "st").Select(s => s.Shortcut));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal("stl", TextSnippetMatcher.Match(Snippets, "STL")[0].Shortcut);
        Assert.Equal("abg", Assert.Single(TextSnippetMatcher.Match(Snippets, "abgeschl")).Shortcut);
    }

    [Fact]
    public void An_empty_request_lists_from_the_top_capped_at_six()
    {
        var many = Enumerable.Range(0, 10).Select(i => new TextSnippet($"t{i}", $"Text {i}")).ToList();

        Assert.Equal(
            new[] { "t0", "t1", "t2", "t3", "t4", "t5" },
            TextSnippetMatcher.Match(many, string.Empty).Select(s => s.Shortcut));
    }

    [Fact]
    public void Apply_replaces_the_request_and_selects_the_first_gap()
    {
        var result = TextSnippetMatcher.Apply("Funk: ::std weiter", 6, 11, Snippets[1]);

        Assert.Equal("Funk: Straße gesperrt, Umleitung über [ORT]  weiter", result.Text);
        Assert.Equal("[ORT]", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void Apply_without_a_gap_leaves_the_caret_after_the_text_and_a_space()
    {
        var result = TextSnippetMatcher.Apply("::stl", 0, 5, Snippets[0]);

        Assert.Equal("Stromleitung beschädigt, EVU alarmiert ", result.Text);
        Assert.Equal(result.Text.Length, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
    }

    [Fact]
    public void Apply_selects_a_gap_at_the_very_start_of_the_text()
    {
        var result = TextSnippetMatcher.Apply("::kon", 0, 5, new TextSnippet("kon", "[X] kontrollieren"));

        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(3, result.SelectionLength);
    }

    [Fact]
    public void Apply_selects_only_a_closed_gap()
    {
        // A stray "]" before the gap and an unclosed "[" are not gaps.
        var closedLater = TextSnippetMatcher.Apply("::a", 0, 3, new TextSnippet("a", "Lage [X] bestätigt"));
        var unclosed = TextSnippetMatcher.Apply("::b", 0, 3, new TextSnippet("b", "Lage [X bestätigt"));

        Assert.Equal("[X]", closedLater.Text.Substring(closedLater.SelectionStart, closedLater.SelectionLength));
        Assert.Equal(unclosed.Text.Length, unclosed.SelectionStart);
        Assert.Equal(0, unclosed.SelectionLength);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(3, 2)]
    [InlineData(0, 9)]
    public void Apply_refuses_a_range_outside_the_text(int start, int caret) =>
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => TextSnippetMatcher.Apply("::stl", start, caret, Snippets[0]));

    [Fact]
    public void Apply_without_gap_selection_puts_the_caret_after_the_text()
    {
        // Android: the IME and a programmatic selection fight, so the gap is not selected there.
        var result = TextSnippetMatcher.Apply("::lgb", 0, 5, Snippets[2], selectGap: false);

        Assert.Equal("Lage bestätigt: [X] ", result.Text);
        Assert.Equal(result.Text.Length, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
    }
}
