using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class AtemschutzSection
{
    private static readonly string[] HeaderTitles =
        ["Trupp", "Mannschaft", "Funkrufname", "Start", "Rückzug", "Ende", "Druck Start", "Druck akt."];

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
                    columns.ConstantColumn(75); // Rückzug
                    columns.ConstantColumn(75); // Ausstieg
                    columns.ConstantColumn(60); // Einstiegsdruck
                    columns.ConstantColumn(60); // letzter Druck
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                });

                foreach (var trupp in incident.ScbaTrupps)
                {
                    table.Cell().Element(Cells.Body).Text(trupp.DisplayName);
                    table.Cell().Element(Cells.Body).Text(trupp.MembersDisplay);
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(trupp.CallSign));
                    table.Cell().Element(Cells.Body).Text(trupp.StartTime is { } s ? Formatting.Timestamp(s) : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.WithdrawTime is { } w ? Formatting.Timestamp(w) : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.ExitTime is { } e ? Formatting.Timestamp(e) : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.EntryPressure is { } ep ? $"{ep} bar" : "—");
                    table.Cell().Element(Cells.Body).Text(trupp.LatestPressure is { } lp ? $"{lp} bar" : "—");
                }
            });
        });
    }
}
