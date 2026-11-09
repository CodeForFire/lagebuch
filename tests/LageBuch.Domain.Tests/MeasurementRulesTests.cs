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
