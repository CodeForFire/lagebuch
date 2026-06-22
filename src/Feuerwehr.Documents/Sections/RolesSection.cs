using Feuerwehr.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents.Sections;

public static class RolesSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Funktionszuweisung").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Roles.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1); // Funktion
                    columns.RelativeColumn(2); // Name
                    columns.RelativeColumn(2); // Funkrufname
                    columns.ConstantColumn(95); // Von
                    columns.ConstantColumn(95); // Bis
                });

                table.Header(header =>
                {
                    foreach (var title in new[] { "Funktion", "Name", "Funkrufname", "Von", "Bis" })
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                });

                foreach (var role in incident.Roles)
                {
                    table.Cell().Element(Cells.Body).Text(role.Role);
                    table.Cell().Element(Cells.Body).Text(role.PersonName);
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(role.CallSign));
                    table.Cell().Element(Cells.Body).Text(role.From is { } f ? Formatting.Timestamp(f) : "—");
                    table.Cell().Element(Cells.Body).Text(role.To is { } t ? Formatting.Timestamp(t) : "—");
                }
            });
        });
    }
}
