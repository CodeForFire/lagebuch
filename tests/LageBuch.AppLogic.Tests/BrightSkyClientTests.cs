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

    // Every client a test makes, disposed together; some tests make more than one.
    private readonly List<HttpClient> _clients = [];

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

    /// <summary>
    /// A body that never says how long it is: <paramref name="content"/>, or without it one that
    /// runs on far past any cap.
    /// </summary>
    private sealed class UnsizedStream(byte[]? content = null) : Stream
    {
        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (content is null)
            {
                Array.Fill(buffer, (byte)' ', offset, count);
                return count;
            }

            var n = Math.Min(count, content.Length - _offset);
            Array.Copy(content, _offset, buffer, offset, n);
            _offset += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Never answers; only the client's own timeout ends the wait.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Started { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>The observation padded with JSON whitespace to exactly <paramref name="length"/> bytes.</summary>
    private static byte[] Padded(long length)
    {
        var bytes = Encoding.UTF8.GetBytes(CurrentWeather);
        var padded = new byte[length];
        Array.Fill(padded, (byte)' ');
        bytes.CopyTo(padded, 0);
        return padded;
    }

    private static string PathOf(HttpRequestMessage request) => Assert.IsType<Uri>(request.RequestUri).AbsolutePath;

    private static bool IsAlerts(HttpRequestMessage request) => PathOf(request) == "/alerts";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Answer(string current, string alerts) =>
        r => Json(IsAlerts(r) ? alerts : current);

    private (BrightSkyClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        return (new BrightSkyClient(Own(new HttpClient(handler)), new Uri("https://brightsky.test/")), handler);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    private HttpClient Own(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }

    [Fact]
    public async Task Reads_the_station_observation_the_service_answers_with()
    {
        var (client, _) = Create(Answer(CurrentWeather, NoAlerts));

        var reading = await client.LoadAsync(Fuerstenfeldbruck);

        Assert.Equal(new DateTimeOffset(2026, 11, 5, 21, 10, 0, TimeSpan.Zero), reading.MeasuredAt);
        Assert.Equal("Maisach-Galgen", reading.StationName);
        Assert.Equal(4576, Assert.NotNull(reading.StationDistanceMetres), precision: 3);
        Assert.Equal(60, Assert.NotNull(reading.WindFromDegrees), precision: 3);
        Assert.Equal(10.1, Assert.NotNull(reading.WindSpeedKmh), precision: 3);
        Assert.Equal(15.8, Assert.NotNull(reading.WindGustKmh), precision: 3);
        Assert.Equal(0.3, Assert.NotNull(reading.TemperatureCelsius), precision: 3);
        Assert.Equal(0, Assert.NotNull(reading.PrecipitationMm), precision: 3);
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
                Assert.Equal("?lat=48.18&lon=11.26", Assert.IsType<Uri>(r.RequestUri).Query);
                Assert.Contains("LageBuch", r.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            });
            Assert.Contains(handler.Requests, r => PathOf(r) == "/current_weather");
            Assert.Contains(handler.Requests, r => PathOf(r) == "/alerts");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Weather_without_the_warnings_still_loads_when_only_the_alerts_call_fails()
    {
        var (client, _) = Create(r => PathOf(r) == "/alerts"
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
    public async Task Without_an_endpoint_it_asks_the_public_bright_sky_instance()
    {
        var handler = new FakeHandler(Answer(CurrentWeather, NoAlerts));
        await new BrightSkyClient(Own(new HttpClient(handler))).LoadAsync(Fuerstenfeldbruck);

        Assert.All(handler.Requests, r => Assert.Equal("api.brightsky.dev", Assert.IsType<Uri>(r.RequestUri).Host));
    }

    [Fact]
    public async Task A_body_of_exactly_the_cap_is_read_whether_or_not_it_says_its_length()
    {
        var (sized, _) = Create(r => IsAlerts(r)
            ? Json(NoAlerts)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Padded(BrightSkyClient.MaxResponseBytes)) });
        Assert.Equal("Maisach-Galgen", (await sized.LoadAsync(Fuerstenfeldbruck)).StationName);

        var (unsized, _) = Create(r => IsAlerts(r)
            ? Json(NoAlerts)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnsizedStream(Padded(BrightSkyClient.MaxResponseBytes))) });
        Assert.Equal("Maisach-Galgen", (await unsized.LoadAsync(Fuerstenfeldbruck)).StationName);
    }

    [Fact]
    public async Task One_byte_past_the_cap_without_a_length_is_refused()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnsizedStream(Padded(BrightSkyClient.MaxResponseBytes + 1))),
        });

        var e = await Assert.ThrowsAsync<WeatherException>(() => client.LoadAsync(Fuerstenfeldbruck));

        Assert.Contains("zu groß", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_service_that_never_answers_runs_into_the_clients_own_timeout()
    {
        var handler = new HangingHandler();
        var time = new ManualTimeProvider();
        var client = new BrightSkyClient(Own(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }), new Uri("https://brightsky.test/"), time);

        var load = client.LoadAsync(Fuerstenfeldbruck);
        Assert.Equal(2, handler.Started);
        Assert.False(load.IsCompleted);
        time.Tick();

        var e = await Assert.ThrowsAsync<WeatherException>(() => load);
        Assert.Contains("nicht rechtzeitig", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_of_the_wrong_type_is_skipped_for_the_next_one_that_fits()
    {
        const string odd = """
            {"weather":{"timestamp":"2026-11-05T21:10:00+00:00","wind_direction_10":360,"wind_speed_10":"10",
            "wind_speed_30":5.5,"condition":42,"source_id":7},
            "sources":[{"id":7,"station_name":"   ","distance":1000000}]}
            """;
        var (client, _) = Create(Answer(odd, NoAlerts));

        var reading = await client.LoadAsync(Fuerstenfeldbruck);

        Assert.Equal(360, Assert.NotNull(reading.WindFromDegrees), precision: 3);
        Assert.Equal(5.5, Assert.NotNull(reading.WindSpeedKmh), precision: 3);
        Assert.Equal(1_000_000, Assert.NotNull(reading.StationDistanceMetres), precision: 3);
        Assert.Null(reading.Condition);
        Assert.Null(reading.StationName);
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
        Assert.True(reading.StationName?.Length <= BrightSkyClient.MaxTextLength);
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
