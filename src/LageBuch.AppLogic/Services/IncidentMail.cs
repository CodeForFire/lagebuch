using System.Globalization;
using System.Text;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The prefilled subject and body of the e-mail that carries an incident's PDF. Both stay free of
/// person names: the PDF already carries what the recipient needs, and a subject line is shown in
/// every inbox listing it passes through.
/// </summary>
public static class IncidentMail
{
    // "Einsatzbericht 2026-0815 · B3 Wohnungsbrand · Hauptstr. 5, Nord · 19.09.2026 22:17", blank
    // parts dropped, so even an unnamed incident gets a subject that sorts and reads sensibly.
    public static string Subject(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var parts = new[]
            {
                incident.IncidentNumber?.Value,
                incident.Keyword,
                Formatting.Address(incident.Street, incident.District),
                Formatting.Timestamp(incident.StartedAt),
            }
            .Select(SingleLine)
            .Where(p => p.Length > 0);
        return "Einsatzbericht " + string.Join(" · ", parts);
    }

    public static string Body(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var body = new StringBuilder()
            .AppendLine("Anbei der Einsatzbericht als PDF.")
            .AppendLine()
            .Append("Beginn: ").AppendLine(Formatting.Timestamp(incident.StartedAt));
        if (incident.ClosedAt is { } closedAt)
        {
            body.Append("Abschluss: ").AppendLine(Formatting.Timestamp(closedAt));
        }

        return body.ToString();
    }

    // A subject is one header line; a line break typed into the Stichwort -- or sent by a sync peer --
    // would otherwise become a header break in a mailto: or xdg-email hand-off. Bidi overrides and
    // zero-width characters would let it display as something else in every inbox. All of them
    // collapse to one space.
    private static string SingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = new string(value.Select(c => IsInvisibleOrBreaking(c) ? ' ' : c).ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsInvisibleOrBreaking(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator;
}
