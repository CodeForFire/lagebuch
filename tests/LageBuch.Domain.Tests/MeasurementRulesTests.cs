using System.Globalization;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.Domain.Tests;

public class MeasurementRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(1));

    private static readonly GasDefinition O2 = new("O₂", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);

    [Fact]
    public void A_range_of_a_rising_gas_is_judged_by_its_high_end()
    {
        var co = Channel(GasDefinition.DefaultCo);

        Assert.Equal(CoSeverity.Dangerous, MeasurementRules.SeverityOf(co, new MeasuredValue(15m, 70m)));
        Assert.Equal(CoSeverity.Normal, MeasurementRules.SeverityOf(co, MeasuredValue.Single(15m)));
    }

    [Fact]
    public void A_range_of_a_falling_gas_is_judged_by_its_low_end()
    {
        var o2 = Channel(O2);

        Assert.Equal(CoSeverity.Dangerous, MeasurementRules.SeverityOf(o2, new MeasuredValue(16.5m, 20.9m)));
        Assert.Equal(CoSeverity.Normal, MeasurementRules.SeverityOf(o2, MeasuredValue.Single(20.9m)));
    }

    [Fact]
    public void A_value_is_implausible_when_either_end_is_beyond_the_limit()
    {
        var co = Channel(GasDefinition.DefaultCo);

        Assert.True(MeasurementRules.IsImplausible(co, new MeasuredValue(10m, 9999m)));
        Assert.False(MeasurementRules.IsImplausible(co, new MeasuredValue(10m, 2000m)));
    }

    [Fact]
    public void A_range_typed_the_wrong_way_round_is_turned_around_and_rounded()
    {
        var value = new MeasuredValue(14.456m, 8m);

        Assert.Equal(8m, value.Low);
        Assert.Equal(14.46m, value.High);
        Assert.True(value.IsRange);
        Assert.False(MeasuredValue.Single(3m).IsRange);
    }

    [Fact]
    public void A_negative_or_oversized_value_is_rejected_and_TryCreate_says_so()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeasuredValue(-1m, 3m));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasuredValue.Single(GasDefinition.MaxValue + 1));
        Assert.False(MeasuredValue.TryCreate(1m, -3m, out var rejected));
        Assert.Null(rejected);
        Assert.True(MeasuredValue.TryCreate(3m, 1m, out var accepted));
        Assert.Equal(new MeasuredValue(1m, 3m), accepted);
    }

    [Fact]
    public void A_value_is_labelled_with_a_dash_between_the_ends_and_the_band_word_only_when_in_a_band()
    {
        var co = Channel(GasDefinition.DefaultCo);
        var ex = Channel(new GasDefinition("Ex", "%UEG", GasDirection.Rising, 20m, 40m, null, 100m));

        Assert.Equal("CO 38 ppm (Erhöht)", MeasurementLabels.NamedValue(co, MeasuredValue.Single(38m)));
        Assert.Equal("Ex 8–14,5 %UEG", MeasurementLabels.NamedValue(ex, new MeasuredValue(8m, 14.5m)));
        Assert.Equal("CO 250 ppm (Lebensgefährlich)", MeasurementLabels.NamedValue(co, MeasuredValue.Single(250m)));
    }

    [Fact]
    public void Latest_leaves_out_an_outlier_and_falls_back_to_the_value_before_it()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "Kellerabgang", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        MeasurementFixture.Record(incident, clock, op, point.Id, T0, (co.Id, 12m));
        var outlier = MeasurementFixture.Record(incident, clock, op, point.Id, T0.AddMinutes(5), (co.Id, 900m));

        Assert.Equal(900m, Assert.Single(MeasurementRules.Latest(incident.MeasurementLog, point.Id)).Value.High);

        incident.AssessMeasurement(clock, op, outlier.Id, MeasurementStatus.Outlier, "Fachberater Muster", "Sensor übersteuert");

        var latest = Assert.Single(MeasurementRules.Latest(incident.MeasurementLog, point.Id));
        Assert.Equal(MeasuredValue.Single(12m), latest.Value);
        Assert.Equal(co.Id, latest.ChannelId);
    }

    [Fact]
    public void Latest_goes_by_measured_time_and_keeps_points_apart()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var first = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var second = incident.AddMeasuringPoint(clock, op, "MP 2", null, MeasurementZone.Cordon, null);
        var co = incident.MeasurementChannels[0];
        MeasurementFixture.Record(incident, clock, op, first.Id, T0.AddMinutes(10), (co.Id, 30m));
        MeasurementFixture.Record(incident, clock, op, first.Id, T0, (co.Id, 5m));
        MeasurementFixture.Record(incident, clock, op, second.Id, T0, (co.Id, 2m));

        var all = MeasurementRules.Latest(incident.MeasurementLog, pointId: null);

        Assert.Equal(new (Guid, decimal)[] { (first.Id, 30m), (second.Id, 2m) }, all.Select(l => (l.PointId, l.Value.Low)));
        Assert.Equal(2m, Assert.Single(MeasurementRules.Latest(incident.MeasurementLog, second.Id)).Value.Low);
    }

    [Fact]
    public void RunningNumber_counts_per_point_by_measured_time_then_recording_order()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var other = incident.AddMeasuringPoint(clock, op, "MP 2", null, MeasurementZone.Danger, null);
        var co = incident.MeasurementChannels[0];
        var late = MeasurementFixture.Record(incident, clock, op, point.Id, T0.AddMinutes(20), (co.Id, 1m));
        var elsewhere = MeasurementFixture.Record(incident, clock, op, other.Id, T0, (co.Id, 1m));
        var early = MeasurementFixture.Record(incident, clock, op, point.Id, T0, (co.Id, 1m));
        var sameTime = MeasurementFixture.Record(incident, clock, op, point.Id, T0, (co.Id, 2m));
        var log = incident.MeasurementLog;

        Assert.Equal(1, MeasurementRules.RunningNumber(log, early));
        Assert.Equal(2, MeasurementRules.RunningNumber(log, sameTime));
        Assert.Equal(3, MeasurementRules.RunningNumber(log, late));
        Assert.Equal(1, MeasurementRules.RunningNumber(log, elsewhere));
    }

    [Fact]
    public void RunningNumber_refuses_a_messung_the_log_does_not_hold()
    {
        var (incident, clock, op) = MeasurementFixture.NewIncident();
        var point = incident.AddMeasuringPoint(clock, op, "MP 1", null, MeasurementZone.Danger, null);
        var recorded = MeasurementFixture.Record(incident, clock, op, point.Id, T0, (incident.MeasurementChannels[0].Id, 1m));
        var stranger = recorded with { Id = Guid.NewGuid() };

        var refused = Assert.Throws<ArgumentException>(() => MeasurementRules.RunningNumber(incident.MeasurementLog, stranger));

        Assert.StartsWith($"Messung {stranger.Id} ist nicht im Messprotokoll.", refused.Message, StringComparison.Ordinal);
        Assert.Equal("measurement", refused.ParamName);
    }

    [Fact]
    public void An_out_of_range_end_names_the_limit_in_german()
    {
        var low = Assert.Throws<ArgumentOutOfRangeException>(() => new MeasuredValue(-1m, 3m));
        var high = Assert.Throws<ArgumentOutOfRangeException>(() => new MeasuredValue(3m, GasDefinition.MaxValue + 1));

        Assert.StartsWith("Messwert liegt außerhalb von 0 bis 100000.", low.Message, StringComparison.Ordinal);
        Assert.Equal("low", low.ParamName);
        Assert.StartsWith("Messwert liegt außerhalb von 0 bis 100000.", high.Message, StringComparison.Ordinal);
        Assert.Equal("high", high.ParamName);
    }

    [Fact]
    public void A_value_deconstructs_into_its_low_and_high_end()
    {
        var (low, high) = new MeasuredValue(14m, 8m);

        Assert.Equal(8m, low);
        Assert.Equal(14m, high);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("300", "300")]
    [InlineData("12.34", "12.3")]
    [InlineData("12.35", "12.4")]
    public void A_wind_speed_from_0_to_300_is_kept_rounded_to_a_tenth(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), WeatherObservation.NormalizeWindSpeed(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("-0.1")]
    [InlineData("300.1")]
    public void A_wind_speed_outside_0_to_300_is_dropped(string value) =>
        Assert.Null(WeatherObservation.NormalizeWindSpeed(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("-60", "-60")]
    [InlineData("60", "60")]
    [InlineData("-2.45", "-2.5")]
    public void A_temperature_from_minus_60_to_60_is_kept_rounded_to_a_tenth(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), WeatherObservation.NormalizeTemperature(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("-60.1")]
    [InlineData("60.1")]
    public void A_temperature_outside_minus_60_to_60_is_dropped(string value) =>
        Assert.Null(WeatherObservation.NormalizeTemperature(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Fact]
    public void A_sanitized_weather_observation_keeps_known_values_and_reads_unknown_ones_as_their_default()
    {
        var known = new WeatherObservation(Guid.NewGuid(), T0, WindDirection.NW, 12.34m, -2.45m, CloudCover.Overcast, Precipitation.Fog, " DWD ");
        var unknown = known with { Clouds = (CloudCover)42, Precipitation = (Precipitation)42 };

        Assert.Equal(known with { WindSpeedKmh = 12.3m, TemperatureCelsius = -2.5m, Source = "DWD" }, known.Sanitized());
        Assert.Equal(CloudCover.Clear, unknown.Sanitized().Clouds);
        Assert.Equal(Precipitation.None, unknown.Sanitized().Precipitation);
    }

    [Fact]
    public void A_sanitized_device_keeps_its_kind_and_reads_an_unknown_one_as_sonstiges()
    {
        var device = new MeasuringDevice(Guid.NewGuid(), DeviceKind.Pid, " PID ", " 7 ", null, " ");

        Assert.Equal(device with { Name = "PID", Number = "7", Check = null }, device.Sanitized());
        Assert.Equal(DeviceKind.Other, (device with { Kind = (DeviceKind)42 }).Sanitized()?.Kind);
        Assert.Null((device with { Name = " " }).Sanitized());
    }

    [Fact]
    public void A_sanitized_assessment_keeps_its_status_and_reads_an_unknown_one_as_unbestaetigt()
    {
        var assessment = new MeasurementAssessment(MeasurementStatus.Outlier, " Muster ", " Sensor ", T0);

        Assert.Equal(new MeasurementAssessment(MeasurementStatus.Outlier, "Muster", "Sensor", T0), assessment.Sanitized());
        Assert.Equal(MeasurementStatus.Unconfirmed, (assessment with { Status = (MeasurementStatus)42 }).Sanitized()?.Status);
        Assert.Null((assessment with { By = " " }).Sanitized());
    }

    [Fact]
    public void A_sanitized_messung_keeps_known_protection_and_mode_and_reads_unknown_ones_as_their_default()
    {
        var values = new Dictionary<Guid, MeasuredValue>();
        var measurement = new Measurement(
            Guid.NewGuid(), T0, null, Guid.NewGuid(), null, ProtectiveEquipment.Form3, MeasurementMode.Stationary, true, false, null, values, MeasurementStatus.Unconfirmed, null, [], T0, " Muster ");
        var unknown = measurement with { Protection = (ProtectiveEquipment)42, Mode = (MeasurementMode)42, RecordedBy = " " };

        var kept = measurement.Sanitized(new HashSet<Guid>());
        var defaulted = unknown.Sanitized(new HashSet<Guid>());

        Assert.Equal(ProtectiveEquipment.Form3, kept.Protection);
        Assert.Equal(MeasurementMode.Stationary, kept.Mode);
        Assert.Equal("Muster", kept.RecordedBy);
        Assert.Equal(ProtectiveEquipment.None, defaulted.Protection);
        Assert.Equal(MeasurementMode.Remote, defaulted.Mode);
        Assert.Equal(string.Empty, defaulted.RecordedBy);
    }

    private static MeasurementChannel Channel(GasDefinition definition) =>
        new(Guid.NewGuid(), definition, DeviceId: null, IsActive: true, Note: null);
}

internal static class MeasurementFixture
{
    public static readonly DateTimeOffset Start = new(2026, 11, 8, 8, 30, 0, TimeSpan.FromHours(1));

    public static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(Start);
        var op = new SessionOperator("Muster", "ELW 1");
        return (Incident.Start(clock, op), clock, op);
    }

    public static Measurement Record(
        Incident incident, FixedClock clock, SessionOperator op, Guid pointId, DateTimeOffset at, params (Guid Channel, decimal Value)[] values) =>
        incident.RecordMeasurement(
            clock,
            op,
            at,
            order: null,
            pointId,
            team: null,
            ProtectiveEquipment.None,
            MeasurementMode.Remote,
            odour: false,
            deposits: false,
            note: null,
            values.ToDictionary(v => v.Channel, v => MeasuredValue.Single(v.Value)));
}
