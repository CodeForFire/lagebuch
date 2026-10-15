using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The rules behind <c>::kürzel</c>: when a text field is asking for a Textbaustein, which ones
/// it offers, and what inserting one leaves in the field. Kept free of Avalonia so it is tested
/// as plain code; the view's behaviour only feeds it the text and the caret.
/// </summary>
public static class TextSnippetMatcher
{
    /// <summary>The trigger the Funker types before the Kürzel.</summary>
    public const string Trigger = "::";

    /// <summary>How many suggestions the list shows; more would cover the form it belongs to.</summary>
    public const int MaxSuggestions = 6;

    /// <summary>
    /// Whether the text just before <paramref name="caret"/> is <c>::</c> followed by letters and
    /// digits only -- the Funker is typing a Kürzel. <paramref name="start"/> is where the
    /// <c>::</c> begins and <paramref name="query"/> what follows it, as typed. A space, a
    /// punctuation mark or a third colon ends the request: the text is prose again.
    /// </summary>
    public static bool TryGetQuery(string? text, int caret, out int start, out string query)
    {
        start = -1;
        query = string.Empty;
        if (string.IsNullOrEmpty(text) || caret < Trigger.Length || caret > text.Length)
        {
            return false;
        }

        var i = caret;
        while (i > 0 && char.IsLetterOrDigit(text[i - 1]))
        {
            i--;
        }

        if (i < Trigger.Length || !string.Equals(text.Substring(i - Trigger.Length, Trigger.Length), Trigger, StringComparison.Ordinal))
        {
            return false;
        }

        // ":::" is not a request: the third colon belongs to the prose before it.
        if (i > Trigger.Length && text[i - Trigger.Length - 1] == ':')
        {
            return false;
        }

        start = i - Trigger.Length;
        query = text[i..caret];
        return true;
    }

    /// <summary>
    /// The Textbausteine for a query, at most <see cref="MaxSuggestions"/>: those whose Kürzel
    /// starts with it first, in Stammdaten order, then those whose Kategorie or text contains it.
    /// An empty query offers the list from the top, which is how a Funker browses it.
    /// </summary>
    public static IReadOnlyList<TextSnippet> Match(IEnumerable<TextSnippet> snippets, string query)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        ArgumentNullException.ThrowIfNull(query);
        var all = snippets as IReadOnlyList<TextSnippet> ?? snippets.ToList();
        var byShortcut = all.Where(s => s.Shortcut.StartsWith(query, StringComparison.OrdinalIgnoreCase));
        var byContent = query.Length == 0
            ? Enumerable.Empty<TextSnippet>()
            : all.Where(s => !s.Shortcut.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                && (s.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || s.Text.Contains(query, StringComparison.OrdinalIgnoreCase)));
        return byShortcut.Concat(byContent).Take(MaxSuggestions).ToList();
    }

    /// <summary>
    /// Replaces the <c>::kürzel</c> between <paramref name="start"/> and <paramref name="caret"/>
    /// with the Textbaustein and a space. The selection marks its first <c>[…]</c> gap, so typing
    /// fills it; without one, or when <paramref name="selectGap"/> is false, the caret simply
    /// follows the inserted text.
    /// </summary>
    public static SnippetInsertion Apply(string text, int start, int caret, TextSnippet snippet, bool selectGap = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(caret, start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, text.Length);

        var inserted = snippet.Text + " ";
        var result = string.Concat(text.AsSpan(0, start), inserted, text.AsSpan(caret));
        var end = start + inserted.Length;

        var open = snippet.Text.IndexOf('[', StringComparison.Ordinal);
        var close = open < 0 ? -1 : snippet.Text.IndexOf(']', open + 1);
        return selectGap && close > open
            ? new SnippetInsertion(result, start + open, close - open + 1)
            : new SnippetInsertion(result, end, 0);
    }
}

/// <summary>The field's text after an insertion, and what to select: a gap, or nothing at the caret.</summary>
public readonly record struct SnippetInsertion(string Text, int SelectionStart, int SelectionLength);
