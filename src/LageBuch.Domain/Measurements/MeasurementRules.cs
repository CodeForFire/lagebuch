using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Measurements;

/// <summary>The judgements the Messprotokoll makes about its values (#930), shared by the view
/// models, the ETB text and the PDF so they cannot disagree.</summary>
public static class MeasurementRules
{
    /// <summary>The band a value falls in, read off the worse of its two ends: the high end of a
    /// rising gas, the low end of a falling one such as O₂. A single number has one end.</summary>
    public static CoSeverity SeverityOf(MeasurementChannel channel, MeasuredValue value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(value);
        var low = GasSeverityClassifier.SeverityOf(channel.Definition, value.Low);
        var high = GasSeverityClassifier.SeverityOf(channel.Definition, value.High);
        return low > high ? low : high;
    }

    /// <summary>Whether either end of a value is beyond the gas's Plausibel-bis limit: worth a
    /// second look, never a rejection.</summary>
    public static bool IsImplausible(MeasurementChannel channel, MeasuredValue value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(value);
        return GasSeverityClassifier.IsImplausible(channel.Definition, value.Low)
            || GasSeverityClassifier.IsImplausible(channel.Definition, value.High);
    }

    /// <summary>The Messung's number at its Messpunkt, counting from 1 by <see cref="Measurement.MeasuredAt"/>,
    /// two at the same time in the order they were recorded. A Messung entered afterwards with an
    /// earlier time takes its place in the row. Throws when the log does not hold it.</summary>
    public static int RunningNumber(MeasurementLog log, Measurement measurement)
    {
        ArgumentNullException.ThrowIfNull(log);
        return RunningNumber(log.Measurements, measurement);
    }

    internal static int RunningNumber(IEnumerable<Measurement> measurements, Measurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var index = InTimeOrder(measurements.Where(m => m.PointId == measurement.PointId))
            .FindIndex(m => m.Id == measurement.Id);
        return index >= 0
            ? index + 1
            : throw new ArgumentException($"Messung {measurement.Id} ist nicht im Messprotokoll.", nameof(measurement));
    }

    /// <summary>
    /// The latest value per Messpunkt and Messgröße, from Messungen not marked Ausreißer: at every
    /// Messpunkt, or at <paramref name="pointId"/> only. Ordered like the Messpunkte, then like
    /// the Messgrößen. A Messgröße never measured at a point has no entry there.
    /// </summary>
    public static IReadOnlyList<LatestValue> Latest(MeasurementLog log, Guid? pointId)
    {
        ArgumentNullException.ThrowIfNull(log);
        var latest = new Dictionary<(Guid Point, Guid Channel), LatestValue>();
        foreach (var measurement in InTimeOrder(log.Measurements.Where(m => m.Status != MeasurementStatus.Outlier && (pointId is null || m.PointId == pointId))))
        {
            foreach (var (channelId, value) in measurement.Values)
            {
                latest[(measurement.PointId, channelId)] = new LatestValue(measurement.PointId, channelId, value, measurement.Id, measurement.MeasuredAt);
            }
        }

        var result = new List<LatestValue>();
        foreach (var point in log.Points)
        {
            result.AddRange(log.Channels
                .Where(c => latest.ContainsKey((point.Id, c.Id)))
                .Select(c => latest[(point.Id, c.Id)]));
        }

        return result;
    }

    // OrderBy is stable, so two Messungen at the same time keep the order they were recorded in.
    private static List<Measurement> InTimeOrder(IEnumerable<Measurement> measurements) =>
        measurements.OrderBy(m => m.MeasuredAt).ToList();
}

/// <summary>One entry of <see cref="MeasurementRules.Latest"/>: the value a Messgröße last showed at a
/// Messpunkt, and the Messung it came from.</summary>
public sealed record LatestValue(Guid PointId, Guid ChannelId, MeasuredValue Value, Guid MeasurementId, DateTimeOffset MeasuredAt);
