using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.Domain.Tests;

public class MeasurementLabelsTests
{
    [Fact]
    public void A_rising_gas_lists_each_band_it_has_with_its_word_and_unit()
    {
        Assert.Equal(
            "Erhöht ≥ 20 · Gefährlich ≥ 60 · Lebensgefährlich ≥ 200 ppm",
            MeasurementLabels.Bands(GasDefinition.DefaultCo));
    }

    [Fact]
    public void A_falling_gas_counts_down_and_a_missing_band_is_skipped()
    {
        var o2 = new GasDefinition("O₂", "Vol-%", GasDirection.Falling, 19m, null, 10.5m, 25m);

        Assert.Equal("Erhöht ≤ 19 · Lebensgefährlich ≤ 10,5 Vol-%", MeasurementLabels.Bands(o2));
    }

    [Fact]
    public void A_gas_without_bands_or_unit_says_so()
    {
        var gas = new GasDefinition("pH", string.Empty, GasDirection.Rising, null, null, null, null);

        Assert.Equal("ohne Schwellen", MeasurementLabels.Bands(gas));
        Assert.Equal("Erhöht ≥ 8", MeasurementLabels.Bands(gas with { Elevated = 8m }));
    }

    [Theory]
    [InlineData(GasDirection.Rising, "steigend")]
    [InlineData(GasDirection.Falling, "fallend")]
    public void The_direction_has_its_german_word(GasDirection direction, string label) =>
        Assert.Equal(label, MeasurementLabels.Direction(direction));

    [Fact]
    public void An_unknown_direction_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Direction((GasDirection)7));
}
