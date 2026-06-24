using Feuerwehr.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents.Sections;

public static class AtemschutzSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Atemschutzüberwachung").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.ScbaTrupps.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2); // Trupp
                    columns.RelativeColumn(3); // Mannschaft
                    columns.RelativeColumn(2); // Funkrufname
                    columns.ConstantColumn(75); // Einstieg
                    columns.ConstantColumn(75); // Ausstieg
                    columns.ConstantColumn(60); // Einstiegsdruck
                    columns.ConstantColumn(60); // letzter Druck
                });

                table.Header(header =>
                {
                    foreach (var title in new[]
                             { "Trupp", "Mannschaft", "Funkrufname", "Start", "Ende", "Druck Start", "Druck akt." })
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                });

                foreach (var trupp in incident.ScbaTrupps)
                {
                    table.Cell().Element(Cells.Body).Text(trupp.Designation);
                    table.Cell().Element(Cells.Body).Text(trupp.Members);
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(trupp.CallSign));
                    table.Cell().Element(Cells.Body).Text(trupp.StartTime is { } s ? Formatting.Timestamp(s) : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.ExitTime is { } e ? Formatting.Timestamp(e) : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.StartPressure is { } sp ? $"{sp} bar" : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.LatestPressure is { } lp ? $"{lp} bar" : "—");
                }
            });
        });
    }
}
