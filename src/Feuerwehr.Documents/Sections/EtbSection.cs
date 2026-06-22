using Feuerwehr.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents.Sections;

public static class EtbSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Einsatztagebuch (ETB)").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Journal.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(95);  // Zeit
                    columns.ConstantColumn(60);  // Richtung
                    columns.RelativeColumn(1);   // Von
                    columns.RelativeColumn(1);   // An
                    columns.RelativeColumn(3);   // Eintrag
                    columns.RelativeColumn(1);   // Erfasst von
                });

                table.Header(header =>
                {
                    foreach (var title in new[] { "Zeit", "Richtung", "Von", "An", "Eintrag", "Erfasst von" })
                        header.Cell().Element(HeaderCell).Text(title).SemiBold();
                });

                foreach (var entry in incident.Journal)
                {
                    table.Cell().Element(BodyCell).Text(Formatting.Timestamp(entry.Timestamp));
                    table.Cell().Element(BodyCell).Text(Formatting.Direction(entry.Direction));
                    table.Cell().Element(BodyCell).Text(Formatting.OrDash(entry.From));
                    table.Cell().Element(BodyCell).Text(Formatting.OrDash(entry.To));
                    table.Cell().Element(BodyCell).Text(entry.Text);
                    table.Cell().Element(BodyCell).Text(entry.EnteredBy);
                }
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Medium);

    private static IContainer BodyCell(IContainer c) =>
        c.PaddingVertical(2).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
}
