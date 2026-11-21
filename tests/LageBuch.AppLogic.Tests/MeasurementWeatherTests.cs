using LageBuch.AppLogic.Services.Weather;
using LageBuch.Domain.Measurements;

namespace LageBuch.AppLogic.Tests;

// #930: a DWD reading as the Messprotokoll's Wetter form takes it.
public sealed class MeasurementWeatherTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 21, 10, 0, TimeSpan.Zero);

    private static WeatherReading Reading(
        double? from = null,
        double? speed = null,
        string? condition = null,
        double? precipitation = null,
        string? station = null,
        double? distance = null,
        double? clouds = null) =>
        new(At, station, distance, from, speed, null, null, condition, precipitation, []) { CloudCoverPercent = clouds };

    [Theory]
    [InlineData(0, WindDirection.N)]
    [InlineData(22.4, WindDirection.N)]
    [InlineData(22.5, WindDirection.NE)]
    [InlineData(60, WindDirection.NE)]
    [InlineData(90, WindDirection.E)]
    [InlineData(135, WindDirection.SE)]
    [InlineData(180, WindDirection.S)]
    [InlineData(225, WindDirection.SW)]
    [InlineData(270, WindDirection.W)]
    [InlineData(315, WindDirection.NW)]
    [InlineData(337.5, WindDirection.N)]
    [InlineData(360, WindDirection.N)]
    [InlineData(-90, WindDirection.W)]
    public void A_wind_direction_becomes_the_nearest_of_eight_points(double degrees, WindDirection expected) =>
        Assert.Equal(expected, MeasurementWeather.Wind(Reading(from: degrees, speed: 10)));

    [Fact]
    public void Under_one_kmh_is_windstille_whatever_the_vane_says()
    {
        Assert.Equal(WindDirection.Calm, MeasurementWeather.Wind(Reading(from: 270, speed: 0.9)));
        Assert.Equal(WindDirection.W, MeasurementWeather.Wind(Reading(from: 270, speed: 1)));
    }

    [Fact]
    public void A_wind_without_a_direction_is_umlaufend_and_no_wind_at_all_is_no_answer()
    {
        Assert.Equal(WindDirection.Variable, MeasurementWeather.Wind(Reading(speed: 8)));
        Assert.Equal(WindDirection.N, MeasurementWeather.Wind(Reading(from: 0)));
        Assert.Null(MeasurementWeather.Wind(Reading()));
    }

    [Theory]
    [InlineData(0, CloudCover.Clear)]
    [InlineData(6.2, CloudCover.Clear)]
    [InlineData(6.25, CloudCover.UpToHalf)]
    [InlineData(50, CloudCover.UpToHalf)]
    [InlineData(50.1, CloudCover.MoreThanHalf)]
    [InlineData(87.5, CloudCover.MoreThanHalf)]
    [InlineData(93.75, CloudCover.Overcast)]
    [InlineData(100, CloudCover.Overcast)]
    public void Cloud_cover_in_percent_becomes_the_forms_four_steps(double percent, CloudCover expected) =>
        Assert.Equal(expected, MeasurementWeather.Clouds(Reading(clouds: percent)));

    [Fact]
    public void No_cloud_cover_is_no_answer() => Assert.Null(MeasurementWeather.Clouds(Reading()));

    [Theory]
    [InlineData("dry", 2.0, Precipitation.None)]
    [InlineData("rain", null, Precipitation.Rain)]
    [InlineData("hail", null, Precipitation.Rain)]
    [InlineData("thunderstorm", null, Precipitation.Rain)]
    [InlineData("snow", null, Precipitation.Snow)]
    [InlineData("sleet", null, Precipitation.Snow)]
    [InlineData("fog", null, Precipitation.Fog)]
    [InlineData(null, 0.4, Precipitation.Rain)]
    [InlineData("unbekannt", 0.0, Precipitation.None)]
    [InlineData(null, null, Precipitation.None)]
    public void The_condition_decides_the_niederschlag_and_without_one_the_last_hour_does(string? condition, double? mm, Precipitation expected) =>
        Assert.Equal(expected, MeasurementWeather.Precipitation(Reading(condition: condition, precipitation: mm)));

    [Fact]
    public void The_source_names_the_station_and_how_far_off_it_is()
    {
        Assert.Equal("DWD Maisach-Galgen (4,6 km)", MeasurementWeather.Source(Reading(station: "Maisach-Galgen", distance: 4576)));
        Assert.Equal("DWD Maisach-Galgen", MeasurementWeather.Source(Reading(station: "Maisach-Galgen")));
        Assert.Equal("DWD", MeasurementWeather.Source(Reading(distance: 4576)));
    }

    [Fact]
    public void A_long_station_name_drops_the_distance_first_then_is_cut_to_the_source_length()
    {
        // 33 + " (4,6 km)" is 42, over the 40 a source takes; 33 alone fits.
        var fits = MeasurementWeather.Source(Reading(station: new string('a', 29), distance: 4576));
        Assert.Equal("DWD " + new string('a', 29), fits);

        var cut = MeasurementWeather.Source(Reading(station: new string('b', 60), distance: 4576));
        Assert.Equal(WeatherObservation.MaxSourceLength, cut.Length);
        Assert.StartsWith("DWD bbb", cut, StringComparison.Ordinal);

        // Exactly 40 with the distance still takes it.
        var exact = MeasurementWeather.Source(Reading(station: new string('c', 27), distance: 4576));
        Assert.Equal($"DWD {new string('c', 27)} (4,6 km)", exact);
        Assert.Equal(WeatherObservation.MaxSourceLength, exact.Length);
    }

    [Theory]
    [InlineData(10.14, "10,1")]
    [InlineData(10.16, "10,2")]
    [InlineData(12, "12")]
    [InlineData(-3.25, "-3,3")]
    [InlineData(-0.04, "0")]
    public void A_number_is_written_as_the_form_reads_it(double value, string expected) =>
        Assert.Equal(expected, MeasurementWeather.Number(value));

    [Fact]
    public void No_number_is_an_empty_box() => Assert.Null(MeasurementWeather.Number(null));
}
