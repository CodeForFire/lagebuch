using LageBuch.AppLogic.Services.Weather;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class LagekarteWeatherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 22, 15, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);
    private static readonly GeoPoint Amperbruecke = new(48.17633, 11.25881);

    private static readonly WeatherReading Maisach = new(
        new DateTimeOffset(2026, 11, 5, 21, 10, 0, TimeSpan.Zero), "Maisach-Galgen", 4576, 60, 10.1, 15.8, 0.3, "dry", 0, []);

    private static readonly WeatherAlert Sturm = new(
        "STURMBÖEN", "Amtliche WARNUNG vor STURMBÖEN", WeatherAlertSeverity.Severe, null, new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));

    /// <summary>Answers each load when the test says so, and remembers where it was asked for.</summary>
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

    [Fact]
    public void Weather_cannot_be_loaded_before_the_Einsatzort_is_set()
    {
        var (_, vm, _) = Open(new FakeWeather());

        Assert.True(vm.Weather.IsAvailable);
        Assert.False(vm.Weather.LoadCommand.CanExecute(null));
        Assert.Equal("Wetter: erst den Einsatzort setzen", vm.Weather.LoadTip);
    }

    [Fact]
    public void Without_a_weather_source_the_weather_is_not_offered_at_all()
    {
        var (session, vm, _) = Open(weather: null);
        session.SetIncidentLocation(Marktplatz);

        Assert.False(vm.Weather.IsAvailable);
        Assert.False(vm.Weather.LoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Loading_asks_for_the_Einsatzort_and_shows_what_the_station_measured()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        Assert.True(vm.Weather.LoadCommand.CanExecute(null));
        Assert.Equal("Wetter am Einsatzort laden (DWD)", vm.Weather.LoadTip);

        var load = vm.Weather.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.Weather.IsLoading);
        Assert.False(vm.Weather.LoadCommand.CanExecute(null));
        weather.Answer(Maisach);
        await load;

        Assert.Equal(Marktplatz, Assert.Single(weather.Asked));
        Assert.False(vm.Weather.IsLoading);
        Assert.True(vm.Weather.HasReading);
        Assert.True(vm.Weather.HasWind);
        Assert.Equal("Wind aus NO (60°) · 10 km/h · Böen 16 km/h", vm.Weather.WindText);
        Assert.Equal("0,3 °C · trocken", vm.Weather.DetailText);
        Assert.Equal("DWD-Station Maisach-Galgen (4,6 km) · Stand 22:10", vm.Weather.SourceText);
        Assert.Empty(vm.Weather.Alerts);
    }

    [Fact]
    public async Task The_wind_arrow_points_downwind_and_turns_with_the_map()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var load = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach);
        await load;

        Assert.Equal(240, vm.Weather.WindArrowAngle, precision: 6);

        var notified = new List<string?>();
        vm.Weather.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        vm.Map.ChangeViewCommand.Execute(new LageBuch.AppLogic.Services.Map.MapViewChange(Marktplatz, vm.Map.Zoom, 150));

        Assert.Equal(30, vm.Weather.WindArrowAngle, precision: 6);
        Assert.Contains(nameof(WeatherOverlay.WindArrowAngle), notified);
    }

    [Fact]
    public async Task Calm_air_without_a_direction_draws_no_arrow()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var load = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach with { WindFromDegrees = null });
        await load;

        Assert.True(vm.Weather.HasReading);
        Assert.False(vm.Weather.HasWind);
    }

    [Fact]
    public async Task A_dwd_warning_is_listed_and_marked_when_it_is_severe()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var load = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach with { Alerts = [Sturm, Sturm with { Event = "FROST", Headline = "Amtliche WARNUNG vor FROST", Severity = WeatherAlertSeverity.Minor, Expires = null }] });
        await load;

        Assert.Collection(
            vm.Weather.Alerts,
            a =>
            {
                Assert.Equal("Amtliche WARNUNG vor STURMBÖEN · bis 05.11. 23:00", a.Text);
                Assert.True(a.IsSevere);
            },
            a =>
            {
                Assert.Equal("Amtliche WARNUNG vor FROST", a.Text);
                Assert.False(a.IsSevere);
            });
    }

    [Fact]
    public async Task A_failed_load_says_why_and_keeps_nothing_old()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var first = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach);
        await first;

        var second = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Fail("Wetterdienst nicht erreichbar. Internetverbindung prüfen.");
        await second;

        Assert.Equal("Wetterdienst nicht erreichbar. Internetverbindung prüfen.", vm.Weather.Error);
        Assert.False(vm.Weather.HasReading);
        Assert.False(vm.Weather.IsLoading);
        Assert.True(vm.Weather.LoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Moving_the_Einsatzort_drops_the_reading_and_a_late_answer_for_the_old_spot()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var first = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach);
        await first;

        session.SetIncidentLocation(Amperbruecke);
        Assert.False(vm.Weather.HasReading);

        var late = vm.Weather.LoadCommand.ExecuteAsync(null);
        session.SetIncidentLocation(Marktplatz);
        Assert.False(vm.Weather.IsLoading);
        weather.Answer(Maisach);
        await late;

        Assert.False(vm.Weather.HasReading);
        Assert.Null(vm.Weather.Error);
    }

    [Fact]
    public async Task Close_takes_the_weather_off_the_map()
    {
        var weather = new FakeWeather();
        var (session, vm, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var load = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach);
        await load;

        vm.Weather.CloseCommand.Execute(null);

        Assert.False(vm.Weather.HasReading);
        Assert.False(vm.Weather.HasWind);
    }

    [Fact]
    public async Task Ins_ETB_writes_the_reading_as_an_Intern_entry_once()
    {
        var weather = new FakeWeather();
        var (session, vm, changes) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        var load = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach with { Alerts = [Sturm] });
        await load;
        var before = session.Incident.Journal.Count;
        var changesBefore = changes();

        Assert.True(vm.Weather.CanLog);
        vm.Weather.LogToEtbCommand.Execute(null);

        var entry = session.Incident.Journal.Last();
        Assert.Equal(before + 1, session.Incident.Journal.Count);
        Assert.Equal(EtbDirection.Internal, entry.Direction);
        Assert.Equal(
            "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus NO (60°) 10 km/h, Böen 16 km/h, 0,3 °C, trocken. Warnung: STURMBÖEN bis 05.11. 23:00.",
            entry.Text);
        Assert.Equal(changesBefore + 1, changes());
        Assert.True(vm.Weather.IsLogged);
        Assert.False(vm.Weather.LogToEtbCommand.CanExecute(null));

        var reload = vm.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach);
        await reload;
        Assert.False(vm.Weather.IsLogged);
        Assert.True(vm.Weather.LogToEtbCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_closed_Einsatz_shows_the_weather_but_takes_no_ETB_entry()
    {
        var weather = new FakeWeather();
        var (session, _, _) = Open(weather);
        session.SetIncidentLocation(Marktplatz);
        session.Close();
        using var closed = new LagekarteViewModel(session, () => { }, weather: weather, clock: new FixedClock(T0));

        var load = closed.Weather.LoadCommand.ExecuteAsync(null);
        weather.Answer(Maisach);
        await load;

        Assert.True(closed.Weather.HasReading);
        Assert.False(closed.Weather.CanLog);
        Assert.False(closed.Weather.LogToEtbCommand.CanExecute(null));
    }

    [Fact]
    public void Nothing_can_be_logged_before_a_reading_is_loaded()
    {
        var (session, vm, _) = Open(new FakeWeather());
        session.SetIncidentLocation(Marktplatz);

        Assert.False(vm.Weather.LogToEtbCommand.CanExecute(null));
    }

    private static (LocalIncidentSession Session, LagekarteViewModel Vm, Func<int> Changes) Open(IWeatherSource? weather)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var changes = 0;
        var vm = new LagekarteViewModel(session, () => changes++, weather: weather, clock: new FixedClock(T0));
        return (session, vm, () => changes);
    }
}
