using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class WasserfoerderungSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Wasserförderung").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Wasserfoerderung.Count == 0)
            {
                column.Item().Text("— keine Förderstrecke geplant —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(46);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1.5f);
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(55);
                    columns.ConstantColumn(65);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    foreach (var title in new[] { "Leitung", "Übergabestelle", "Ansprechpartner", "B-Längen",
                        "Länge", "Höhen-unterschied", "Verstärker-pumpen", "Reserve-pumpen" })
                        header.Cell().Element(HeaderCell).Text(title).SemiBold();
                });

                foreach (var leitung in incident.Wasserfoerderung)
                {
                    table.Cell().Element(BodyCell).Text($"Ltg {leitung.Number}");
                    table.Cell().Element(BodyCell).Text(Formatting.OrDash(leitung.Uebergabestelle));
                    table.Cell().Element(BodyCell).Text(Formatting.OrDash(leitung.Ansprechpartner));
                    table.Cell().Element(BodyCell).Text(leitung.HoseCount.ToString());
                    table.Cell().Element(BodyCell).Text(Formatting.Meters(leitung.LengthMeters));
                    table.Cell().Element(BodyCell).Text(
                        leitung.ElevationRiseMeters > 0 ? Formatting.Meters(leitung.ElevationRiseMeters) : "—");
                    table.Cell().Element(BodyCell).Text(leitung.PumpCount.ToString());
                    table.Cell().Element(BodyCell).Text(leitung.ReservePumpCount.ToString());
                }
            });

            column.Item().PaddingTop(2).Text(
                "Planung: B-800, B-Schlauch 20 m, 8 bar Speisedruck, 1,5 bar Pumpeneingang, " +
                "3 % Reserveschlauch pro Teilstrecke.")
                .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Medium);

    private static IContainer BodyCell(IContainer c) =>
        c.PaddingVertical(2).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
}