using System.Globalization;
using LageBuch.Domain;
using LageBuch.Domain.Equipment;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>The Ausgabeliste — every Gerät handed out, to whom, and whether it came back — as one table.</summary>
public static class IssuedItemsSection
{
    private const string NotReturned = "nicht zurückgegeben";

    private static readonly string[] HeaderTitles = ["Nr.", "Gerät", "Ausgegeben an", "Ausgabe", "Rückgabe", "Bemerkung"];

    public static void Compose(IContainer container, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Ausgabeliste").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.IssuedItems.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            if (incident.ItemsOut > 0)
            {
                column.Item().Text($"{incident.ItemsOut} von {incident.IssuedItems.Count} {NotReturned}")
                    .SemiBold().FontColor(Colors.Red.Darken2);
            }

            column.Item().Table(table =>
            {
                // The recipient and the note are free text and get the slack; a full date and time
                // fits a fixed column.
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(28); // Nr.
                    columns.RelativeColumn(2); // Gerät
                    columns.RelativeColumn(3); // Ausgegeben an: name, phone and Organisation, one per line
                    columns.ConstantColumn(80); // Ausgabe
                    columns.ConstantColumn(80); // Rückgabe
                    columns.RelativeColumn(3); // Bemerkung
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    }
                });

                for (var i = 0; i < incident.IssuedItems.Count; i++)
                {
                    var item = incident.IssuedItems[i];
                    table.Cell().Element(Cells.Body).Text((i + 1).ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(Cells.Body).Text(item.Device);
                    table.Cell().Element(Cells.Body).Text(Recipient(item));

                    // The full date, not only the time: the Bericht is read long after an Einsatz
                    // that may have run past midnight.
                    table.Cell().Element(Cells.Body).Text(Formatting.Timestamp(item.IssuedAt));
                    if (item.ReturnedAt is { } returnedAt)
                    {
                        table.Cell().Element(Cells.Body).Text(Formatting.Timestamp(returnedAt));
                    }
                    else
                    {
                        table.Cell().Element(Cells.Body).Text(NotReturned).SemiBold().FontColor(Colors.Red.Darken2);
                    }

                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(item.Remark));
                }
            });
        });
    }

    private static string Recipient(IssuedItem item) =>
        string.Join(
            '\n',
            new[] { item.IssuedTo, item.IssuedToPhone is null ? null : PhoneNumber.Display(item.IssuedToPhone), item.IssuedToDetails }
                .Where(line => line is not null));
}
