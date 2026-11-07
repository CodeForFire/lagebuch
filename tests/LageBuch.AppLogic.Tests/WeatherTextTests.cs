using LageBuch.AppLogic.Services.Weather;

namespace LageBuch.AppLogic.Tests;

public class WeatherTextTests
{
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);

    private static WeatherReading Maisach(IReadOnlyList<WeatherAlert>? alerts = null) => new(
        new DateTimeOffset(2026, 11, 5, 21, 10, 0, TimeSpan.Zero),
        "Maisach-Galgen",
        4576,
        60,
        10.1,
        15.8,
        0.3,
        "dry",
        0,
        alerts ?? []);

    private static WeatherReading Empty() => new(
        new DateTimeOffset(2026, 11, 5, 21, 10, 0, TimeSpan.Zero), null, null, null, null, null, null, null, null, []);

    private static readonly WeatherAlert Sturm = new(
        "STURMBÖEN",
        "Amtliche WARNUNG vor STURMBÖEN",
        WeatherAlertSeverity.Severe,
        new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(0, "Nord")]
    [InlineData(22.4, "Nord")]
    [InlineData(22.5, "Nordost")]
    [InlineData(60, "Nordost")]
    [InlineData(90, "Ost")]
    [InlineData(135, "Südost")]
    [InlineData(180, "Süd")]
    [InlineData(225, "Südwest")]
    [InlineData(270, "West")]
    [InlineData(315, "Nordwest")]
    [InlineData(337.4, "Nordwest")]
    [InlineData(337.5, "Nord")]
    [InlineData(360, "Nord")]
    public void Compass_point_names_the_nearest_of_eight_in_german(double degrees, string expected) =>
        Assert.Equal(expected, WeatherText.CompassPoint(degrees));

    [Theory]
    [InlineData("dry", "trocken")]
    [InlineData("fog", "Nebel")]
    [InlineData("rain", "Regen")]
    [InlineData("sleet", "Schneeregen")]
    [InlineData("snow", "Schnee")]
    [InlineData("hail", "Hagel")]
    [InlineData("thunderstorm", "Gewitter")]
    [InlineData("tornado", null)]
    [InlineData(null, null)]
    public void Condition_is_named_in_german_and_an_unknown_one_is_left_out(string? key, string? expected) =>
        Assert.Equal(expected, WeatherText.Condition(key));

    [Fact]
    public void Wind_says_where_it_comes_from_how_strong_and_the_gusts() =>
        Assert.Equal("Wind aus Nordost (60°) · 10 km/h · Böen 16 km/h", WeatherText.Wind(Maisach()));

    [Fact]
    public void Wind_without_a_measurement_says_so() =>
        Assert.Equal("Wind: keine Messung", WeatherText.Wind(Empty()));

    [Fact]
    public void Details_give_temperature_and_condition() =>
        Assert.Equal("0,3 °C · trocken", WeatherText.Details(Maisach()));

    [Fact]
    public void Details_add_precipitation_only_when_some_fell() =>
        Assert.Equal("12,0 °C · Regen · 1,2 mm in 1 h", WeatherText.Details(Maisach() with { TemperatureCelsius = 12, Condition = "rain", PrecipitationMm = 1.2 }));

    [Fact]
    public void Details_are_empty_without_any_measurement() =>
        Assert.Equal(string.Empty, WeatherText.Details(Empty()));

    [Fact]
    public void Source_names_the_station_its_distance_and_the_local_time_of_measurement() =>
        Assert.Equal("DWD-Station Maisach-Galgen (4,6 km) · Stand 22:10", WeatherText.Source(Maisach(), Cet));

    [Fact]
    public void Source_without_a_station_still_gives_the_time() =>
        Assert.Equal("DWD · Stand 22:10", WeatherText.Source(Empty(), Cet));

    [Fact]
    public void Alert_gives_the_headline_and_until_when_it_applies() =>
        Assert.Equal("Amtliche WARNUNG vor STURMBÖEN · bis 05.11. 23:00", WeatherText.Alert(Sturm, Cet));

    [Fact]
    public void Alert_without_an_end_is_the_headline_alone() =>
        Assert.Equal("Amtliche WARNUNG vor STURMBÖEN", WeatherText.Alert(Sturm with { Expires = null }, Cet));

    [Fact]
    public void Etb_entry_records_the_reading_in_one_line() =>
        Assert.Equal(
            "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus Nordost (60°) 10 km/h, Böen 16 km/h, 0,3 °C, trocken.",
            WeatherText.EtbEntry(Maisach(), Cet));

    [Fact]
    public void Etb_entry_adds_every_warning_in_force() =>
        Assert.Equal(
            "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus Nordost (60°) 10 km/h, Böen 16 km/h, 0,3 °C, trocken. Warnung: STURMBÖEN bis 05.11. 23:00.",
            WeatherText.EtbEntry(Maisach([Sturm]), Cet));

    [Fact]
    public void Etb_entry_names_precipitation_as_such() =>
        Assert.Equal(
            "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus Nordost (60°) 10 km/h, Böen 16 km/h, 0,3 °C, Regen, 1,2 mm Niederschlag in 1 h.",
            WeatherText.EtbEntry(Maisach() with { Condition = "rain", PrecipitationMm = 1.2 }, Cet));

    [Fact]
    public void Etb_entry_stays_within_the_ETB_limit_and_counts_the_warnings_left_out()
    {
        var longest = Enumerable.Range(0, 20)
            .Select(i => Sturm with { Event = $"{i:D2}" + new string('X', 198), Expires = null })
            .ToList();

        var entry = WeatherText.EtbEntry(Maisach(longest), Cet);

        Assert.True(entry.Length <= LageBuch.Domain.Etb.EtbEntry.MaxTextLength, $"{entry.Length} characters");
        var shown = entry.Split(" Warnung: ").Length - 1;
        Assert.InRange(shown, 1, 19);
        Assert.EndsWith($" {20 - shown} weitere Warnungen.", entry, StringComparison.Ordinal);
        Assert.Contains("00" + new string('X', 198) + ".", entry, StringComparison.Ordinal);

        // One past what fits: the last is named as one.
        var onePast = WeatherText.EtbEntry(Maisach(longest.Take(shown + 1).ToList()), Cet);
        Assert.EndsWith(" Eine weitere Warnung.", onePast, StringComparison.Ordinal);
        Assert.True(onePast.Length <= LageBuch.Domain.Etb.EtbEntry.MaxTextLength);

        // Exactly what fits: nothing is said to be missing.
        var fitting = WeatherText.EtbEntry(Maisach(longest.Take(shown).ToList()), Cet);
        Assert.EndsWith(new string('X', 198) + ".", fitting, StringComparison.Ordinal);
    }

    [Fact]
    public void Etb_entry_without_measurements_says_so() =>
        Assert.Equal("Wetter (DWD, 22:10 Uhr): keine Messwerte.", WeatherText.EtbEntry(Empty(), Cet));
}
