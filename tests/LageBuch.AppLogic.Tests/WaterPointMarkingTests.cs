using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class WaterPointMarkingTests
{
    private static readonly WaterPoint Hydrant = new(new GeoPoint(48.1785, 11.2555), WaterPointKind.Hydrant, "UH 100");

    [Theory]
    [InlineData(null, FlowClass.Unbekannt)]
    [InlineData(0, FlowClass.Unter400)]
    [InlineData(399, FlowClass.Unter400)]
    [InlineData(400, FlowClass.Bis799)]
    [InlineData(799, FlowClass.Bis799)]
    [InlineData(800, FlowClass.Bis1599)]
    [InlineData(1599, FlowClass.Bis1599)]
    [InlineData(1600, FlowClass.Bis3199)]
    [InlineData(3199, FlowClass.Bis3199)]
    [InlineData(3200, FlowClass.Ab3200)]
    [InlineData(12000, FlowClass.Ab3200)]
    public void A_flow_falls_into_its_class_at_the_richtwerte(int? litresPerMinute, FlowClass expected) =>
        Assert.Equal(expected, WaterPointMarking.Classify(litresPerMinute));

    [Fact]
    public void The_classes_run_from_weakest_to_strongest_which_is_the_drawing_order() =>
        Assert.Equal(
            [FlowClass.Unbekannt, FlowClass.Unter400, FlowClass.Bis799, FlowClass.Bis1599, FlowClass.Bis3199, FlowClass.Ab3200],
            Enum.GetValues<FlowClass>().Order());

    [Theory]
    [InlineData(null, false, "?")]
    [InlineData(null, true, "?")]
    [InlineData(300, false, "300")]
    [InlineData(1600, false, "1600")]
    [InlineData(9999, false, "9999")]
    [InlineData(10000, false, "10k")]
    [InlineData(12999, false, "12k")]
    [InlineData(1600, true, "(1600)")]
    [InlineData(12000, true, "(12k)")]
    public void The_flow_is_shown_short_with_brackets_for_an_estimate(int? litresPerMinute, bool estimated, string expected) =>
        Assert.Equal(expected, WaterPointMarking.FlowText(Hydrant with { FlowLitresPerMinute = litresPerMinute, IsEstimated = estimated }));

    [Theory]
    [InlineData(100, "DN 100")]
    [InlineData(null, "DN ?")]
    public void The_diameter_line_says_dn_or_a_question_mark(int? dn, string expected) =>
        Assert.Equal(expected, WaterPointMarking.DiameterText(Hydrant with { NominalDiameter = dn }));

    [Theory]
    [InlineData(WaterPointKind.Hydrant, WaterPointSymbol.Unterflurhydrant)]
    [InlineData(WaterPointKind.Saugstelle, WaterPointSymbol.Saugstelle)]
    [InlineData(WaterPointKind.Loeschwasserbehaelter, WaterPointSymbol.Zisterne)]
    [InlineData(WaterPointKind.Loeschteich, WaterPointSymbol.OffenesGewaesser)]
    public void Unless_told_otherwise_a_point_carries_the_glyph_of_its_kind(WaterPointKind kind, WaterPointSymbol symbol) =>
        Assert.Equal(symbol, (Hydrant with { Kind = kind }).Symbol);

    [Fact]
    public void A_glyph_set_explicitly_is_kept() =>
        Assert.Equal(WaterPointSymbol.Wandhydrant, (Hydrant with { Symbol = WaterPointSymbol.Wandhydrant }).Symbol);
}
