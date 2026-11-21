namespace LageBuch.Domain.Measurements;

/// <summary>
/// The whole Messprotokoll of an Einsatz as one read-only bundle (#930): Messpunkte, Geräte,
/// Messgrößen, Messungen and Wetter. It is what <see cref="Incident.Rehydrate"/> takes, what the
/// snapshot carries and what the PDF reads; <see cref="Incident.MeasurementLog"/> hands out a copy.
/// </summary>
public sealed record MeasurementLog(
    IReadOnlyList<MeasuringPoint> Points,
    IReadOnlyList<MeasuringDevice> Devices,
    IReadOnlyList<MeasurementChannel> Channels,
    IReadOnlyList<Measurement> Measurements,
    IReadOnlyList<WeatherObservation> Weather)
{
    public static MeasurementLog Empty { get; } = new([], [], [], [], []);

    /// <summary>
    /// The same log with every rule that cannot be trusted from outside applied, never strict:
    /// each entry sanitised, and entries over a cap, with a duplicate id, without a name, or
    /// pointing at a Messpunkt that is not there dropped. A Messgröße naming an unknown Gerät is
    /// kept and unlinked; a value or correction of an unknown Messgröße is dropped from its
    /// Messung. Two Messgrößen with the same name keep the first.
    /// </summary>
    public MeasurementLog Sanitized()
    {
        var points = Points
            .Select(p => p.Sanitized())
            .OfType<MeasuringPoint>()
            .DistinctBy(p => p.Id)
            .Take(MeasuringPoint.MaxCount)
            .ToList();
        var devices = Devices
            .Select(d => d.Sanitized())
            .OfType<MeasuringDevice>()
            .DistinctBy(d => d.Id)
            .Take(MeasuringDevice.MaxCount)
            .ToList();
        var deviceIds = devices.Select(d => d.Id).ToHashSet();

        var channels = new List<MeasurementChannel>();
        foreach (var channel in Channels.Select(c => c.Sanitized()).OfType<MeasurementChannel>())
        {
            if (channels.Exists(c => c.Id == channel.Id || string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            channels.Add(channel.DeviceId is { } deviceId && !deviceIds.Contains(deviceId) ? channel with { DeviceId = null } : channel);
            if (channels.Count == MeasurementChannel.MaxCount)
            {
                break;
            }
        }

        var pointIds = points.Select(p => p.Id).ToHashSet();
        var channelIds = channels.Select(c => c.Id).ToHashSet();
        var measurements = Measurements
            .Where(m => pointIds.Contains(m.PointId))
            .DistinctBy(m => m.Id)
            .Take(Measurement.MaxCount)
            .Select(m => m.Sanitized(channelIds))
            .ToList();
        var weather = Weather
            .DistinctBy(w => w.Id)
            .Take(WeatherObservation.MaxCount)
            .Select(w => w.Sanitized())
            .ToList();

        return new MeasurementLog(points, devices, channels, measurements, weather);
    }
}
