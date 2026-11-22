using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class WaterPointMarkingTests
{
    private static readonly WaterPoint Hydrant = new(new GeoPoint(48.1785, 11.2555), WaterPointKind.Hydrant, "UH 100");

    [Theory]
    [InlineData(100, "DN 100")]
    [InlineData(null, "DN ?")]
    public void A_hydrant_s_first_line_is_its_dn_or_a_question_mark(int? dn, string expected) =>
        Assert.Equal(expected, WaterPointMarking.PrimaryText(Hydrant with { NominalDiameter = dn }));

    [Theory]
    [InlineData(100, "Z 3", "100 m³")]
    [InlineData(null, "Z 3", "Z 3")]
    [InlineData(null, "", "")]
    public void A_tank_s_first_line_is_its_volume_or_else_its_label(int? volume, string label, string expected) =>
        Assert.Equal(expected, WaterPointMarking.PrimaryText(Hydrant with { Kind = WaterPointKind.Loeschwasserbehaelter, Label = label, VolumeCubicMetres = volume }));

    [Theory]
    [InlineData(WaterPointKind.Saugstelle)]
    [InlineData(WaterPointKind.Loeschteich)]
    public void Open_water_s_first_line_is_its_label(WaterPointKind kind) =>
        Assert.Equal("Amper", WaterPointMarking.PrimaryText(Hydrant with { Kind = kind, Label = "Amper", NominalDiameter = 100 }));

    [Fact]
    public void The_second_line_joins_lage_outlets_and_private_access() =>
        Assert.Equal(
            "Fahrbahn · A, 2×B · privat",
            WaterPointMarking.SecondaryText(Hydrant with { Lage = "Fahrbahn", Outlets = "A, 2×B", IsPrivate = true }));

    [Fact]
    public void The_second_line_leaves_out_what_is_unknown_and_is_empty_without_anything()
    {
        Assert.Equal("Gehweg", WaterPointMarking.SecondaryText(Hydrant with { Lage = "Gehweg" }));
        Assert.Equal(string.Empty, WaterPointMarking.SecondaryText(Hydrant));
    }

    [Theory]
    [InlineData(WaterPointKind.Hydrant, false)]
    [InlineData(WaterPointKind.Saugstelle, true)]
    [InlineData(WaterPointKind.Loeschwasserbehaelter, true)]
    [InlineData(WaterPointKind.Loeschteich, true)]
    public void Only_a_hydrant_hangs_on_the_mains(WaterPointKind kind, bool independent) =>
        Assert.Equal(independent, (Hydrant with { Kind = kind }).IsIndependent);

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
