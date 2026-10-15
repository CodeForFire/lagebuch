using System.Globalization;
using LageBuch.Domain;
using LageBuch.Domain.SituationReports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>
/// The Lagemeldungen sent to the Leitstelle (#592), one row each with its parts spelled out.
/// Drafts are left out: nothing in them was ever reported.
/// </summary>
public static class SituationReportsSection
{
    private static readonly string[] HeaderTitles = ["Nr.", "Zeit", "Meldender", "Lagemeldung"];

    public static void Compose(IContainer container, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var sent = incident.SituationReports.Where(r => r.IsSent).OrderBy(r => r.Number).ToList();
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Lagemeldungen").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (sent.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(32); // Nr.
                    columns.ConstantColumn(42); // Zeit
                    columns.RelativeColumn(1); // Meldender
                    columns.RelativeColumn(4); // Lagemeldung
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    }
                });

                foreach (var report in sent)
                {
                    table.Cell().Element(Cells.Body).Text(report.Number?.ToString(CultureInfo.InvariantCulture) ?? "–");
                    table.Cell().Element(Cells.Body).Text(report.SentAt is { } at ? Formatting.TimeOfDay(at) : "–");
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(report.Reporter));
                    table.Cell().Element(Cells.Body).Column(parts =>
                    {
                        foreach (var (label, value) in Parts(report))
                        {
                            parts.Item().Text(text =>
                            {
                                text.Span($"{label}: ").SemiBold();
                                text.Span(value);
                            });
                        }
                    });
                }
            });
        });
    }

    // The filled-in parts in the order they are read out; an empty one is not printed.
    private static IEnumerable<(string Label, string Value)> Parts(SituationReport report)
    {
        (string Label, string? Value)[] parts =
        [
            ("Einsatzstelle", report.Location),
            ("Lage", report.Situation),
            ("Maßnahmen", report.Measures),
            ("Einheiten", report.Units),
            ("Nachforderungen", report.Requests),
        ];
        foreach (var (label, value) in parts)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return (label, value);
            }
        }
    }
}
