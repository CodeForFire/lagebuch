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
                    {
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    }
                });

                foreach (var trupp in incident.ScbaTrupps)
                {
                    // The Sicherheitstrupp (#399) rides as a sub-line here rather than taking a
                    // ninth column. A4 portrait less the 1.5 cm margins leaves ~510pt; the five
                    // constant columns already claim 345 of it, so the remaining 165pt splits
                    // 2/3/2 into roughly 47/71/47pt and Mannschaft already wraps. A ninth column
                    // would cut it to 55pt (relative) or 45pt (constant) and the crew — the thing
                    // this table exists to record — would stop being legible. A sub-line costs no
                    // width at all. A dangling id prints nothing, which is the right degradation.
                    table.Cell().Element(Cells.Body).Column(cell =>
                    {
                        cell.Item().Text(trupp.DisplayName);
                        if (trupp.SafetyTruppId is { } safetyId
                            && incident.FindScbaTruppOrDefault(safetyId) is { } safety)
                        {
                            cell.Item().Text($"Si.-Trupp: Trupp {safety.TruppNumber}")
                                .FontSize(8).FontColor(Colors.Grey.Darken1);
                        }
                    });

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
