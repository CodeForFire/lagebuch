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
                    t.Span(CoMeasurementLabels.FloorRangeLabel(building.UndergroundFloorCount, building.FloorCount));
                });

                // Every floor lists its own units (#265): floors -- especially Untergeschosse --
                // rarely share one Wohnungen count anymore, so a single fixed-column table can no
                // longer represent every floor of a building at once.
                for (var floor = building.FloorCount; floor >= -building.UndergroundFloorCount; floor--)
                {
                    var units = incident.Dwellings
                        .Where(d => d.BuildingId == building.Id && d.FloorOrdinal == floor)
                        .OrderBy(d => d.ApartmentNumber)
                        .ToList();

                    var description = building.FloorDescriptions.TryGetValue(floor, out var d2) ? d2 : null;
                    column.Item().PaddingTop(4).Text(t =>
                    {
                        t.Span($"{CoMeasurementLabels.FloorLabel(floor)}").SemiBold().FontSize(9);
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            t.Span($" — {description}").FontSize(9).Italic();
                        }
                    });

                    foreach (var unit in units)
                    {
                        var label = CoMeasurementLabels.ApartmentLabel(building, floor, unit.ApartmentNumber);
                        var co = unit.CoValue is { } v ? $"{v} ppm" : "kein Messwert";
                        var key = unit.KeyAvailable is true ? "ja" : unit.KeyAvailable is false ? "nein" : "—";
                        var resident = string.IsNullOrWhiteSpace(unit.ResidentName) ? null : $", {unit.ResidentName}";
                        column.Item().Text(t =>
                        {
                            t.Span($"• {label}{resident}: ").FontSize(8);
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
