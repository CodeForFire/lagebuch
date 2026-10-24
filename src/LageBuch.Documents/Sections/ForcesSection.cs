using System.Globalization;
using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class ForcesSection
{
    private static readonly string[] HeaderTitles = ["Feuerwehr", "Funkrufname", "Abschnitt", "Stärke", "AGT", "Status", "Bemerkung"];

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
                        columns.RelativeColumn(3); // Abschnitt (an Unterabschnitt prints "Abschnitt / Name")
                        columns.ConstantColumn(55); // Stärke
                        columns.ConstantColumn(45); // AGT
                        columns.RelativeColumn(2); // Status
                        columns.RelativeColumn(3); // Bemerkung
                    });

                    table.Header(header =>
                    {
                        foreach (var title in HeaderTitles)
                        {
                            header.Cell().Element(Cells.Header).Text(title).SemiBold();
                        }
                    });

                    foreach (var unit in incident.Forces)
                    {
                        table.Cell().Element(Cells.Body).Text(unit.Brigade);
                        table.Cell().Element(Cells.Body).Text(Formatting.OrDash(unit.CallSign));

                        // The record is the record: a dissolved Abschnitt still names its units, and a
                        // node id this file does not hold (a foreign file) reads like an unplaced unit.
                        var abschnitt = incident.NodeLabel(unit.NodeId);
                        table.Cell().Element(Cells.Body).Text(Formatting.OrDash(abschnitt));

                        // Stärke im 1/1/2-Format: Führungskräfte/Mannschaft/Gesamt (#76).
                        table.Cell().Element(Cells.Body).Text(unit.StrengthText);
                        table.Cell().Element(Cells.Body).Text(unit.ScbaCount.ToString(CultureInfo.InvariantCulture));
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

                // The rows' notation, Zugführer/Führungskraft/Mannschaft/Gesamt (#216, #621), as
                // the Kräfte view totals it in ForcesViewModel.TotalStrengthText.
                var mannschaft = incident.TotalPersonnel - incident.TotalOfficer - incident.TotalZugfuehrer;
                t.Span($"{incident.TotalZugfuehrer}/{incident.TotalOfficer}/{mannschaft}/{incident.TotalPersonnel}");
                t.Span("   davon Atemschutzgeräteträger: ").SemiBold();
                t.Span(incident.TotalScba.ToString(CultureInfo.InvariantCulture));
            });
        });
    }
}
