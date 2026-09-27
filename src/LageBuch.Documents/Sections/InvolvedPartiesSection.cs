using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>The Beteiligte — people involved who are not forces — as one table.</summary>
public static class InvolvedPartiesSection
{
    private static readonly string[] HeaderTitles = ["Name", "Telefon", "Notiz"];

    public static void Compose(IContainer container, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Beteiligte").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.InvolvedParties.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                // The note is the free text and gets the slack; a phone number fits a fixed column.
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2); // Name
                    columns.ConstantColumn(110); // Telefon
                    columns.RelativeColumn(3); // Notiz
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    }
                });

                foreach (var party in incident.InvolvedParties)
                {
                    table.Cell().Element(Cells.Body).Text(party.Name);
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(party.Phone));
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(party.Notes));
                }
            });
        });
    }
}
