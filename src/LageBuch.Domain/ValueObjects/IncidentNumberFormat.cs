using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace LageBuch.Domain.ValueObjects;

/// <summary>
/// The shape of a brigade's Einsatznummer (#462), from a Stammdaten template such as
/// <c>B 1.2 {JJMMTT} {NNN}</c>. Inside braces, <c>JJJJ</c>/<c>JJ</c>, <c>MM</c> and <c>TT</c> are the
/// incident's date (<c>Y</c> and <c>D</c> spell year and day too), and a run of <c>N</c> is the
/// running number the Leitstelle assigns; everything else is literal.
/// <para>
/// It only helps: <see cref="Prefill"/> types the known parts for the Lagebuchführer and leaves the
/// running number as a gap, and <see cref="Matches"/> backs a hint, never a refusal. The template
/// arrives from a Stammdaten import or a sync host, so the matcher is hand-rolled and linear in the
/// number's length rather than a regular expression built from it.
/// </para>
/// </summary>
public sealed record IncidentNumberFormat
{
    private readonly IReadOnlyList<Part> _parts;

    private IncidentNumberFormat(string template, IReadOnlyList<Part> parts)
    {
        Template = template;
        _parts = parts;
    }

    /// <summary>The template as configured, trimmed.</summary>
    public string Template { get; }

    // Two formats parsed from the same template are the same format; the parts are derived from it.
    public bool Equals(IncidentNumberFormat? other) =>
        other is not null && string.Equals(Template, other.Template, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Template);

    /// <summary>
    /// False for a blank template, for unbalanced or empty braces, for letters other than the ones
    /// above, and for a template with no placeholder at all -- there is nothing to fill in then.
    /// </summary>
    public static bool TryParse(string? template, [MaybeNullWhen(false)] out IncidentNumberFormat format)
    {
        format = null;
        var trimmed = template?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var parts = new List<Part>();
        var literal = new StringBuilder();
        var hasPlaceholder = false;
        var i = 0;
        while (i < trimmed.Length)
        {
            var c = trimmed[i];
            if (c == '}')
            {
                return false;
            }

            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }

            var end = trimmed.IndexOf('}', i + 1);
            if (end < 0 || !TryParsePlaceholder(trimmed[(i + 1)..end], parts, literal))
            {
                return false;
            }

            hasPlaceholder = true;
            i = end + 1;
        }

        if (!hasPlaceholder)
        {
            return false;
        }

        FlushLiteral(parts, literal);
        format = new IncidentNumberFormat(trimmed, parts);
        return true;
    }

    /// <summary>
    /// The template with the date filled in from <paramref name="startedAt"/> (in its own offset) and
    /// the first gap left empty, with the caret where the gap was -- or at the end without one. Later
    /// gaps are left empty too.
    /// </summary>
    public (string Text, int CaretIndex) Prefill(DateTimeOffset startedAt)
    {
        var text = new StringBuilder();
        int? caret = null;
        foreach (var part in _parts)
        {
            switch (part)
            {
                case LiteralPart l:
                    text.Append(l.Text);
                    break;
                case DatePart d:
                    text.Append(startedAt.ToString(d.Format, CultureInfo.InvariantCulture));
                    break;
                case GapPart:
                    caret ??= text.Length;
                    break;
            }
        }

        return (text.ToString(), caret ?? text.Length);
    }

    /// <summary>
    /// Whether <paramref name="number"/> has the template's shape: literals exactly (any run of
    /// whitespace standing for one or more), each date field as that many digits -- not the
    /// incident's actual date, which an Einsatz past midnight would fail -- and each gap as one or
    /// more digits.
    /// </summary>
    public bool Matches(string number)
    {
        ArgumentNullException.ThrowIfNull(number);

        // reachable[p]: the parts so far can consume exactly number[..p].
        var reachable = new bool[number.Length + 1];
        reachable[0] = true;
        foreach (var part in _parts)
        {
            reachable = part switch
            {
                LiteralPart { IsWhitespace: true } => Run(number, reachable, char.IsWhiteSpace),
                LiteralPart l => Exact(number, reachable, l.Text),
                DatePart d => Digits(number, reachable, d.Format.Length),
                _ => Run(number, reachable, char.IsAsciiDigit),
            };
        }

        return reachable[number.Length];
    }

    private static bool TryParsePlaceholder(string body, List<Part> parts, StringBuilder literal)
    {
        if (body.Length == 0)
        {
            return false;
        }

        if (body.All(c => c == 'N'))
        {
            FlushLiteral(parts, literal);
            parts.Add(new GapPart());
            return true;
        }

        var dateParts = new List<Part>();
        var i = 0;
        while (i < body.Length)
        {
            var rest = body[i..];
            var (format, length) = rest switch
            {
                _ when rest.StartsWith("JJJJ", StringComparison.Ordinal) || rest.StartsWith("YYYY", StringComparison.Ordinal) => ("yyyy", 4),
                _ when rest.StartsWith("JJ", StringComparison.Ordinal) || rest.StartsWith("YY", StringComparison.Ordinal) => ("yy", 2),
                _ when rest.StartsWith("MM", StringComparison.Ordinal) => ("MM", 2),
                _ when rest.StartsWith("TT", StringComparison.Ordinal) || rest.StartsWith("DD", StringComparison.Ordinal) => ("dd", 2),
                _ => (string.Empty, 0),
            };

            if (length == 0)
            {
                return false;
            }

            dateParts.Add(new DatePart(format));
            i += length;
        }

        FlushLiteral(parts, literal);
        parts.AddRange(dateParts);
        return true;
    }

    // A literal is split into whitespace and non-whitespace runs, so that "B 1.2" matches "B  1.2".
    private static void FlushLiteral(List<Part> parts, StringBuilder literal)
    {
        var text = literal.ToString();
        literal.Clear();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[start]))
            {
                parts.Add(new LiteralPart(text[start..i], char.IsWhiteSpace(text[start])));
                start = i;
            }
        }
    }

    private static bool[] Exact(string number, bool[] from, string text)
    {
        var next = new bool[from.Length];
        for (var p = 0; p + text.Length < from.Length; p++)
        {
            if (from[p] && string.CompareOrdinal(number, p, text, 0, text.Length) == 0)
            {
                next[p + text.Length] = true;
            }
        }

        return next;
    }

    private static bool[] Digits(string number, bool[] from, int count)
    {
        var next = new bool[from.Length];
        for (var p = 0; p + count < from.Length; p++)
        {
            if (from[p] && !number.AsSpan(p, count).ContainsAnyExceptInRange('0', '9'))
            {
                next[p + count] = true;
            }
        }

        return next;
    }

    // One or more characters satisfying the predicate, from any reachable position: a single sweep,
    // so a template full of gaps against a long number stays linear.
    private static bool[] Run(string number, bool[] from, Func<char, bool> accepts)
    {
        var next = new bool[from.Length];
        var open = false;
        for (var p = 0; p < number.Length; p++)
        {
            open = (open || from[p]) && accepts(number[p]);
            next[p + 1] = open;
        }

        return next;
    }

    private abstract record Part;

    private sealed record LiteralPart(string Text, bool IsWhitespace) : Part;

    private sealed record DatePart(string Format) : Part;

    private sealed record GapPart : Part;
}
