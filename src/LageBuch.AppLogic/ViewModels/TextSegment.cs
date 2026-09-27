namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One run of a text that is either part of a search hit or not, so a view can draw the hit
/// differently without knowing how the search matched.
/// </summary>
public sealed record TextSegment(string Text, bool IsMatch)
{
    /// <summary>
    /// Splits <paramref name="text"/> into runs, marking every occurrence of every term.
    /// </summary>
    /// <remarks>
    /// Matches the way <see cref="ContactsViewModel"/> filters: ordinal and case-insensitive.
    /// Overlapping and adjacent hits merge into one run, so "gefahr gefahrgut" marks "Gefahrgut"
    /// once rather than twice or in fragments. A blank text yields no runs at all, so the view
    /// can bind its visibility to the count.
    /// </remarks>
    public static IReadOnlyList<TextSegment> Highlight(string? text, IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<TextSegment>();
        }

        var marked = new bool[text.Length];
        foreach (var term in terms.Where(t => t.Length > 0))
        {
            for (var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                 at >= 0;
                 at = text.IndexOf(term, at + 1, StringComparison.OrdinalIgnoreCase))
            {
                Array.Fill(marked, true, at, term.Length);
            }
        }

        var segments = new List<TextSegment>();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || marked[i] != marked[start])
            {
                segments.Add(new TextSegment(text[start..i], marked[start]));
                start = i;
            }
        }

        return segments;
    }
}
