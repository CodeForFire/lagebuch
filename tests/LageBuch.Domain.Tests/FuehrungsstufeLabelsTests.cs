using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class FuehrungsstufeLabelsTests
{
    [Theory]
    [InlineData(Fuehrungsstufe.A, "A")]
    [InlineData(Fuehrungsstufe.B, "B")]
    [InlineData(Fuehrungsstufe.C, "C")]
    [InlineData(Fuehrungsstufe.D, "D")]
    public void Each_Stufe_is_spelt_as_its_letter(Fuehrungsstufe stufe, string expected) =>
        Assert.Equal(expected, FuehrungsstufeLabels.Of(stufe));
}
