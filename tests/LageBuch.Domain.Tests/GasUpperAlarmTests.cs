using System.Globalization;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Tests;

// #927: O2 falls, and is dangerous when it rises too. A falling gas may carry an upper alarm, the
// enrichment limit, whichever instrument the reading comes from -- a multi-gas meter or a
// Gastec 31B tube reading 6-24 %.
public class GasUpperAlarmTests
{
    private static readonly GasDefinition O2 = new("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m, UpperAlarm: 23m);

    [Theory]
    [InlineData("24", CoSeverity.Dangerous)]
    [InlineData("23", CoSeverity.Dangerous)]
    [InlineData("22.99", CoSeverity.Normal)]
    [InlineData("22", CoSeverity.Normal)]
    [InlineData("20.9", CoSeverity.Normal)]
    [InlineData("19", CoSeverity.Elevated)]
    [InlineData("10", CoSeverity.Lethal)]
    public void O2_at_24_Vol_percent_is_dangerous_and_the_falling_bands_still_apply(string value, CoSeverity expected)
    {
        Assert.Equal(expected, GasSeverityClassifier.SeverityOf(O2, decimal.Parse(value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_falling_gas_without_an_upper_alarm_never_colours_a_high_reading()
    {
        Assert.Equal(CoSeverity.Normal, GasSeverityClassifier.SeverityOf(O2 with { UpperAlarm = null }, 24m));
    }

    [Fact]
    public void An_upper_alarm_on_a_rising_gas_is_ignored_by_the_classifier()
    {
        var co = GasDefinition.DefaultCo with { UpperAlarm = 5m };

        Assert.Equal(CoSeverity.Normal, GasSeverityClassifier.SeverityOf(co, 10m));
    }

    [Fact]
    public void An_enriched_O2_reading_is_the_worst_gas_next_to_an_elevated_CO()
    {
        var gases = new List<IncidentGas> { new(Guid.NewGuid(), GasDefinition.DefaultCo), new(Guid.NewGuid(), O2) };
        var values = new Dictionary<Guid, decimal> { [gases[0].Id] = 25, [gases[1].Id] = 24 };

        var worst = GasSeverityClassifier.Worst(gases, values);

        Assert.NotNull(worst);
        Assert.Equal("O2", worst.Gas.Name);
        Assert.Equal(CoSeverity.Dangerous, worst.Severity);
    }

    [Fact]
    public void An_upper_alarm_above_the_mildest_band_and_within_plausibility_is_in_order()
    {
        Assert.True(O2.ThresholdsInOrder);
        Assert.True((O2 with { UpperAlarm = 25m }).ThresholdsInOrder);
        Assert.True((O2 with { Elevated = null, Dangerous = null, Lethal = null, PlausibleUpTo = null }).ThresholdsInOrder);
    }

    [Theory]
    [InlineData("19")]
    [InlineData("18")]
    [InlineData("25.01")]
    public void An_upper_alarm_at_or_below_the_mildest_band_or_beyond_plausibility_is_out_of_order(string upper)
    {
        Assert.False((O2 with { UpperAlarm = decimal.Parse(upper, CultureInfo.InvariantCulture) }).ThresholdsInOrder);
    }

    [Fact]
    public void Without_Erhoeht_the_upper_alarm_must_clear_the_next_band_that_is_set()
    {
        var withoutElevated = O2 with { Elevated = null };

        Assert.True((withoutElevated with { UpperAlarm = 18m }).ThresholdsInOrder);
        Assert.False((withoutElevated with { UpperAlarm = 17m }).ThresholdsInOrder);
        Assert.False((withoutElevated with { Dangerous = null, UpperAlarm = 10m }).ThresholdsInOrder);
    }

    [Fact]
    public void An_upper_alarm_on_a_rising_gas_does_not_make_its_bands_out_of_order()
    {
        Assert.True((GasDefinition.DefaultCo with { UpperAlarm = 1m }).ThresholdsInOrder);
    }

    [Fact]
    public void Sanitising_keeps_the_upper_alarm_of_a_falling_gas()
    {
        Assert.Equal(23m, O2.Sanitized()?.UpperAlarm);
    }

    [Fact]
    public void Sanitising_drops_the_upper_alarm_of_a_rising_or_unknown_direction_gas()
    {
        Assert.Null((O2 with { Direction = GasDirection.Rising, Elevated = null, Dangerous = null, Lethal = null }).Sanitized()?.UpperAlarm);
        Assert.Null((O2 with { Direction = (GasDirection)7 }).Sanitized()?.UpperAlarm);
    }

    [Fact]
    public void Sanitising_drops_an_upper_alarm_out_of_order_but_keeps_the_bands()
    {
        var clean = (O2 with { UpperAlarm = 18m }).Sanitized();

        Assert.NotNull(clean);
        Assert.Null(clean.UpperAlarm);
        Assert.Equal(19m, clean.Elevated);
        Assert.Equal(17m, clean.Dangerous);
        Assert.Equal(10m, clean.Lethal);
    }

    [Fact]
    public void Sanitising_drops_an_oversized_upper_alarm_and_rounds_a_fine_one()
    {
        Assert.Null((O2 with { PlausibleUpTo = null, UpperAlarm = GasDefinition.MaxValue + 1 }).Sanitized()?.UpperAlarm);
        Assert.Null((O2 with { UpperAlarm = -1m }).Sanitized()?.UpperAlarm);
        Assert.Equal(23.46m, (O2 with { UpperAlarm = 23.455m }).Sanitized()?.UpperAlarm);
    }

    [Fact]
    public void Sanitising_bands_out_of_order_keeps_an_upper_alarm_that_still_fits()
    {
        var clean = (O2 with { Elevated = 10m, Lethal = 19m }).Sanitized();

        Assert.NotNull(clean);
        Assert.Null(clean.Elevated);
        Assert.Equal(23m, clean.UpperAlarm);
    }
}
