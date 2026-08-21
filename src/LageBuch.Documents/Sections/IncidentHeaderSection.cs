using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class IncidentHeaderSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Text("Einsatzdokumentation")
                .FontSize(18).SemiBold().FontColor(Colors.Blue.Darken2);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Einsatznummer: ").SemiBold();
                    t.Span(Formatting.OrDash(incident.IncidentNumber?.Value));
                });
                row.RelativeItem().Text(t =>
                {
                    t.Span("Status: ").SemiBold();
                    t.Span(Formatting.State(incident.State));
                });
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Stichwort: ").SemiBold();
                    t.Span(Formatting.OrDash(incident.Keyword));
                });
                row.RelativeItem().Text(t =>
                {
                    t.Span("Adresse: ").SemiBold();
                    t.Span(Formatting.OrDash(
                        string.Join(", ", new[] { incident.Street, incident.District }
                            .Where(s => !string.IsNullOrWhiteSpace(s)))));
                });
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Beginn: ").SemiBold();
                    t.Span(Formatting.Timestamp(incident.StartedAt));
                });
                row.RelativeItem().Text(t =>
                {
                    t.Span("Abschluss: ").SemiBold();
                    t.Span(incident.ClosedAt is { } closed ? Formatting.Timestamp(closed) : "—");
                });
            });
        });
    }
}
