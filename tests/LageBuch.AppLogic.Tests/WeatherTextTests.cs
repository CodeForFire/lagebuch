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
    [InlineData(0, "N")]
    [InlineData(22.4, "N")]
    [InlineData(22.5, "NO")]
    [InlineData(60, "NO")]
    [InlineData(90, "O")]
    [InlineData(135, "SO")]
    [InlineData(180, "S")]
    [InlineData(225, "SW")]
    [InlineData(270, "W")]
    [InlineData(315, "NW")]
    [InlineData(337.4, "NW")]
    [InlineData(337.5, "N")]
    [InlineData(360, "N")]
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
        Assert.Equal("Wind aus NO (60°) · 10 km/h · Böen 16 km/h", WeatherText.Wind(Maisach()));

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
            "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus NO (60°) 10 km/h, Böen 16 km/h, 0,3 °C, trocken.",
            WeatherText.EtbEntry(Maisach(), Cet));

    [Fact]
    public void Etb_entry_adds_every_warning_in_force() =>
        Assert.Equal(
            "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus NO (60°) 10 km/h, Böen 16 km/h, 0,3 °C, trocken. Warnung: STURMBÖEN bis 05.11. 23:00.",
            WeatherText.EtbEntry(Maisach([Sturm]), Cet));

    [Fact]
    public void Etb_entry_without_measurements_says_so() =>
        Assert.Equal("Wetter (DWD, 22:10 Uhr): keine Messwerte.", WeatherText.EtbEntry(Empty(), Cet));
}
