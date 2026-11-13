using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One Messgröße's Verlauf over the Messreihe: per Messpunkt its values in time order, and the
/// gas's bands for the threshold lines. Drawn small in its tile and large once the tile is opened.
/// </summary>
public sealed record MeasurementTrend(
    string Name,
    string Unit,
    GasDirection Direction,
    decimal? Elevated,
    decimal? Dangerous,
    decimal? Lethal,
    IReadOnlyList<MeasurementTrendSeries> Series)
{
    /// <summary>"VERLAUF · CO (ppm)", the panel's heading.</summary>
    public string Heading => Unit.Length == 0 ? $"VERLAUF · {Name}" : $"VERLAUF · {Name} ({Unit})";

    /// <summary>The Verlauf of <paramref name="channel"/> at the Messpunkte in <paramref name="points"/>,
    /// in their order; a Messpunkt that never measured it has no series.</summary>
    public static MeasurementTrend Of(MeasurementChannel channel, IEnumerable<MeasuringPoint> points, IReadOnlyList<Measurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(measurements);
        var series = new List<MeasurementTrendSeries>();
        foreach (var point in points)
        {
            // OrderBy is stable: two Messungen at the same time keep the order they were recorded in.
            var values = measurements
                .Where(m => m.PointId == point.Id && m.Values.ContainsKey(channel.Id))
                .OrderBy(m => m.MeasuredAt)
                .Select(m => (Measurement: m, Value: m.Values[channel.Id]))
                .Select(x => new MeasurementTrendPoint(
                    x.Measurement.MeasuredAt,
                    x.Value.Low,
                    x.Value.High,
                    MeasurementRules.SeverityOf(channel, x.Value),
                    x.Measurement.Status == MeasurementStatus.Outlier))
                .ToList();
            if (values.Count > 0)
            {
                series.Add(new MeasurementTrendSeries(point.Name, values));
            }
        }

        var definition = channel.Definition;
        return new MeasurementTrend(
            channel.Name, channel.Unit, definition.Direction, definition.Elevated, definition.Dangerous, definition.Lethal, series);
    }
}

/// <summary>One Messpunkt's line in a <see cref="MeasurementTrend"/>.</summary>
public sealed record MeasurementTrendSeries(string PointName, IReadOnlyList<MeasurementTrendPoint> Points);

/// <summary>One value of a <see cref="MeasurementTrendSeries"/>: a single number has
/// <see cref="Low"/> equal to <see cref="High"/>. An Ausreißer stays in, marked, off the line.</summary>
public sealed record MeasurementTrendPoint(DateTimeOffset At, decimal Low, decimal High, CoSeverity Severity, bool IsOutlier);
