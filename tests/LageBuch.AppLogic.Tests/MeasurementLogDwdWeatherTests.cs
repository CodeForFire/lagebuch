using LageBuch.AppLogic.Services.Weather;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Measurements;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

// #930: DWD-WETTER LADEN fills the Messprotokoll's Wetter form from the station nearest the
// Einsatzort, the source the Lagekarte uses; the Lagebuchführer checks it and saves it.
public sealed class MeasurementLogDwdWeatherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 22, 30, 0, TimeSpan.FromHours(1));

    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);

    private static readonly WeatherReading Maisach = new(
        new DateTimeOffset(2026, 11, 5, 21, 10, 0, TimeSpan.Zero),
        "Maisach-Galgen",
        4576,
        60,
        10.1,
        15.8,
        0.3,
        "rain",
        0.4,
        [new WeatherAlert("STURMBÖEN", "Amtliche WARNUNG vor STURMBÖEN", WeatherAlertSeverity.Severe, null, new DateTimeOffset(2026, 11, 5, 23, 0, 0, TimeSpan.Zero))])
    {
        CloudCoverPercent = 75,
    };

    /// <summary>Answers each load when the test says so; records where it was asked for.</summary>
    private sealed class FakeWeather : IWeatherSource
    {
        private readonly Queue<TaskCompletionSource<WeatherReading>> _pending = new();

        public List<GeoPoint> Asked { get; } = [];

        public Task<WeatherReading> LoadAsync(GeoPoint at, CancellationToken cancellationToken = default)
        {
            Asked.Add(at);
            var answer = new TaskCompletionSource<WeatherReading>();
            _pending.Enqueue(answer);
            return answer.Task;
        }

        public void Answer(WeatherReading reading) => _pending.Dequeue().SetResult(reading);

        public void Fail(string message) => _pending.Dequeue().SetException(new WeatherException(message));
    }

    private static LocalIncidentSession NewSession(FakeStore? store = null) =>
        TestSession.StartNew(
            store ?? new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static MeasurementLogViewModel NewVm(LocalIncidentSession session, IWeatherSource? weather) =>
        new(session, new FixedClock(T0), () => { }, weather: weather);

    [Fact]
    public void Without_a_weather_source_there_is_no_dwd_button()
    {
        using var vm = NewVm(NewSession(), null);

        Assert.False(vm.IsDwdAvailable);
        Assert.False(vm.LoadDwdWeatherCommand.CanExecute(null));

        // Run anyway, as a binding never would: it does nothing, not even complain.
        vm.LoadDwdWeatherCommand.Execute(null);
        Assert.Null(vm.WeatherError);
    }

    [Fact]
    public void A_station_reading_fills_the_form_and_saves_nothing_until_wetter_erfassen()
    {
        var session = NewSession();
        session.SetIncidentLocation(Marktplatz);
        var weather = new FakeWeather();
        using var vm = NewVm(session, weather);

        vm.LoadDwdWeatherCommand.Execute(null);
        Assert.True(vm.IsLoadingDwd);
        Assert.False(vm.LoadDwdWeatherCommand.CanExecute(null));
        weather.Answer(Maisach);

        Assert.Equal(Marktplatz, Assert.Single(weather.Asked));
        Assert.False(vm.IsLoadingDwd);
        Assert.Null(vm.WeatherError);
        Assert.Equal("22:10", vm.WeatherTime);
        Assert.Equal(WindDirection.NE, vm.WeatherWind);
        Assert.Equal("10,1", vm.WeatherSpeed);
        Assert.Equal("0,3", vm.WeatherTemperature);
        Assert.Equal(CloudCover.MoreThanHalf, vm.WeatherClouds);
        Assert.Equal(Precipitation.Rain, vm.WeatherPrecipitation);
        Assert.Equal("DWD Maisach-Galgen (4,6 km)", vm.WeatherSource);
        Assert.Equal(
            "DWD-Station Maisach-Galgen (4,6 km) · Stand 22:10 · prüfen, dann WETTER ERFASSEN.\nAmtliche WARNUNG vor STURMBÖEN · bis 06.11. 00:00",
            vm.DwdNote);
        Assert.Empty(session.Incident.WeatherObservations);

        vm.AddWeatherCommand.Execute(null);

        var saved = Assert.Single(session.Incident.WeatherObservations);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 22, 10, 0, TimeSpan.FromHours(1)), saved.ObservedAt);
        Assert.Equal(WindDirection.NE, saved.Wind);
        Assert.Equal(10.1m, saved.WindSpeedKmh);
        Assert.Equal(0.3m, saved.TemperatureCelsius);
        Assert.Equal(CloudCover.MoreThanHalf, saved.Clouds);
        Assert.Equal(Precipitation.Rain, saved.Precipitation);
        Assert.Equal("DWD Maisach-Galgen (4,6 km)", saved.Source);
        Assert.Null(vm.DwdNote);
    }

    [Fact]
    public void A_value_the_station_did_not_report_keeps_the_forms_choice()
    {
        var session = NewSession();
        session.SetIncidentLocation(Marktplatz);
        var weather = new FakeWeather();
        using var vm = NewVm(session, weather);
        vm.WeatherWind = WindDirection.SW;
        vm.WeatherClouds = CloudCover.Overcast;
        vm.WeatherSpeed = "20";

        vm.LoadDwdWeatherCommand.Execute(null);
        weather.Answer(new WeatherReading(T0, null, null, null, null, null, null, null, null, []));

        Assert.Equal(WindDirection.SW, vm.WeatherWind);
        Assert.Equal(CloudCover.Overcast, vm.WeatherClouds);
        Assert.Null(vm.WeatherSpeed);
        Assert.Equal("DWD", vm.WeatherSource);
        Assert.Equal("DWD · Stand 22:30 · prüfen, dann WETTER ERFASSEN.", vm.DwdNote);
    }

    [Fact]
    public void Without_an_einsatzort_it_says_where_to_set_one_and_asks_nobody()
    {
        var weather = new FakeWeather();
        using var vm = NewVm(NewSession(), weather);

        vm.LoadDwdWeatherCommand.Execute(null);

        Assert.Empty(weather.Asked);
        Assert.Equal("Für DWD-Wetter erst den Einsatzort setzen (EINSATZDATEN).", vm.WeatherError);
        Assert.False(vm.IsLoadingDwd);
    }

    [Fact]
    public void A_failed_load_shows_the_weather_services_message_and_leaves_the_form()
    {
        var session = NewSession();
        session.SetIncidentLocation(Marktplatz);
        var weather = new FakeWeather();
        using var vm = NewVm(session, weather);
        vm.WeatherSpeed = "12";

        vm.LoadDwdWeatherCommand.Execute(null);
        weather.Fail("Wetterdienst nicht erreichbar. Internetverbindung prüfen.");

        Assert.Equal("Wetterdienst nicht erreichbar. Internetverbindung prüfen.", vm.WeatherError);
        Assert.Equal("12", vm.WeatherSpeed);
        Assert.Null(vm.DwdNote);
        Assert.False(vm.IsLoadingDwd);
        Assert.True(vm.LoadDwdWeatherCommand.CanExecute(null));
    }

    [Fact]
    public void A_new_load_clears_the_last_error_and_note()
    {
        var session = NewSession();
        session.SetIncidentLocation(Marktplatz);
        var weather = new FakeWeather();
        using var vm = NewVm(session, weather);
        vm.LoadDwdWeatherCommand.Execute(null);
        weather.Answer(Maisach);
        vm.WeatherError = "alt";

        vm.LoadDwdWeatherCommand.Execute(null);

        Assert.Null(vm.WeatherError);
        Assert.Null(vm.DwdNote);

        // xUnit waits for the command it started; the load has to end for the test to.
        weather.Answer(Maisach);
        Assert.NotNull(vm.DwdNote);
    }

    [Fact]
    public void A_closed_einsatz_offers_no_dwd_load()
    {
        var store = new FakeStore();
        var seed = NewSession(store);
        seed.Close();
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");
        var weather = new FakeWeather();
        using var vm = NewVm(ro, weather);

        Assert.False(vm.IsDwdAvailable);
        Assert.False(vm.LoadDwdWeatherCommand.CanExecute(null));
        vm.LoadDwdWeatherCommand.Execute(null);
        Assert.Empty(weather.Asked);
        Assert.Null(vm.WeatherError);
    }
}
