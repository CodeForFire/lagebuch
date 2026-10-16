using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class IncidentHeaderSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        // The mark sits beside the title rather than above it so the header keeps its height —
        // it repeats on every page, so any vertical growth here costs space in the whole report.
        container.Row(header =>
        {
            header.ConstantItem(46).AlignTop().Image(Branding.GetMark());
            header.RelativeItem().PaddingLeft(12).Element(c => ComposeTitleBlock(c, incident));
        });
    }

    /// <summary>The full seal hash of a sealed, closed incident, or <c>null</c> when it has none.</summary>
    public static string? SealText(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.State == IncidentState.Closed ? incident.Seal?.Hash : null;
    }

    private static void ComposeTitleBlock(IContainer container, Incident incident)
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
                    t.Span(Formatting.OrDash(Formatting.Address(incident.Street, incident.District)));
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

            // The full hash, beside the short form in the footer: what someone comparing a printout
            // with a file years later checks against (#598).
            if (SealText(incident) is { } seal)
            {
                column.Item().Text(t =>
                {
                    t.Span("Siegel: ").SemiBold();
                    t.Span(seal).FontSize(8);
                });
            }
        });
    }
}
