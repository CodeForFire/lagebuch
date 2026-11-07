using System.Globalization;
using System.Net;
using System.Text.Json;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Weather;

/// <summary>
/// Loads the current DWD observation and the DWD warnings for a point from Bright Sky
/// (<c>api.brightsky.dev</c>), a free JSON front end to the Deutscher Wetterdienst's open data.
/// It is asked only when someone presses the button on the Lagekarte, and the point it is sent is
/// rounded to about a kilometre: finer than the station grid, coarser than an address.
/// </summary>
/// <remarks>
/// The answer is untrusted input from the network: it is read against a size cap, every field is
/// optional, values outside what a weather station can measure are dropped, and texts are capped.
/// Every way the request can fail surfaces as a <see cref="WeatherException"/> with a German
/// message, never as a raw transport exception.
/// </remarks>
public sealed class BrightSkyClient : IWeatherSource
{
    /// <summary>One observation runs to about 2 KB and a day of warnings to a few dozen.</summary>
    public const long MaxResponseBytes = 256 * 1024;

    /// <summary>The most warnings kept; DWD rarely issues more than a handful for one place.</summary>
    public const int MaxAlerts = 20;

    /// <summary>The longest station name, warning event or headline kept.</summary>
    public const int MaxTextLength = 200;

    private const string UserAgent = "LageBuch (+https://github.com/CodeForFire/lagebuch)";

    private const string TooLarge = "Die Antwort des Wetterdienstes ist zu groß.";

    private const string Unreadable = "Die Antwort des Wetterdienstes ist unlesbar.";

    /// <summary>The public instance run by the Bright Sky project.</summary>
    public static readonly Uri DefaultEndpoint = new("https://api.brightsky.dev/");

    /// <summary>How long one request may take; the shared HttpClient's own default is far longer.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeProvider _time;

    public BrightSkyClient(HttpClient http, Uri? endpoint = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _endpoint = endpoint ?? DefaultEndpoint;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<WeatherReading> LoadAsync(GeoPoint at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(at);
        var query = "?lat=" + Coordinate(at.Latitude) + "&lon=" + Coordinate(at.Longitude);

        var current = GetAsync("current_weather" + query, cancellationToken);
        var alerts = AlertsAsync("alerts" + query, cancellationToken);

        var reading = ParseCurrent(await current);
        var warnings = await alerts;
        return reading with { Alerts = warnings };
    }

    // The observation is what the button was pressed for; a warnings call that fails on its own
    // costs only the banner, so its failure is folded into "no warnings" rather than thrown.
    private async Task<IReadOnlyList<WeatherAlert>> AlertsAsync(string relative, CancellationToken cancellationToken)
    {
        try
        {
            return ParseAlerts(await GetAsync(relative, cancellationToken));
        }
        catch (WeatherException)
        {
            return [];
        }
    }

    private static string Coordinate(double degrees) =>
        Math.Round(degrees, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private async Task<byte[]> GetAsync(string relative, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_endpoint, relative));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var timer = new CancellationTokenSource(RequestTimeout, _time);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherException(Refusal(response.StatusCode));
            }

            // Stryker disable once Statement : the capped read below refuses the same body, only later.
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw new WeatherException(TooLarge);
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await ReadCappedAsync(body, timeout.Token);
        }
        catch (HttpRequestException e)
        {
            throw new WeatherException("Wetterdienst nicht erreichbar. Internetverbindung prüfen.", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout, or HttpClient's, reported as a cancellation nobody asked for.
            throw new WeatherException("Der Wetterdienst hat nicht rechtzeitig geantwortet. Später erneut versuchen.", e);
        }
    }

    private static string Refusal(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => "Der Wetterdienst ist ausgelastet (HTTP 429). In ein paar Minuten erneut versuchen.",
        HttpStatusCode.NotFound => "Für diesen Ort liefert der DWD keine Messwerte (HTTP 404).",
        _ => $"Der Wetterdienst hat mit HTTP {(int)status} geantwortet.",
    };

    // A missing or lying Content-Length must not let the body grow without bound.
    private static async Task<byte[]> ReadCappedAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new WeatherException(TooLarge);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static JsonDocument Parse(byte[] json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new WeatherException(Unreadable, e);
        }
    }

    private static WeatherReading ParseCurrent(byte[] json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("weather", out var weather)
            || weather.ValueKind != JsonValueKind.Object
            || Time(weather, "timestamp") is not { } measuredAt)
        {
            throw new WeatherException(Unreadable);
        }

        var (station, distance) = Station(root, weather);
        return new WeatherReading(
            measuredAt,
            station,
            distance,
            First(weather, 0, 360, "wind_direction_10", "wind_direction_30", "wind_direction_60"),
            First(weather, 0, 500, "wind_speed_10", "wind_speed_30", "wind_speed_60"),
            First(weather, 0, 500, "wind_gust_speed_10", "wind_gust_speed_30", "wind_gust_speed_60"),
            First(weather, -90, 60, "temperature"),
            Text(weather, "condition"),
            First(weather, 0, 500, "precipitation_60"),
            []);
    }

    // The observation names its station by id; the sources list says which station that is.
    private static (string? Name, double? Distance) Station(JsonElement root, JsonElement weather)
    {
        if (!weather.TryGetProperty("source_id", out var id)
            || id.ValueKind != JsonValueKind.Number
            || !root.TryGetProperty("sources", out var sources)
            || sources.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty("id", out var sourceId)
                && sourceId.ValueKind == JsonValueKind.Number
                && sourceId.GetRawText() == id.GetRawText())
            {
                return (Text(source, "station_name"), First(source, 0, 1_000_000, "distance"));
            }
        }

        return (null, null);
    }

    private static List<WeatherAlert> ParseAlerts(byte[] json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("alerts", out var alerts)
            || alerts.ValueKind != JsonValueKind.Array)
        {
            throw new WeatherException(Unreadable);
        }

        var result = new List<WeatherAlert>();
        foreach (var alert in alerts.EnumerateArray())
        {
            if (result.Count == MaxAlerts)
            {
                break;
            }

            if (alert.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var headline = Text(alert, "headline_de");
            if ((Text(alert, "event_de") ?? headline) is not { } name)
            {
                continue;
            }

            result.Add(new WeatherAlert(
                name,
                headline ?? name,
                Severity(Text(alert, "severity")),
                Time(alert, "onset"),
                Time(alert, "expires")));
        }

        return result;
    }

    private static WeatherAlertSeverity Severity(string? value) => value switch
    {
        "moderate" => WeatherAlertSeverity.Moderate,
        "severe" => WeatherAlertSeverity.Severe,
        "extreme" => WeatherAlertSeverity.Extreme,
        _ => WeatherAlertSeverity.Minor,
    };

    /// <summary>The first of <paramref name="names"/> that holds a number within the range.</summary>
    private static double? First(JsonElement element, double min, double max, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number)
                && double.IsFinite(number))
            {
                return number >= min && number <= max ? number : null;
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Stryker disable once Equality : a text of exactly the cap is cut to itself.
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Text(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time
            : null;
}
