using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Measurements;

namespace LageBuch.Domain.Tests;

public class MeasurementLogAggregateTests
{
    private static readonly GasDefinition O2 = new("O₂", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);

    private static readonly GasDefinition Ex = new("Ex", "%UEG", GasDirection.Rising, 20m, 40m, null, 100m);

    private static readonly GasDefinition H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);

    [Fact]
    public void The_first_point_copies_the_stammdaten_gases_in_as_channels_by_value()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var stammdaten = new List<GasDefinition> { GasDefinition.DefaultCo, O2, Ex };

        incident.AddMeasuringPoint(clock, op, "Kellerabgang", null, MeasurementZone.Danger, stammdaten);
        stammdaten.Clear();
        stammdaten.Add(H2S);

        Assert.Equal(["CO", "O₂", "Ex"], incident.MeasurementChannels.Select(c => c.Name));
        Assert.Equal(O2, incident.MeasurementChannels[1].Definition);
        Assert.All(incident.MeasurementChannels, c => Assert.True(c.IsActive));
        Assert.Empty(incident.Gases);
    }

    [Fact]
    public void Only_an_einsatz_without_channels_is_seeded()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, [O2]);

        incident.AddMeasuringPoint(clock, op, "MP 2", null, MeasurementZone.Danger, [H2S, Ex]);
        incident.AddMeasurementChannel(clock, op, Ex, deviceId: null, note: null, seedGases: [H2S]);

        Assert.Equal(["O₂", "Ex"], incident.MeasurementChannels.Select(c => c.Name));
    }

    [Fact]
    public void An_empty_stammdaten_list_seeds_CO_at_the_DGUV_bands()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, []);

        Assert.Equal(GasDefinition.DefaultCo, Assert.Single(incident.MeasurementChannels).Definition);
    }

    [Fact]
    public void The_first_channel_seeds_the_stammdaten_first_then_adds_itself()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var added = incident.AddMeasurementChannel(clock, op, H2S, deviceId: null, note: " Röhrchen ", seedGases: [O2]);

        Assert.Equal(["O₂", "H2S"], incident.MeasurementChannels.Select(c => c.Name));
        Assert.Equal("Röhrchen", added.Note);
        Assert.Equal("Messgröße angelegt: H2S (ppm)", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void Channels_stop_at_ten_and_a_failed_add_changes_nothing()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        for (var i = 1; i < MeasurementChannel.MaxCount; i++)
        {
            incident.AddMeasurementChannel(clock, op, H2S with { Name = $"G{i}" }, null, null, null);
        }

        Assert.Equal(10, incident.MeasurementChannels.Count);
        Assert.Throws<InvalidOperationException>(() => incident.AddMeasurementChannel(clock, op, H2S, null, null, null));
        Assert.Equal(10, incident.MeasurementChannels.Count);
    }

    [Fact]
    public void A_channel_name_can_be_taken_only_once_whatever_the_case()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, [H2S]);

        Assert.Throws<InvalidOperationException>(() => incident.AddMeasurementChannel(clock, op, H2S with { Name = "h2s" }, null, null, null));
        Assert.Single(incident.MeasurementChannels);
    }

    [Fact]
    public void A_channel_with_readings_cannot_be_removed_only_deactivated()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, [GasDefinition.DefaultCo, O2]);
        var co = incident.MeasurementChannels[0];
        var o2 = incident.MeasurementChannels[1];
        MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 5m));

        var refused = Assert.Throws<InvalidOperationException>(() => incident.RemoveMeasurementChannel(co.Id));
        Assert.Equal("Messgröße hat Messwerte – nur deaktivieren.", refused.Message);

        var deactivated = incident.UpdateMeasurementChannel(co.Id, deviceId: null, isActive: false, note: null);
        Assert.False(deactivated.IsActive);
        incident.RemoveMeasurementChannel(o2.Id);
        Assert.Equal([co.Id], incident.MeasurementChannels.Select(c => c.Id));
    }

    [Fact]
    public void A_channel_keeps_its_gas_once_it_has_readings()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, [GasDefinition.DefaultCo, O2]);
        var co = incident.MeasurementChannels[0];
        var o2 = incident.MeasurementChannels[1];
        MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 5m));

        Assert.Throws<InvalidOperationException>(() => incident.UpdateMeasurementChannel(co.Id, null, true, null, GasDefinition.LegacyCo));
        var changed = incident.UpdateMeasurementChannel(o2.Id, null, true, null, O2 with { Elevated = 19.5m });

        Assert.Equal(19.5m, changed.Definition.Elevated);
        Assert.Equal(GasDefinition.DefaultCo, incident.MeasurementChannels[0].Definition);
    }

    [Fact]
    public void Removing_a_device_unlinks_the_channels_it_measured()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var device = incident.AddMeasuringDevice(DeviceKind.MultiGas, "X-am 8000", "ARFA-0001", "GW-Mess", "Begasung 07.11.");
        var channel = incident.AddMeasurementChannel(clock, op, H2S, device.Id, null, [GasDefinition.DefaultCo]);

        incident.RemoveMeasuringDevice(device.Id);

        Assert.Empty(incident.MeasuringDevices);
        Assert.Null(incident.MeasurementChannels.Single(c => c.Id == channel.Id).DeviceId);
        Assert.Throws<KeyNotFoundException>(() => incident.AddMeasurementChannel(clock, op, Ex, device.Id, null, null));
    }

    [Fact]
    public void A_value_may_only_name_an_active_channel()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        incident.UpdateMeasurementChannel(co.Id, null, isActive: false, note: null);

        Assert.Throws<ArgumentException>(() => MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 5m)));
        Assert.Throws<ArgumentException>(() => MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (Guid.NewGuid(), 5m)));
        Assert.Empty(incident.Measurements);
    }

    [Fact]
    public void A_measurement_has_to_say_something()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);

        Assert.Throws<ArgumentException>(() => MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now));
        var smelled = incident.RecordMeasurement(
            clock, op, clock.Now, null, point.Id, null, ProtectiveEquipment.None, MeasurementMode.Remote, odour: true, deposits: false, note: "  ", new Dictionary<Guid, MeasuredValue>());

        Assert.Null(smelled.Note);
        Assert.Equal("Messung MP 1 #1 (ohne Sonderausrüstung, abgesetzt (Messtrupp)) – Geruch wahrnehmbar", incident.Journal[^1].Text);
    }

    [Fact]
    public void Recording_a_measurement_writes_it_to_the_etb_in_full_and_never_hidden()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "Kellerabgang", "Musterweg 1", MeasurementZone.Danger, [GasDefinition.DefaultCo, O2, Ex]);
        var (co, o2, ex) = (incident.MeasurementChannels[0], incident.MeasurementChannels[1], incident.MeasurementChannels[2]);
        MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now.AddMinutes(-20), (co.Id, 3m));
        MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now.AddMinutes(-10), (co.Id, 9m));

        var measurement = incident.RecordMeasurement(
            clock,
            op,
            clock.Now,
            " MA 04 ",
            point.Id,
            "Florian Musterstadt 40/1 · Angriffstrupp",
            ProtectiveEquipment.ScbaForm1,
            MeasurementMode.Remote,
            odour: true,
            deposits: false,
            note: "stechender Geruch",
            new Dictionary<Guid, MeasuredValue>
            {
                [ex.Id] = new MeasuredValue(8m, 14m),
                [co.Id] = MeasuredValue.Single(38m),
                [o2.Id] = MeasuredValue.Single(20.1m),
            });

        var entry = incident.Journal[^1];
        Assert.Equal(
            "Messung MA 04 · Kellerabgang #3 (Florian Musterstadt 40/1 · Angriffstrupp, PA + Form 1, abgesetzt (Messtrupp)): CO 38 ppm (Erhöht), O₂ 20,1 Vol-%, Ex 8–14 %UEG – Geruch wahrnehmbar – stechender Geruch",
            entry.Text);
        Assert.Equal(EtbDirection.Measurement, entry.Direction);
        Assert.Equal("MA 04", measurement.Order);
        Assert.Equal(MeasurementStatus.Unconfirmed, measurement.Status);
        Assert.Equal(clock.Now, measurement.RecordedAt);
        Assert.Equal(op.Display, measurement.RecordedBy);
    }

    [Fact]
    public void Opening_a_point_writes_its_name_and_zone()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var point = incident.AddMeasuringPoint(clock, op, "  Kellerabgang ", " ", MeasurementZone.Transition, null);

        Assert.Equal("Kellerabgang", point.Name);
        Assert.Null(point.Location);
        Assert.Equal("Messpunkt angelegt: Kellerabgang (Übergangsbereich)", incident.Journal[^1].Text);
    }

    [Fact]
    public void Updating_a_point_writes_an_etb_line_only_when_something_changed()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var lines = incident.Journal.Count;

        incident.UpdateMeasuringPoint(clock, op, point.Id, "MP 1", null, MeasurementZone.Danger);
        Assert.Equal(lines, incident.Journal.Count);

        incident.UpdateMeasuringPoint(clock, op, point.Id, "MP 1 Kellerabgang", null, MeasurementZone.Cordon);
        Assert.Equal("Messpunkt geändert: MP 1 → MP 1 Kellerabgang (Absperrbereich)", incident.Journal[^1].Text);
    }

    [Fact]
    public void Text_is_cut_to_its_cap_rather_than_rejected()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var point = incident.AddMeasuringPoint(clock, op, new string('M', 100), null, MeasurementZone.Danger, null);
        var device = incident.AddMeasuringDevice(DeviceKind.Pid, "PID", new string('1', 100), null, null);

        Assert.Equal(MeasuringPoint.MaxNameLength, point.Name.Length);
        Assert.Equal(MeasuringDevice.MaxNumberLength, device.Number?.Length);
        Assert.Throws<ArgumentException>(() => incident.AddMeasuringPoint(clock, op, "   ", null, MeasurementZone.Danger, null));
    }

    [Fact]
    public void Assessing_a_measurement_sets_its_status_and_writes_who_judged_it()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "Kellerabgang", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 38m));
        clock.Now = clock.Now.AddMinutes(3);

        var assessed = incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Confirmed, " Fachberater Muster ", "plausibel, Nachmessung bestätigt");

        Assert.Equal(MeasurementStatus.Confirmed, assessed.Status);
        Assert.Equal(new MeasurementAssessment(MeasurementStatus.Confirmed, "Fachberater Muster", "plausibel, Nachmessung bestätigt", clock.Now), assessed.Assessment);
        Assert.Equal("Messung Kellerabgang #1 bewertet: bestätigt – plausibel, Nachmessung bestätigt (Fachberater Muster)", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.Measurement, incident.Journal[^1].Direction);
        Assert.Throws<ArgumentException>(() => incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Outlier, " ", null));
    }

    [Fact]
    public void A_correction_keeps_the_previous_value_and_writes_the_change_with_its_reason()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "Kellerabgang", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 140m));
        clock.Now = clock.Now.AddMinutes(2);

        var corrected = incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(14m), "Ablesefehler");

        Assert.Equal(MeasuredValue.Single(14m), corrected.Values[co.Id]);
        var correction = Assert.Single(corrected.Corrections);
        Assert.Equal(new MeasurementCorrection(co.Id, MeasuredValue.Single(140m), MeasuredValue.Single(14m), "Ablesefehler", op.Display, clock.Now), correction);
        Assert.Equal("Messwert korrigiert: Kellerabgang #1: CO 140 → 14 ppm (Grund: Ablesefehler)", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.Measurement, incident.Journal[^1].Direction);
        Assert.Same(corrected, incident.Measurements[0]);
    }

    [Fact]
    public void A_correction_needs_a_reason_and_an_actual_change()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 140m));

        Assert.Throws<ArgumentException>(() => incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(14m), " "));
        Assert.Throws<ArgumentException>(() => incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(140m), "Ablesefehler"));
        Assert.Empty(incident.Measurements[0].Corrections);
    }

    [Fact]
    public void A_corrected_away_value_is_gone_from_the_values_but_kept_in_the_correction()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 140m));

        var corrected = incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, value: null, "falscher Messpunkt");

        Assert.Empty(corrected.Values);
        Assert.Equal(MeasuredValue.Single(140m), Assert.Single(corrected.Corrections).Previous);
        Assert.Equal("Messwert korrigiert: MP 1 #1: CO 140 ppm gelöscht (Grund: falscher Messpunkt)", incident.Journal[^1].Text);
        Assert.Throws<InvalidOperationException>(() => incident.RemoveMeasurementChannel(co.Id));
    }

    [Fact]
    public void A_weather_observation_is_written_to_the_etb()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var observation = incident.AddWeatherObservation(
            clock, op, clock.Now, WindDirection.W, 12m, 14m, CloudCover.Clear, Precipitation.None, "Handwindmesser");

        Assert.Equal(12m, observation.WindSpeedKmh);
        Assert.Equal("Wetter: Wind aus W, 12 km/h, 14 °C, klar, kein Niederschlag (Handwindmesser)", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.Measurement, incident.Journal[^1].Direction);

        incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.NE, null, -2.5m, CloudCover.Overcast, Precipitation.Snow, null);
        Assert.Equal("Wetter: Wind aus NO, -2,5 °C, bedeckt, Schnee", incident.Journal[^1].Text);
    }

    [Fact]
    public void A_weather_value_out_of_range_is_rejected()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, 301m, null, CloudCover.Clear, Precipitation.None, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, null, -61m, CloudCover.Clear, Precipitation.None, null));
        Assert.Empty(incident.WeatherObservations);
    }

    [Fact]
    public void A_closed_incident_rejects_every_messprotokoll_change()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var device = incident.AddMeasuringDevice(DeviceKind.MultiGas, "X-am", null, null, null);
        var co = incident.MeasurementChannels[0];
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 5m));
        incident.Close(clock, op);

        Action[] changes =
        [
            () => incident.AddMeasuringPoint(clock, op, "MP 2", null, MeasurementZone.Danger, null),
            () => incident.UpdateMeasuringPoint(clock, op, point.Id, "MP 1a", null, MeasurementZone.Danger),
            () => incident.AddMeasuringDevice(DeviceKind.Pid, "PID", null, null, null),
            () => incident.UpdateMeasuringDevice(device.Id, DeviceKind.Pid, "PID", null, null, null),
            () => incident.RemoveMeasuringDevice(device.Id),
            () => incident.AddMeasurementChannel(clock, op, H2S, null, null, null),
            () => incident.UpdateMeasurementChannel(co.Id, null, false, null),
            () => incident.RemoveMeasurementChannel(co.Id),
            () => MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 6m)),
            () => incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Confirmed, "Muster", null),
            () => incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(7m), "Ablesefehler"),
            () => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null),
        ];

        Assert.All(changes, change => Assert.Throws<IncidentClosedException>(change));
        Assert.Single(incident.Measurements);
    }

    [Fact]
    public void An_unknown_id_throws()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);

        Assert.Throws<KeyNotFoundException>(() => incident.UpdateMeasuringPoint(clock, op, Guid.NewGuid(), "X", null, MeasurementZone.Danger));
        Assert.Throws<KeyNotFoundException>(() => incident.RemoveMeasuringDevice(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => incident.RemoveMeasurementChannel(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => MeasurementFixture.Record(incident, clock, op, Guid.NewGuid(), clock.Now, (incident.MeasurementChannels[0].Id, 1m)));
        Assert.Throws<KeyNotFoundException>(() => incident.AssessMeasurement(clock, op, Guid.NewGuid(), MeasurementStatus.Confirmed, "Muster", null));
    }

    [Fact]
    public void The_measurement_log_is_a_copy_that_does_not_follow_later_changes()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);

        var log = incident.MeasurementLog;
        incident.AddMeasuringPoint(clock, op, "MP 2", null, MeasurementZone.Danger, null);

        Assert.Single(log.Points);
        Assert.Equal(2, incident.MeasurementLog.Points.Count);
    }

    [Fact]
    public void Rehydrate_round_trips_a_measurement_log()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", "Hof", MeasurementZone.Danger, [GasDefinition.DefaultCo, Ex]);
        var device = incident.AddMeasuringDevice(DeviceKind.MultiGas, "X-am", "1", "GW-Mess", null);
        incident.UpdateMeasurementChannel(incident.MeasurementChannels[1].Id, device.Id, true, "Sensor neu");
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (incident.MeasurementChannels[0].Id, 40m));
        incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Outlier, "Muster", "Sensor");
        incident.CorrectMeasurementValue(clock, op, measurement.Id, incident.MeasurementChannels[0].Id, MeasuredValue.Single(4m), "Komma");
        incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.SW, 8m, 10m, CloudCover.UpToHalf, Precipitation.Rain, "DWD");
        var log = incident.MeasurementLog;

        var back = RehydrateWith(log);

        Assert.Equal(log.Points, back.MeasuringPoints);
        Assert.Equal(log.Devices, back.MeasuringDevices);
        Assert.Equal(log.Channels, back.MeasurementChannels);
        Assert.Equal(log.Weather, back.WeatherObservations);
        var m = Assert.Single(back.Measurements);
        Assert.Equal(MeasurementStatus.Outlier, m.Status);
        Assert.Equal(log.Measurements[0].Assessment, m.Assessment);
        Assert.Equal(log.Measurements[0].Corrections, m.Corrections);
        Assert.Equal(log.Measurements[0].Values, m.Values);
    }

    [Fact]
    public void Rehydrate_drops_entries_over_the_caps_duplicates_and_dangling_references()
    {
        var t = MeasurementFixture.Start;
        var points = Enumerable.Range(0, MeasuringPoint.MaxCount + 2)
            .Select(i => new MeasuringPoint(Guid.NewGuid(), $"MP {i}", null, MeasurementZone.Danger))
            .ToList();
        points.Insert(1, points[0] with { Name = "Doppelt" });
        points.Insert(2, new MeasuringPoint(Guid.NewGuid(), "  ", null, MeasurementZone.Danger));
        points.Insert(3, new MeasuringPoint(Guid.NewGuid(), "Unbekannter Bereich", null, (MeasurementZone)99));
        var devices = Enumerable.Range(0, MeasuringDevice.MaxCount + 1)
            .Select(i => new MeasuringDevice(Guid.NewGuid(), DeviceKind.MultiGas, $"Gerät {i}", null, null, null))
            .ToList();
        var channels = Enumerable.Range(0, MeasurementChannel.MaxCount + 2)
            .Select(i => new MeasurementChannel(Guid.NewGuid(), H2S with { Name = $"G{i}" }, null, true, null))
            .ToList();
        channels[0] = channels[0] with { DeviceId = Guid.NewGuid() };
        channels.Insert(2, channels[1] with { Id = Guid.NewGuid(), Definition = H2S with { Name = "g1" } });
        var droppedChannel = channels[^1];
        var keptPoint = points[0];
        var measurements = new List<Measurement>
        {
            Messung(keptPoint.Id, t, new() { [channels[0].Id] = MeasuredValue.Single(1m), [droppedChannel.Id] = MeasuredValue.Single(2m) }),
            Messung(Guid.NewGuid(), t, new() { [channels[0].Id] = MeasuredValue.Single(3m) }),
        };
        measurements.Add(measurements[0] with { Note = "Doppelt" });
        var weather = new List<WeatherObservation>
        {
            new(Guid.NewGuid(), t, (WindDirection)42, 999m, 99m, CloudCover.Clear, Precipitation.None, "  "),
        };

        var incident = RehydrateWith(new MeasurementLog(points, devices, channels, measurements, weather));

        Assert.Equal(MeasuringPoint.MaxCount, incident.MeasuringPoints.Count);
        Assert.Equal("MP 0", incident.MeasuringPoints[0].Name);
        Assert.DoesNotContain(incident.MeasuringPoints, p => p.Name == "Doppelt");
        Assert.Equal(MeasurementZone.Unknown, incident.MeasuringPoints.Single(p => p.Name == "Unbekannter Bereich").Zone);
        Assert.Equal(MeasuringDevice.MaxCount, incident.MeasuringDevices.Count);
        Assert.Equal(MeasurementChannel.MaxCount, incident.MeasurementChannels.Count);
        Assert.DoesNotContain(incident.MeasurementChannels, c => c.Name == "g1");
        Assert.DoesNotContain(incident.MeasurementChannels, c => c.Id == droppedChannel.Id);
        Assert.Null(incident.MeasurementChannels[0].DeviceId);
        var kept = Assert.Single(incident.Measurements);
        Assert.Equal([channels[0].Id], kept.Values.Keys);
        var observation = Assert.Single(incident.WeatherObservations);
        Assert.Null(observation.WindSpeedKmh);
        Assert.Null(observation.TemperatureCelsius);
        Assert.Null(observation.Source);
        Assert.Equal(WindDirection.Variable, observation.Wind);
    }

    [Fact]
    public void Rehydrate_without_a_measurement_log_starts_empty()
    {
        var incident = RehydrateWith(null);

        Assert.Empty(incident.MeasuringPoints);
        Assert.Empty(incident.MeasurementChannels);
        Assert.Equal(MeasurementLog.Empty.Points, incident.MeasurementLog.Points);
    }

    [Fact]
    public void A_full_log_refuses_one_more_entry_of_each_kind_and_says_how_many_it_keeps()
    {
        var t = MeasurementFixture.Start;
        var co = new MeasurementChannel(Guid.NewGuid(), GasDefinition.DefaultCo, null, true, null);
        var points = Enumerable.Range(0, MeasuringPoint.MaxCount)
            .Select(i => new MeasuringPoint(Guid.NewGuid(), $"MP {i}", null, MeasurementZone.Danger))
            .ToList();
        var devices = Enumerable.Range(0, MeasuringDevice.MaxCount)
            .Select(i => new MeasuringDevice(Guid.NewGuid(), DeviceKind.MultiGas, $"Gerät {i}", null, null, null))
            .ToList();
        var measurements = Enumerable.Range(0, Measurement.MaxCount)
            .Select(_ => Messung(points[0].Id, t, new() { [co.Id] = MeasuredValue.Single(1m) }))
            .ToList();
        var weather = Enumerable.Range(0, WeatherObservation.MaxCount)
            .Select(_ => new WeatherObservation(Guid.NewGuid(), t, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null))
            .ToList();
        var incident = RehydrateWith(new MeasurementLog(points, devices, [co], measurements, weather));
        var clock = new FixedClock(t);
        var op = new SessionOperator("Muster");

        var point = Assert.Throws<InvalidOperationException>(() => incident.AddMeasuringPoint(clock, op, "MP X", null, MeasurementZone.Danger, null));
        var device = Assert.Throws<InvalidOperationException>(() => incident.AddMeasuringDevice(DeviceKind.Pid, "PID", null, null, null));
        var measurement = Assert.Throws<InvalidOperationException>(() => MeasurementFixture.Record(incident, clock, op, points[0].Id, t, (co.Id, 2m)));
        var observation = Assert.Throws<InvalidOperationException>(
            () => incident.AddWeatherObservation(clock, op, t, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null));

        Assert.Equal("Höchstens 50 Messpunkte.", point.Message);
        Assert.Equal("Höchstens 20 Messgeräte.", device.Message);
        Assert.Equal("Höchstens 2000 Messungen.", measurement.Message);
        Assert.Equal("Höchstens 500 Wetterbeobachtungen.", observation.Message);
        Assert.Equal(MeasuringPoint.MaxCount, incident.MeasuringPoints.Count);
        Assert.Equal(MeasuringDevice.MaxCount, incident.MeasuringDevices.Count);
        Assert.Equal(Measurement.MaxCount, incident.Measurements.Count);
        Assert.Equal(WeatherObservation.MaxCount, incident.WeatherObservations.Count);
        Assert.Empty(incident.Journal);
    }

    [Fact]
    public void A_messung_takes_no_correction_past_its_cap()
    {
        var t = MeasurementFixture.Start;
        var point = new MeasuringPoint(Guid.NewGuid(), "MP 1", null, MeasurementZone.Danger);
        var co = new MeasurementChannel(Guid.NewGuid(), GasDefinition.DefaultCo, null, true, null);
        var corrections = Enumerable.Range(0, Measurement.MaxCorrectionCount)
            .Select(i => new MeasurementCorrection(co.Id, MeasuredValue.Single(i), MeasuredValue.Single(i + 1), "Ablesefehler", "Muster", t))
            .ToList();
        var full = Messung(point.Id, t, new() { [co.Id] = MeasuredValue.Single(50m) }) with { Corrections = corrections };
        var incident = RehydrateWith(new MeasurementLog([point], [], [co], [full], []));

        var refused = Assert.Throws<InvalidOperationException>(
            () => incident.CorrectMeasurementValue(new FixedClock(t), new SessionOperator("Muster"), full.Id, co.Id, MeasuredValue.Single(5m), "Komma"));

        Assert.Equal("Höchstens 50 Korrekturen je Messung.", refused.Message);
        Assert.Equal(MeasuredValue.Single(50m), incident.Measurements[0].Values[co.Id]);
    }

    [Fact]
    public void A_first_channel_named_like_a_seeded_gas_is_refused_and_seeds_nothing()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var taken = Assert.Throws<InvalidOperationException>(() => incident.AddMeasurementChannel(clock, op, O2, null, null, [H2S, O2]));

        Assert.Equal("Messgröße O₂ gibt es schon.", taken.Message);
        Assert.Empty(incident.MeasurementChannels);
    }

    [Fact]
    public void A_full_set_of_channels_says_how_many_it_keeps()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        for (var i = 1; i < MeasurementChannel.MaxCount; i++)
        {
            incident.AddMeasurementChannel(clock, op, H2S with { Name = $"G{i}" }, null, null, null);
        }

        var full = Assert.Throws<InvalidOperationException>(() => incident.AddMeasurementChannel(clock, op, Ex, null, null, null));

        Assert.Equal("Höchstens 10 Messgrößen.", full.Message);
    }

    [Fact]
    public void A_new_channel_is_active_and_one_without_unit_is_written_without_brackets()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var ph = new GasDefinition("pH", string.Empty, GasDirection.Rising, null, null, null, null);

        var added = incident.AddMeasurementChannel(clock, op, ph, null, null, [O2]);

        Assert.True(added.IsActive);
        Assert.Equal("Messgröße angelegt: pH", incident.Journal[^1].Text);
    }

    [Fact]
    public void A_channel_needs_a_named_gas_and_a_known_device()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var channel = incident.AddMeasurementChannel(clock, op, H2S, null, null, [O2]);
        var nameless = H2S with { Name = "  " };

        var added = Assert.Throws<ArgumentException>(() => incident.AddMeasurementChannel(clock, op, nameless, null, null, null));
        var updated = Assert.Throws<ArgumentException>(() => incident.UpdateMeasurementChannel(channel.Id, null, true, null, nameless));

        Assert.StartsWith("Messgröße darf nicht leer sein.", added.Message, StringComparison.Ordinal);
        Assert.StartsWith("Messgröße darf nicht leer sein.", updated.Message, StringComparison.Ordinal);
        Assert.Throws<KeyNotFoundException>(() => incident.UpdateMeasurementChannel(channel.Id, Guid.NewGuid(), true, null));
        Assert.Null(incident.MeasurementChannels.Single(c => c.Id == channel.Id).DeviceId);
    }

    [Fact]
    public void A_channel_cannot_be_renamed_to_a_name_another_one_has()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, [GasDefinition.DefaultCo, O2]);
        var o2 = incident.MeasurementChannels[1];

        var refused = Assert.Throws<InvalidOperationException>(() => incident.UpdateMeasurementChannel(o2.Id, null, true, null, O2 with { Name = "co" }));
        var renamed = incident.UpdateMeasurementChannel(o2.Id, null, true, null, O2 with { Name = "o2" });

        Assert.Equal("Messgröße co gibt es schon.", refused.Message);
        Assert.Equal("o2", renamed.Name);
    }

    [Fact]
    public void Changing_the_gas_of_a_channel_with_readings_says_why_it_is_refused()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 5m));

        var refused = Assert.Throws<InvalidOperationException>(() => incident.UpdateMeasurementChannel(co.Id, null, true, null, GasDefinition.LegacyCo));

        Assert.Equal("Messgröße hat Messwerte – Gas, Einheit und Bänder bleiben.", refused.Message);
    }

    [Fact]
    public void Recording_refuses_an_unknown_protection_or_mode_and_names_what_is_missing()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var values = new Dictionary<Guid, MeasuredValue> { [incident.MeasurementChannels[0].Id] = MeasuredValue.Single(1m) };

        var protection = Assert.Throws<ArgumentOutOfRangeException>(() => incident.RecordMeasurement(
            clock, op, clock.Now, null, point.Id, null, (ProtectiveEquipment)42, MeasurementMode.Remote, false, false, null, values));
        var mode = Assert.Throws<ArgumentOutOfRangeException>(() => incident.RecordMeasurement(
            clock, op, clock.Now, null, point.Id, null, ProtectiveEquipment.None, (MeasurementMode)42, false, false, null, values));
        var unknown = Assert.Throws<ArgumentException>(() => MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (Guid.NewGuid(), 1m)));
        var empty = Assert.Throws<ArgumentException>(() => MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now));

        Assert.Equal("protection", protection.ParamName);
        Assert.Equal("mode", mode.ParamName);
        Assert.StartsWith("Messwert für eine unbekannte oder deaktivierte Messgröße.", unknown.Message, StringComparison.Ordinal);
        Assert.StartsWith("Eine Messung braucht einen Messwert, Geruch, Niederschlag oder eine Bemerkung.", empty.Message, StringComparison.Ordinal);
        Assert.Empty(incident.Measurements);
    }

    [Fact]
    public void A_deposit_without_a_smell_is_written_as_such()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);

        incident.RecordMeasurement(
            clock, op, clock.Now, null, point.Id, null, ProtectiveEquipment.FilterForm1, MeasurementMode.FromVehicle, odour: false, deposits: true, note: null, new Dictionary<Guid, MeasuredValue>());

        Assert.Equal("Messung MP 1 #1 (Filter + Form 1, aus dem Fahrzeug) – Ruß/Niederschlag sichtbar", incident.Journal[^1].Text);
    }

    [Fact]
    public void Assessing_refuses_an_unknown_status_and_a_nameless_judge_and_writes_no_dash_without_a_reason()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (incident.MeasurementChannels[0].Id, 5m));

        var status = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AssessMeasurement(clock, op, measurement.Id, (MeasurementStatus)42, "Muster", null));
        Assert.Same(measurement, incident.Measurements[0]);
        var nameless = Assert.Throws<ArgumentException>(() => incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Outlier, " ", null));
        incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Outlier, "Muster", "  ");

        Assert.Equal("status", status.ParamName);
        Assert.StartsWith("Bewertet von darf nicht leer sein.", nameless.Message, StringComparison.Ordinal);
        Assert.Equal("by", nameless.ParamName);
        Assert.Equal("Messung MP 1 #1 bewertet: Ausreißer (Muster)", incident.Journal[^1].Text);
    }

    [Fact]
    public void A_refused_correction_says_why()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 140m));

        var reasonless = Assert.Throws<ArgumentException>(() => incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(14m), " "));
        var unchanged = Assert.Throws<ArgumentException>(() => incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(140m), "Ablesefehler"));

        Assert.StartsWith("Grund darf nicht leer sein.", reasonless.Message, StringComparison.Ordinal);
        Assert.StartsWith("Der Messwert ist unverändert.", unchanged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_can_be_added_afterwards_only_for_an_active_channel_but_corrected_on_a_deactivated_one()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, [GasDefinition.DefaultCo, O2, Ex]);
        var (co, o2, ex) = (incident.MeasurementChannels[0], incident.MeasurementChannels[1], incident.MeasurementChannels[2]);
        var measurement = MeasurementFixture.Record(incident, clock, op, point.Id, clock.Now, (co.Id, 140m));

        incident.CorrectMeasurementValue(clock, op, measurement.Id, o2.Id, MeasuredValue.Single(20.9m), "vergessen");
        Assert.Equal("Messwert korrigiert: MP 1 #1: O₂ 20,9 Vol-% nachgetragen (Grund: vergessen)", incident.Journal[^1].Text);

        incident.UpdateMeasurementChannel(co.Id, null, isActive: false, note: null);
        incident.UpdateMeasurementChannel(ex.Id, null, isActive: false, note: null);
        var corrected = incident.CorrectMeasurementValue(clock, op, measurement.Id, co.Id, MeasuredValue.Single(14m), "Ablesefehler");
        var refused = Assert.Throws<ArgumentException>(() => incident.CorrectMeasurementValue(clock, op, measurement.Id, ex.Id, MeasuredValue.Single(5m), "vergessen"));

        Assert.Equal(MeasuredValue.Single(14m), corrected.Values[co.Id]);
        Assert.StartsWith("Messgröße ist deaktiviert.", refused.Message, StringComparison.Ordinal);
        Assert.Equal("channelId", refused.ParamName);
        Assert.False(incident.Measurements[0].Values.ContainsKey(ex.Id));
    }

    [Fact]
    public void A_weather_observation_refuses_unknown_values_and_names_the_range()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var wind = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, (WindDirection)42, null, null, CloudCover.Clear, Precipitation.None, null));
        var clouds = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, null, null, (CloudCover)42, Precipitation.None, null));
        var rain = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, null, null, CloudCover.Clear, (Precipitation)42, null));
        var speed = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, 301m, null, CloudCover.Clear, Precipitation.None, null));
        var temperature = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.W, null, 61m, CloudCover.Clear, Precipitation.None, null));

        Assert.Equal("wind", wind.ParamName);
        Assert.Equal("clouds", clouds.ParamName);
        Assert.Equal("precipitation", rain.ParamName);
        Assert.StartsWith("Windgeschwindigkeit liegt außerhalb von 0 bis 300 km/h.", speed.Message, StringComparison.Ordinal);
        Assert.StartsWith("Temperatur liegt außerhalb von −60 bis 60 °C.", temperature.Message, StringComparison.Ordinal);
        Assert.Empty(incident.WeatherObservations);
    }

    [Fact]
    public void Calm_and_variable_wind_are_written_without_a_direction()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var calm = incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.Calm, null, null, CloudCover.MoreThanHalf, Precipitation.Fog, null);
        Assert.Equal("Wetter: Windstille, mehr als 50 % bedeckt, Nebel", incident.Journal[^1].Text);
        Assert.Same(calm, Assert.Single(incident.WeatherObservations));

        incident.AddWeatherObservation(clock, op, clock.Now, WindDirection.Variable, 3m, null, CloudCover.UpToHalf, Precipitation.Rain, null);
        Assert.Equal("Wetter: Wind umlaufend, 3 km/h, bis 50 % bedeckt, Regen", incident.Journal[^1].Text);
    }

    [Fact]
    public void A_point_or_device_needs_a_known_zone_or_kind_and_a_name()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();

        var zone = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddMeasuringPoint(clock, op, "MP 1", null, (MeasurementZone)42, null));
        var point = Assert.Throws<ArgumentException>(() => incident.AddMeasuringPoint(clock, op, " ", null, MeasurementZone.Danger, null));
        var kind = Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddMeasuringDevice((DeviceKind)42, "PID", null, null, null));
        var device = Assert.Throws<ArgumentException>(() => incident.AddMeasuringDevice(DeviceKind.Pid, " ", null, null, null));

        Assert.Equal("zone", zone.ParamName);
        Assert.StartsWith("Messpunkt darf nicht leer sein.", point.Message, StringComparison.Ordinal);
        Assert.Equal("kind", kind.ParamName);
        Assert.StartsWith("Messgerät darf nicht leer sein.", device.Message, StringComparison.Ordinal);
        Assert.Empty(incident.MeasuringPoints);
        Assert.Empty(incident.MeasuringDevices);
    }

    [Fact]
    public void An_unknown_id_is_named_in_the_error()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var id = Guid.NewGuid();

        var point = Assert.Throws<KeyNotFoundException>(() => incident.UpdateMeasuringPoint(clock, op, id, "X", null, MeasurementZone.Danger));
        var device = Assert.Throws<KeyNotFoundException>(() => incident.RemoveMeasuringDevice(id));
        var channel = Assert.Throws<KeyNotFoundException>(() => incident.RemoveMeasurementChannel(id));
        var measurement = Assert.Throws<KeyNotFoundException>(() => incident.AssessMeasurement(clock, op, id, MeasurementStatus.Confirmed, "Muster", null));

        Assert.Equal($"Messpunkt {id} nicht gefunden.", point.Message);
        Assert.Equal($"Messgerät {id} nicht gefunden.", device.Message);
        Assert.Equal($"Messgröße {id} nicht gefunden.", channel.Message);
        Assert.Equal($"Messung {id} nicht gefunden.", measurement.Message);
    }

    [Fact]
    public void Moving_a_point_to_another_zone_without_renaming_writes_the_name_once()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);

        incident.UpdateMeasuringPoint(clock, op, point.Id, "MP 1", null, MeasurementZone.Outside);

        Assert.Equal("Messpunkt geändert: MP 1 (außerhalb)", incident.Journal[^1].Text);
    }

    private static Measurement Messung(Guid pointId, DateTimeOffset at, Dictionary<Guid, MeasuredValue> values) =>
        new(Guid.NewGuid(), at, null, pointId, null, ProtectiveEquipment.None, MeasurementMode.Remote, false, false, null, values, MeasurementStatus.Unconfirmed, null, [], at, "Muster");

    private static Incident RehydrateWith(MeasurementLog? log) =>
        Incident.Rehydrate(
            id: Guid.NewGuid(),
            startedAt: MeasurementFixture.Start,
            state: IncidentState.Open,
            incidentNumber: null,
            keyword: null,
            street: null,
            district: null,
            status: null,
            closedAt: null,
            closedBy: null,
            checklists: [],
            journal: [],
            roles: [],
            forces: [],
            scbaTrupps: [],
            audit: [],
            timers: [],
            files: [],
            tasks: [],
            buildings: [],
            dwellings: [],
            involvedParties: [],
            measurementLog: log);
}
