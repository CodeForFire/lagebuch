using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class CoMessprotokollSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("CO-Messprotokoll").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Buildings.Count == 0)
            {
                column.Item().Text("— kein CO-Messprotokoll erfasst —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            foreach (var building in incident.Buildings)
            {
                column.Item().PaddingTop(8).Text(t =>
                {
                    t.Span($"{building.Name}: ").SemiBold();
                    t.Span($"{CoMeasurementLabels.FloorRangeLabel(building.UndergroundFloorCount, building.FloorCount)}, {building.ApartmentsPerFloor} Whg./Geschoss");
                });

                // EG/OG floors (#218) share one column per Wohnung across the building, so they
                // fit a single grid. UG floors (#265) are a free-form per-floor list -- their unit
                // count doesn't match apartmentsPerFloor, so they can't share this table's fixed
                // columns and are listed separately below instead.
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);
                        for (var apt = 1; apt <= building.ApartmentsPerFloor; apt++)
                        {
                            columns.ConstantColumn(65);
                        }

                        columns.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(Cells.Header).Text("Geschoss");
                        for (var apt = 1; apt <= building.ApartmentsPerFloor; apt++)
                        {
                            header.Cell().Element(Cells.Header).Text(CoMeasurementLabels.ApartmentLabel(building, apt));
                        }

                        header.Cell().Element(Cells.Header).Text("Lage");
                    });

                    for (var floor = building.FloorCount; floor >= 0; floor--)
                    {
                        table.Cell().Element(Cells.Body).Text(CoMeasurementLabels.FloorLabel(floor));

                        for (var apt = 1; apt <= building.ApartmentsPerFloor; apt++)
                        {
                            var dwelling = incident.Dwellings.FirstOrDefault(d =>
                                d.BuildingId == building.Id &&
                                d.FloorOrdinal == floor &&
                                d.ApartmentNumber == apt);

                            if (dwelling?.CoValue is { } coVal)
                            {
                                table.Cell().Element(c => c
                                    .Background(GetColor(dwelling.Status))
                                    .Padding(2))
                                    .Text($"{coVal} ppm")
                                    .FontSize(8);
                            }
                            else
                            {
                                table.Cell().Element(Cells.Body).Text("—");
                            }
                        }

                        var description = building.FloorDescriptions.TryGetValue(floor, out var d) ? d : null;
                        table.Cell().Element(Cells.Body).Text(description ?? "—");
                    }
                });

                for (var floor = -1; floor >= -building.UndergroundFloorCount; floor--)
                {
                    var units = incident.Dwellings
                        .Where(d => d.BuildingId == building.Id && d.FloorOrdinal == floor)
                        .OrderBy(d => d.ApartmentNumber)
                        .ToList();

                    column.Item().PaddingTop(4).Text($"{CoMeasurementLabels.FloorLabel(floor)} — Einheiten").SemiBold().FontSize(9);
                    if (units.Count == 0)
                    {
                        column.Item().Text("— keine Einheiten erfasst —").Italic().FontSize(8).FontColor(Colors.Grey.Medium);
                        continue;
                    }

                    foreach (var unit in units)
                    {
                        var identity = string.IsNullOrWhiteSpace(unit.ResidentName) ? "Ohne Bezeichnung" : unit.ResidentName;
                        var co = unit.CoValue is { } v ? $"{v} ppm" : "kein Messwert";
                        var key = unit.KeyAvailable is true ? "ja" : unit.KeyAvailable is false ? "nein" : "—";
                        column.Item().Text(t =>
                        {
                            t.Span($"• {identity}: ").FontSize(8);
                            t.Span($"{co}, Schlüssel: {key}, {CoMeasurementLabels.StatusText(unit.Status)}")
                                .FontSize(8).FontColor(GetColor(unit.Status));
                        });
                    }
                }
            }

            var affected = incident.Dwellings.Where(d => d.Status == DwellingStatus.Affected).ToList();
            if (affected.Count > 0)
            {
                column.Item().PaddingTop(8).Text("Betroffene Wohnungen").SemiBold();
                foreach (var d in affected)
                {
                    var building = incident.Buildings.FirstOrDefault(b => b.Id == d.BuildingId);
                    if (building is null)
                    {
                        continue;
                    }

                    var location = CoMeasurementLabels.DwellingLocation(building, d.FloorOrdinal, d.ApartmentNumber);
                    var resident = d.ResidentName ?? "—";
                    var key = d.KeyAvailable is true ? "ja" : d.KeyAvailable is false ? "nein" : "—";
                    var co = d.CoValue is { } v ? $"{v} ppm" : "—";
                    column.Item().Text($"• {location}, Bewohner: {resident}, Schlüssel: {key}, CO: {co}");
                }
            }

            column.Item().PaddingTop(8).Text(t =>
            {
                t.Span("Legende: ").SemiBold().FontSize(8);
                t.Span("■ ").FontColor(HexColor("#FFC000")).FontSize(8);
                t.Span("Nicht abgesucht  ").FontSize(8);
                t.Span("■ ").FontColor(HexColor("#92D050")).FontSize(8);
                t.Span("Abgesucht  ").FontSize(8);
                t.Span("■ ").FontColor(HexColor("#FF0000")).FontSize(8);
                t.Span("Betroffen").FontSize(8);
            });
        });
    }

    private static string GetColor(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => HexColor("#FFC000"),
        DwellingStatus.Searched => HexColor("#92D050"),
        DwellingStatus.Affected => HexColor("#FF0000"),
        _ => Colors.White,
    };

    private static string HexColor(string hex) => hex;
}
