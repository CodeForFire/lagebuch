using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>
/// Every measured value of the Einsatz in time order (#972): the record the ETB section no longer
/// carries. Built from the Messreihen themselves, never by reading the ETB's "Gasmessung …" prose
/// back, so a renamed Haus or a later file format cannot garble it.
/// </summary>
public static class MessungenSection
{
    private static readonly string[] HeaderTitles = ["Zeit", "Ort", "Gas", "Wert", "Erfasst von"];

    /// <summary>Whether there is a reading to print; without one the section is left out.</summary>
    public static bool HasContent(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.Dwellings.Any(d => d.Readings.Count > 0);
    }

    public static void Compose(IContainer container, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var rows = Rows(incident);
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Messungen").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(95); // Zeit, as wide as the ETB's
                    columns.RelativeColumn(8);  // Ort
                    columns.RelativeColumn(3);  // Gas
                    columns.RelativeColumn(4);  // Wert
                    columns.RelativeColumn(5);  // Erfasst von, wide enough for "Huber (FFB 12/1)"
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(HeaderCell).Text(title).SemiBold();
                    }
                });

                foreach (var row in rows)
                {
                    table.Cell().Element(BodyCell).Text(Formatting.Timestamp(row.MeasuredAt));
                    table.Cell().Element(BodyCell).Text(row.Location);
                    table.Cell().Element(BodyCell).Text(row.Gas);
                    table.Cell().Element(BodyCell).Text(row.Value);
                    table.Cell().Element(BodyCell).Text(row.RecordedBy);
                }
            });
        });
    }

    // Collected Wohnung by Wohnung and then ordered by time alone: OrderBy is stable, so the
    // readings of one save keep the gas list's order, in which RecordCoValues writes them.
    private static List<Row> Rows(Incident incident)
    {
        var rows = new List<Row>();
        foreach (var dwelling in incident.Dwellings)
        {
            var building = incident.Buildings.FirstOrDefault(b => b.Id == dwelling.BuildingId);
            if (building is null)
            {
                continue;
            }

            var location = CoMeasurementLabels.DwellingLocation(building, dwelling.FloorOrdinal, dwelling.ApartmentNumber);
            foreach (var reading in dwelling.Readings)
            {
                var gas = incident.Gases.FirstOrDefault(g => g.Id == reading.GasId);

                // A cleared value is part of the record too: it says when a reading was withdrawn,
                // the way the Gasmessprotokoll's Messreihe does.
                var value = reading.Value is { } v ? Formatting.Measurement(v, gas?.Unit ?? string.Empty) : "gelöscht";
                rows.Add(new Row(reading.MeasuredAt, location, gas?.Name ?? "—", value, reading.RecordedBy));
            }
        }

        return rows.OrderBy(r => r.MeasuredAt).ToList();
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Medium);

    private static IContainer BodyCell(IContainer c) =>
        c.PaddingVertical(2).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

    private sealed record Row(DateTimeOffset MeasuredAt, string Location, string Gas, string Value, string RecordedBy);
}
