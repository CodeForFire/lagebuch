using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class StammdatenCatalogueTests
{
    private static readonly string[] UnitStatus =
        ["Alarmiert", "Auf Anfahrt", "Bereitstellungsraum", "Im Einsatz"];

    [Theory]
    [InlineData("Im Einsatz", "Im Einsatz")]
    [InlineData("im einsatz", "Im Einsatz")]
    [InlineData("  IM EINSATZ  ", "Im Einsatz")]
    public void Normalize_adopts_the_catalogues_own_spelling(string typed, string expected) =>
        Assert.Equal(expected, StammdatenCatalogue.Normalize(typed, UnitStatus));

    [Fact]
    public void Normalize_keeps_an_unknown_value_but_trims_it()
    {
        Assert.Equal("Einsatzbereit am Standort", StammdatenCatalogue.Normalize("  Einsatzbereit am Standort ", UnitStatus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_leaves_a_blank_value_alone(string? typed) =>
        Assert.True(string.IsNullOrEmpty(StammdatenCatalogue.Normalize(typed, UnitStatus)));

    [Fact]
    public void IsUnknown_is_true_only_for_a_value_the_catalogue_does_not_hold()
    {
        Assert.True(StammdatenCatalogue.IsUnknown("Einsatzbereit am Standort", UnitStatus));
        Assert.False(StammdatenCatalogue.IsUnknown("Im Einsatz", UnitStatus));
        Assert.False(StammdatenCatalogue.IsUnknown("  im einsatz ", UnitStatus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsUnknown_is_false_for_a_blank_value(string? typed) =>
        Assert.False(StammdatenCatalogue.IsUnknown(typed, UnitStatus));

    [Fact]
    public void IsUnknown_is_false_when_no_stammdaten_are_configured()
    {
        // A fresh install ships no master data at all; flagging every entry there would be noise.
        Assert.False(StammdatenCatalogue.IsUnknown("Im Einsatz", Array.Empty<string>()));
    }

    [Fact]
    public void Including_leaves_the_catalogue_alone_when_it_already_holds_the_value()
    {
        Assert.Same(UnitStatus, StammdatenCatalogue.Including(UnitStatus, "Im Einsatz"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Including_leaves_the_catalogue_alone_for_a_blank_value(string? current) =>
        Assert.Same(UnitStatus, StammdatenCatalogue.Including(UnitStatus, current));

    [Fact]
    public void Including_carries_an_unknown_value_so_a_closed_picker_can_show_it()
    {
        var options = StammdatenCatalogue.Including(UnitStatus, "Einsatzbereit am Standort");

        Assert.Equal(UnitStatus.Length + 1, options.Count);
        Assert.Contains("Einsatzbereit am Standort", options, StringComparer.Ordinal);
    }

    [Fact]
    public void Including_carries_a_value_that_differs_only_in_case()
    {
        // A picker matches its selection against the list by equality, so "im einsatz" would render
        // blank just as surely as an entirely unknown status if it were folded into "Im Einsatz".
        var options = StammdatenCatalogue.Including(UnitStatus, "im einsatz");

        Assert.Contains("im einsatz", options, StringComparer.Ordinal);
    }

    // --- Find: the Trupp-Typ lookup the Atemschutz form uses since #398 ---
    private static readonly TruppType[] TruppTypes =
        [new("Angriffstrupp"), new("CSA-Trupp", 3, 20)];

    [Theory]
    [InlineData("CSA-Trupp")]
    [InlineData("csa-trupp")]
    [InlineData("  CSA-Trupp  ")]
    public void Find_matches_a_trupp_type_trimmed_and_ignoring_case(string designation)
    {
        // The same leniency Normalize applies to every other catalogue value -- a designation that
        // differs only in spacing must not quietly lose the rules its row carries.
        Assert.Equal(new TruppType("CSA-Trupp", 3, 20), StammdatenCatalogue.Find(designation, TruppTypes));
    }

    [Theory]
    [InlineData("Chemietrupp")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Find_returns_null_rather_than_guessing_for_an_unlisted_designation(string? designation) =>
        Assert.Null(StammdatenCatalogue.Find(designation, TruppTypes));

    [Fact]
    public void Find_on_an_empty_catalogue_is_simply_a_miss() =>
        Assert.Null(StammdatenCatalogue.Find("CSA-Trupp", Array.Empty<TruppType>()));
}
