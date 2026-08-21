using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class ForcesSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Kräfteübersicht").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Forces.Count > 0)
            {
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2); // LageBuch
                        columns.RelativeColumn(2); // Funkrufname
                        columns.ConstantColumn(55); // Stärke
                        columns.ConstantColumn(45); // AGT
                        columns.RelativeColumn(2); // Status
                        columns.RelativeColumn(3); // Bemerkung
                    });

                    table.Header(header =>
                    {
                        foreach (var title in new[] { "Feuerwehr", "Funkrufname", "Stärke", "AGT", "Status", "Bemerkung" })
                            header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    });

                    foreach (var unit in incident.Forces)
                    {
                        table.Cell().Element(Cells.Body).Text(unit.Brigade);
                        table.Cell().Element(Cells.Body).Text(Formatting.OrDash(unit.CallSign));
                        table.Cell().Element(Cells.Body).Text(unit.PersonnelCount.ToString());
                        table.Cell().Element(Cells.Body).Text(unit.ScbaCount.ToString());
                        table.Cell().Element(Cells.Body).Text(Formatting.OrDash(unit.Status));
                        table.Cell().Element(Cells.Body).Text(Formatting.OrDash(unit.Notes));
                    }
                });
            }
            else
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
            }

            column.Item().PaddingTop(4).Text(t =>
            {
                t.Span("Gesamtstärke: ").SemiBold();
                t.Span(incident.TotalPersonnel.ToString());
                t.Span("   davon Atemschutzgeräteträger: ").SemiBold();
                t.Span(incident.TotalScba.ToString());
            });
        });
    }
}
