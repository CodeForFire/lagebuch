using LageBuch.Domain;
using UglyToad.PdfPig;

namespace LageBuch.Documents.Tests;

/// <summary>
/// What a generated Bericht actually says, so a test can assert on its content rather than on its
/// size. Words are joined by single spaces in the order QuestPDF draws them: block by block, and
/// within a table row cell by cell, left to right.
/// </summary>
/// <remarks>
/// Drawing order rather than PdfPig's line-based word order, because the Bericht's narrow columns
/// wrap a timestamp onto two lines: read line by line, the second half lands among the next
/// cells' text, while drawn cell by cell "22.06.2026 09:30" stays one fragment. The price is that
/// a cell's sub-lines read before the cell to its right — "Lage erhalten bearbeitet … Huber".
/// </remarks>
internal static class PdfText
{
    /// <summary>The text of a Bericht rendering only <paramref name="sections"/>, plus the header
    /// and footer every page carries.</summary>
    public static string Of(Incident incident, DateTimeOffset asOf, IncidentPdfSections sections) =>
        Of(IncidentPdf.Generate(incident, asOf, sections: sections));

    public static string Of(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(page => InDrawingOrder(page.Letters)));
    }

    // A new word starts at a space, on another line (a wrapped line, the next cell) and at a gap,
    // either way, wider than a fraction of the font size (the padding between two cells on one
    // line). Kerning pulls a pair like "Tr" slightly together, which must not split the word.
    private static string InDrawingOrder(IReadOnlyList<UglyToad.PdfPig.Content.Letter> letters)
    {
        var text = new System.Text.StringBuilder();
        UglyToad.PdfPig.Content.Letter? previous = null;
        foreach (var letter in letters)
        {
            if (string.IsNullOrWhiteSpace(letter.Value))
            {
                previous = null;
                AppendSpace(text);
                continue;
            }

            if (previous is not null)
            {
                var gap = letter.StartBaseLine.X - previous.EndBaseLine.X;
                var sameLine = Math.Abs(letter.StartBaseLine.Y - previous.StartBaseLine.Y) < 0.5;
                if (!sameLine || Math.Abs(gap) > previous.PointSize * 0.2)
                {
                    AppendSpace(text);
                }
            }

            text.Append(letter.Value);
            previous = letter;
        }

        return text.ToString().Trim();
    }

    private static void AppendSpace(System.Text.StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != ' ')
        {
            text.Append(' ');
        }
    }

    /// <summary>
    /// Every printed line of every page, its words left to right. Where <see cref="Of(byte[])"/>
    /// shows what a table says, this shows how it is laid out: a row whose cells all share one
    /// line has as many columns as it has cells, while a table missing a column definition pushes
    /// its last cells onto the next line.
    /// </summary>
    public static IReadOnlyList<string> LinesOf(Incident incident, DateTimeOffset asOf, IncidentPdfSections sections)
    {
        using var document = PdfDocument.Open(IncidentPdf.Generate(incident, asOf, sections: sections));
        return document.GetPages()
            .SelectMany(page => page.GetWords()
                .GroupBy(word => Math.Round(word.Letters[0].StartBaseLine.Y))
                .OrderByDescending(line => line.Key)
                .Select(line => string.Join(' ', line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text))))
            .ToList();
    }

    /// <summary>How many images each page draws, in page order.</summary>
    public static IReadOnlyList<int> ImagesPerPage(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => page.GetImages().Count()).ToList();
    }

    /// <summary>
    /// Asserts each fragment occurs in <paramref name="text"/>, every one after the end of the one
    /// before it.
    /// </summary>
    public static void AssertInOrder(string text, params string[] fragments)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fragments);
        var from = 0;
        foreach (var fragment in fragments)
        {
            var at = text.IndexOf(fragment, from, StringComparison.Ordinal);
            var problem = text.Contains(fragment, StringComparison.Ordinal)
                ? "occurs, but not after the fragment before it"
                : "does not occur";
            Assert.True(at >= 0, $"\"{fragment}\" {problem}.\n{text}");
            from = at + fragment.Length;
        }
    }

    /// <summary>How often <paramref name="fragment"/> occurs in <paramref name="text"/>.</summary>
    public static int Count(string text, string fragment)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        for (var at = text.IndexOf(fragment, StringComparison.Ordinal); at >= 0; at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
