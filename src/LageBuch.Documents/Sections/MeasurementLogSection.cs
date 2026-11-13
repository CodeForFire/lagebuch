using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>
/// The ABC/Gefahrstoff Messprotokoll (#930): the Messpunkte, what was measured with which Gerät
/// and bands, every Messung in time order with its corrections, the Bewertungen and the weather.
/// An Ausreißer is printed and marked, never dropped: the report is the record of what was read.
/// </summary>
public static class MeasurementLogSection
{
    private static readonly string[] PointHeaders = ["Messpunkt", "Ort", "Bereich", "Messungen"];

    private static readonly string[] ChannelHeaders = ["Messgröße", "Richtung", "Schwellen", "Messgerät", "Bemerkung"];

    private static readonly string[] DeviceHeaders = ["Art", "Bezeichnung", "Nr. / Charge", "Fahrzeug", "Prüfung"];

    private static readonly string[] MeasurementHeaders = ["Zeit", "Auftrag", "Messpunkt", "Trupp · PSA · Messart", "Messwerte", "Status"];

    private static readonly string[] WeatherHeaders = ["Zeit", "Wind aus", "Geschw.", "Temp.", "Bewölkung", "Niederschlag", "Quelle"];

    /// <summary>Whether there is anything to print; an Einsatz that never measured leaves the
    /// section out rather than printing an empty heading.</summary>
    public static bool HasContent(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.MeasuringPoints.Count > 0
            || incident.MeasurementChannels.Count > 0
            || incident.MeasuringDevices.Count > 0
            || incident.Measurements.Count > 0
            || incident.WeatherObservations.Count > 0;
    }

    public static void Compose(IContainer container, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var log = incident.MeasurementLog;
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Messprotokoll").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (log.Points.Count > 0)
            {
                column.Item().PaddingTop(4).Text("Messpunkte").SemiBold();
                column.Item().Element(c => PointsTable(c, log));
            }

            if (log.Channels.Count > 0)
            {
                column.Item().PaddingTop(4).Text("Messgrößen").SemiBold();
                column.Item().Element(c => ChannelsTable(c, log));
            }

            if (log.Devices.Count > 0)
            {
                column.Item().PaddingTop(4).Text("Messgeräte").SemiBold();
                column.Item().Element(c => DevicesTable(c, log));
            }

            column.Item().PaddingTop(4).Text("Messreihe").SemiBold();
            if (log.Measurements.Count == 0)
            {
                column.Item().Text("— keine Messung —").Italic().FontColor(Colors.Grey.Medium);
            }
            else
            {
                column.Item().Element(c => MeasurementsTable(c, log));
            }

            var assessed = InTimeOrder(log)
                .Select(m => (Measurement: m, m.Assessment))
                .Where(x => x.Assessment is not null)
                .ToList();
            if (assessed.Count > 0)
            {
                column.Item().PaddingTop(4).Text("Bewertungen").SemiBold();
                foreach (var (measurement, assessment) in assessed)
                {
                    if (assessment is not null)
                    {
                        column.Item().Text(AssessmentLine(log, measurement, assessment)).FontSize(9);
                    }
                }
            }

            if (log.Weather.Count > 0)
            {
                column.Item().PaddingTop(4).Text("Wetter").SemiBold();
                column.Item().Element(c => WeatherTable(c, log));
            }
        });
    }

    private static void PointsTable(IContainer container, MeasurementLog log) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2); // Messpunkt
                columns.RelativeColumn(3); // Ort
                columns.RelativeColumn(2); // Bereich
                columns.ConstantColumn(60); // Messungen
            });
            Header(table, PointHeaders);
            foreach (var point in log.Points)
            {
                table.Cell().Element(Cells.Body).Text(point.Name);
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(point.Location));
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.Zone(point.Zone));
                table.Cell().Element(Cells.Body).Text(Formatting.Number(log.Measurements.Count(m => m.PointId == point.Id)));
            }
        });

    private static void ChannelsTable(IContainer container, MeasurementLog log) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2); // Messgröße and unit
                columns.ConstantColumn(54); // Richtung
                columns.RelativeColumn(4); // Schwellen
                columns.RelativeColumn(3); // Messgerät
                columns.RelativeColumn(2); // Bemerkung
            });
            Header(table, ChannelHeaders);
            foreach (var channel in log.Channels)
            {
                var name = channel.Unit.Length == 0 ? channel.Name : $"{channel.Name} ({channel.Unit})";
                table.Cell().Element(Cells.Body).Text(channel.IsActive ? name : $"{name}\ndeaktiviert");
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.Direction(channel.Definition.Direction));
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.Bands(channel.Definition));
                table.Cell().Element(Cells.Body).Text(DeviceText(log, channel.DeviceId));
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(channel.Note));
            }
        });

    private static void DevicesTable(IContainer container, MeasurementLog log) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3); // Art
                columns.RelativeColumn(2); // Bezeichnung
                columns.RelativeColumn(2); // Nr.
                columns.RelativeColumn(2); // Fahrzeug
                columns.RelativeColumn(2); // Prüfung
            });
            Header(table, DeviceHeaders);
            foreach (var device in log.Devices)
            {
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.DeviceKind(device.Kind));
                table.Cell().Element(Cells.Body).Text(device.Name);
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(device.Number));
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(device.Vehicle));
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(device.Check));
            }
        });

    private static void MeasurementsTable(IContainer container, MeasurementLog log)
    {
        var points = log.Points.ToDictionary(p => p.Id);
        var channels = log.Channels.ToDictionary(c => c.Id);
        container.Table(table =>
        {
            // The values share one cell, so ten Messgrößen still fit A4 portrait.
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(78); // Zeit: the full date, the Einsatz may run past midnight
                columns.ConstantColumn(44); // Auftrag
                columns.RelativeColumn(2); // Messpunkt
                columns.RelativeColumn(3); // Trupp
                columns.RelativeColumn(4); // Messwerte
                columns.ConstantColumn(58); // Status
            });
            Header(table, MeasurementHeaders);
            foreach (var measurement in InTimeOrder(log))
            {
                var point = points[measurement.PointId];
                var outlier = measurement.Status == MeasurementStatus.Outlier;
                table.Cell().Element(Cells.Body).Text(Formatting.Timestamp(measurement.MeasuredAt));
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(measurement.Order));
                table.Cell().Element(Cells.Body).Text($"{point.Name} #{MeasurementRules.RunningNumber(log, measurement)}\n{MeasurementLabels.Zone(point.Zone)}");
                table.Cell().Element(Cells.Body).Text(TeamText(measurement));
                table.Cell().Element(Cells.Body).Text(t => ValuesText(t, log, channels, measurement, outlier));
                var status = table.Cell().Element(Cells.Body).Text(MeasurementLabels.Status(measurement.Status));
                if (outlier)
                {
                    status.SemiBold().FontColor(Colors.Grey.Darken1);
                }
            }
        });
    }

    // Every value in list order, each coloured by its own band; an Ausreißer's values in grey, so
    // the page scanned for red does not announce a danger the Fachberater ruled out. Below them
    // the observations, the note and every correction, with the value it replaced.
    private static void ValuesText(
        TextDescriptor text, MeasurementLog log, Dictionary<Guid, MeasurementChannel> channels, Measurement measurement, bool outlier)
    {
        var values = log.Channels.Where(c => measurement.Values.ContainsKey(c.Id)).ToList();
        if (outlier)
        {
            text.Span("Ausreißer – zählt nicht: ").SemiBold();
        }

        if (values.Count == 0)
        {
            text.Span("kein Messwert");
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                text.Span(" · ");
            }

            var channel = values[i];
            var value = measurement.Values[channel.Id];
            var span = text.Span(MeasurementLabels.NamedValue(channel, value));
            if (outlier)
            {
                span.FontColor(Colors.Grey.Darken1);
            }
            else if (SeverityColor(MeasurementRules.SeverityOf(channel, value)) is { } colour)
            {
                span.FontColor(colour);
            }

            if (MeasurementRules.IsImplausible(channel, value))
            {
                text.Span(" (unplausibel?)").Italic();
            }
        }

        var observations = new[] { measurement.Odour ? "Geruch wahrnehmbar" : null, measurement.Deposits ? "Ruß / Niederschlag erkennbar" : null, measurement.Note }
            .OfType<string>()
            .ToList();
        if (observations.Count > 0)
        {
            text.Span("\n" + string.Join(" – ", observations)).FontSize(8);
        }

        foreach (var correction in measurement.Corrections)
        {
            if (channels.TryGetValue(correction.ChannelId, out var channel))
            {
                text.Span($"\nkorrigiert: {CorrectionText(channel, correction)}").FontSize(8).Italic();
            }
        }
    }

    private static string CorrectionText(MeasurementChannel channel, MeasurementCorrection correction)
    {
        var previous = correction.Previous is { } p ? MeasurementLabels.Value(channel, p) : "–";
        var value = correction.Value is { } v ? MeasurementLabels.Value(channel, v) : "gelöscht";
        return $"{channel.Name} {previous} → {value} (Grund: {correction.Reason}; {correction.By}, {Formatting.TimeOfDay(correction.At)})";
    }

    private static string AssessmentLine(MeasurementLog log, Measurement measurement, MeasurementAssessment assessment)
    {
        var point = log.Points.First(p => p.Id == measurement.PointId);
        var comment = assessment.Text is null ? string.Empty : $" – {assessment.Text}";
        return $"• {Formatting.TimeOfDay(measurement.MeasuredAt)} {point.Name} #{MeasurementRules.RunningNumber(log, measurement)}: "
            + $"{MeasurementLabels.Status(assessment.Status)}{comment} ({assessment.By}, {Formatting.Timestamp(assessment.At)})";
    }

    private static void WeatherTable(IContainer container, MeasurementLog log) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(78); // Zeit
                columns.ConstantColumn(54); // Wind aus
                columns.ConstantColumn(54); // Geschw.
                columns.ConstantColumn(46); // Temp.
                columns.RelativeColumn(2); // Bewölkung
                columns.RelativeColumn(2); // Niederschlag
                columns.RelativeColumn(2); // Quelle
            });
            Header(table, WeatherHeaders);
            foreach (var observation in log.Weather.OrderBy(w => w.ObservedAt))
            {
                table.Cell().Element(Cells.Body).Text(Formatting.Timestamp(observation.ObservedAt));
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.Wind(observation.Wind));
                table.Cell().Element(Cells.Body).Text(observation.WindSpeedKmh is { } speed ? Formatting.Measurement(speed, "km/h") : "—");
                table.Cell().Element(Cells.Body).Text(observation.TemperatureCelsius is { } temperature ? Formatting.Measurement(temperature, "°C") : "—");
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.Clouds(observation.Clouds));
                table.Cell().Element(Cells.Body).Text(MeasurementLabels.Precipitation(observation.Precipitation));
                table.Cell().Element(Cells.Body).Text(Formatting.OrDash(observation.Source));
            }
        });

    private static void Header(TableDescriptor table, string[] titles) =>
        table.Header(header =>
        {
            foreach (var title in titles)
            {
                header.Cell().Element(Cells.Header).Text(title).SemiBold();
            }
        });

    private static string TeamText(Measurement measurement) =>
        string.Join(
            '\n',
            new[] { measurement.Team, MeasurementLabels.Protection(measurement.Protection), MeasurementLabels.Mode(measurement.Mode) }
                .OfType<string>());

    private static string DeviceText(MeasurementLog log, Guid? deviceId) =>
        log.Devices.FirstOrDefault(d => d.Id == deviceId) is { } device
            ? device.Number is null ? device.Name : $"{device.Name} (Nr. {device.Number})"
            : "—";

    // OrderBy is stable: two Messungen at the same minute keep the order they were recorded in.
    private static IEnumerable<Measurement> InTimeOrder(MeasurementLog log) =>
        log.Measurements.OrderBy(m => m.MeasuredAt);

    // The same hex values as CoMessprotokollSection, which keeps them in step with the AXAML brushes.
    private static string? SeverityColor(CoSeverity severity) => severity switch
    {
        CoSeverity.Elevated => "#FFB23E",
        CoSeverity.Dangerous => "#FF5A3C",
        CoSeverity.Lethal => "#FF3D2E",
        _ => null,
    };
}
