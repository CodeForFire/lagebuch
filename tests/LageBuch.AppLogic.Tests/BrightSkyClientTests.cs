using System.Net;
using System.Text;
using LageBuch.AppLogic.Services.Weather;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public sealed class BrightSkyClientTests : IDisposable
{
    // Trimmed from api.brightsky.dev/current_weather?lat=48.18&lon=11.25 on 2026-11-05.
    private const string CurrentWeather = """
        {"weather":{"source_id":9426,"timestamp":"2026-11-05T21:10:00+00:00","cloud_cover":0,"condition":"dry",
        "precipitation_60":0.0,"pressure_msl":1025.5,"relative_humidity":99,
        "wind_direction_10":60,"wind_speed_10":10.1,"wind_gust_speed_10":15.8,"temperature":0.3,"icon":"clear-night"},
        "sources":[{"id":9426,"dwd_station_id":"06346","observation_type":"synop","lat":48.207,"lon":11.2035,
        "station_name":"Maisach-Galgen","distance":4576},
        {"id":18377,"station_name":"Muenchen-Stadt","distance":21825}]}
        """;

    private const string NoAlerts = """
        {"alerts":[],"location":{"warn_cell_id":809179121,"name":"Große Kreisstadt Fürstenfeldbruck"}}
        """;

    private const string OneAlert = """
        {"alerts":[{"id":1,"severity":"severe","event_de":"STURMBÖEN","headline_de":"Amtliche WARNUNG vor STURMBÖEN",
        "onset":"2026-11-05T15:00:00+00:00","expires":"2026-11-05T22:00:00+00:00","event_en":"gusts"}]}
        """;

    private static readonly GeoPoint Fuerstenfeldbruck = new(48.17853, 11.25549);

    private HttpClient? _http;

    /// <summary>Answers per path; records every request.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A body that never says how long it is and runs on far past any cap.</summary>
    private sealed class UnsizedStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)' ', offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Answer(string current, string alerts) =>
        r => Json(r.RequestUri!.AbsolutePath.EndsWith("/alerts", StringComparison.Ordinal) ? alerts : current);

    private (BrightSkyClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        _http = new HttpClient(handler);
        return (new BrightSkyClient(_http, new Uri("https://brightsky.test/")), handler);
    }

    public void Dispose() => _http?.Dispose();

    [Fact]
    public async Task Reads_the_station_observation_the_service_answers_with()
    {
        var (client, _) = Create(Answer(CurrentWeather, NoAlerts));

        var reading = await client.LoadAsync(Fuerstenfeldbruck);

        Assert.Equal(new DateTimeOffset(2026, 11, 5, 21, 10, 0, TimeSpan.Zero), reading.MeasuredAt);
        Assert.Equal("Maisach-Galgen", reading.StationName);
        Assert.Equal(4576, reading.StationDistanceMetres!.Value, precision: 3);
        Assert.Equal(60, reading.WindFromDegrees!.Value, precision: 3);
        Assert.Equal(10.1, reading.WindSpeedKmh!.Value, precision: 3);
        Assert.Equal(15.8, reading.WindGustKmh!.Value, precision: 3);
        Assert.Equal(0.3, reading.TemperatureCelsius!.Value, precision: 3);
        Assert.Equal(0, reading.PrecipitationMm!.Value, precision: 3);
        Assert.Equal("dry", reading.Condition);
        Assert.Empty(reading.Alerts);
    }

    [Fact]
    public async Task Reads_a_dwd_warning_in_its_german_wording()
    {
        var (client, _) = Create(Answer(CurrentWeather, OneAlert));

        var alert = Assert.Single((await client.LoadAsync(Fuerstenfeldbruck)).Alerts);

        Assert.Equal("STURMBÖEN", alert.Event);
        Assert.Equal("Amtliche WARNUNG vor STURMBÖEN", alert.Headline);
        Assert.Equal(WeatherAlertSeverity.Severe, alert.Severity);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero), alert.Expires);
    }

    [Fact]
    public async Task Sends_the_point_rounded_to_a_kilometre_in_invariant_culture_with_a_user_agent()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var (client, handler) = Create(Answer(CurrentWeather, NoAlerts));

            await client.LoadAsync(Fuerstenfeldbruck);

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r =>
            {
                Assert.Equal("?lat=48.18&lon=11.26", r.RequestUri!.Query);
                Assert.Contains("LageBuch", r.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            });
            Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/current_weather");
            Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/alerts");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Weather_without_the_warnings_still_loads_when_only_the_alerts_call_fails()
    {
        var (client, _) = Create(r => r.RequestUri!.AbsolutePath == "/alerts"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(CurrentWeather));

        var reading = await client.LoadAsync(Fuerstenfeldbruck);

        Assert.Equal("Maisach-Galgen", reading.StationName);
        Assert.Empty(reading.Alerts);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "ausgelastet")]
    [InlineData(HttpStatusCode.NotFound, "keine Messwerte")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task A_refusal_becomes_a_german_message(HttpStatusCode status, string expected)
    {
        var (client, _) = Create(_ => new HttpResponseMessage(status));

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Contains(expected, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_service_says_to_check_the_connection_without_the_raw_error()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("No such host is known. (brightsky.test:443)"));

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Equal("Wetterdienst nicht erreichbar. Internetverbindung prüfen.", e.Message);
    }

    [Fact]
    public async Task A_timeout_is_reported_as_one()
    {
        var (client, _) = Create(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Contains("nicht rechtzeitig", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancel_the_caller_asked_for_stays_a_cancel()
    {
        var (client, _) = Create(Answer(CurrentWeather, NoAlerts));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LoadAsync(Fuerstenfeldbruck, cts.Token));
    }

    [Fact]
    public async Task An_oversized_body_is_refused_by_its_content_length()
    {
        var big = new string(' ', (int)BrightSkyClient.MaxResponseBytes + 1);
        var (client, _) = Create(_ => Json(big));

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Contains("zu groß", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endless_body_without_a_length_is_cut_off_at_the_cap()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnsizedStream()) });

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Contains("zu groß", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"weather":null}""")]
    [InlineData("""{"weather":{"temperature":1}}""")]
    [InlineData("""{"weather":{"timestamp":"gestern"}}""")]
    public async Task An_answer_without_a_usable_observation_is_refused(string body)
    {
        var (client, _) = Create(Answer(body, NoAlerts));

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Contains("unlesbar", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Values_that_make_no_sense_are_dropped_not_shown()
    {
        const string hostile = """
            {"weather":{"timestamp":"2026-11-05T21:10:00+00:00","wind_direction_10":9999,"wind_speed_10":-5,
            "wind_gust_speed_10":1e308,"temperature":-400,"precipitation_60":-1,"condition":"dry","source_id":1},
            "sources":[{"id":1,"distance":-3}]}
            """;
        var (client, _) = Create(Answer(hostile, NoAlerts));

        var reading = await client.LoadAsync(Fuerstenfeldbruck);

        Assert.Null(reading.WindFromDegrees);
        Assert.Null(reading.WindSpeedKmh);
        Assert.Null(reading.WindGustKmh);
        Assert.Null(reading.TemperatureCelsius);
        Assert.Null(reading.PrecipitationMm);
        Assert.Null(reading.StationDistanceMetres);
        Assert.Null(reading.StationName);
    }

    [Fact]
    public async Task Strings_and_the_number_of_warnings_are_capped()
    {
        var longText = new string('A', 5000);
        var alerts = string.Join(',', Enumerable.Range(0, 100).Select(i => $$"""{"severity":"extreme","event_de":"{{longText[..1000]}}","headline_de":"x"}"""));
        var current = CurrentWeather.Replace("Maisach-Galgen", longText, StringComparison.Ordinal);
        var (client, _) = Create(Answer(current, $$"""{"alerts":[{{alerts}}]}"""));

        var reading = await client.LoadAsync(Fuerstenfeldbruck);

        Assert.Equal(BrightSkyClient.MaxAlerts, reading.Alerts.Count);
        Assert.All(reading.Alerts, a => Assert.True(a.Event.Length <= BrightSkyClient.MaxTextLength));
        Assert.True(reading.StationName!.Length <= BrightSkyClient.MaxTextLength);
    }

    [Fact]
    public async Task A_warning_without_any_text_is_skipped()
    {
        var (client, _) = Create(Answer(CurrentWeather, """{"alerts":[{"severity":"severe"},{"severity":"weird","event_de":"FROST"}]}"""));

        var alert = Assert.Single((await client.LoadAsync(Fuerstenfeldbruck)).Alerts);

        Assert.Equal("FROST", alert.Event);
        Assert.Equal("FROST", alert.Headline);
        Assert.Equal(WeatherAlertSeverity.Minor, alert.Severity);
    }
}
