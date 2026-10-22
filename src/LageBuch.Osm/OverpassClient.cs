using System.Net;

namespace LageBuch.Osm;

/// <summary>
/// Sends an <see cref="OverpassQuery"/> to an Overpass API endpoint and returns the JSON it
/// answers with. The response is untrusted input from the network, so it is read against a size
/// cap; every way the request can fail surfaces as an <see cref="OverpassException"/> with a German
/// message, never as a raw transport exception.
/// </summary>
public sealed class OverpassClient
{
    /// <summary>
    /// A Gemeinde's tags run to a few megabytes (Fürstenfeldbruck: about 2 MB); a city's to a few
    /// dozen. Anything larger is not a Gemeinde.
    /// </summary>
    public const long MaxResponseBytes = 64L * 1024 * 1024;

    // overpass-api.de refuses a request without one (HTTP 406), and its usage policy asks that
    // a client say who it is.
    private const string UserAgent = "LageBuch-OsmImport (+https://github.com/CodeForFire/lagebuch)";

    /// <summary>The public instance run by the Overpass API developers.</summary>
    public static readonly Uri DefaultEndpoint = new("https://overpass-api.de/api/interpreter");

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly long _maxResponseBytes;

    public OverpassClient(HttpClient http, Uri? endpoint = null, long maxResponseBytes = MaxResponseBytes)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);
        _http = http;
        _endpoint = endpoint ?? DefaultEndpoint;
        _maxResponseBytes = maxResponseBytes;
    }

    /// <exception cref="OverpassException">The request failed, timed out, or was refused, or the
    /// answer was larger than the cap.</exception>
    public async Task<string> QueryAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        request.Headers.UserAgent.ParseAdd(UserAgent);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new OverpassException(Refusal(response.StatusCode));
            }

            if (response.Content.Headers.ContentLength > _maxResponseBytes)
            {
                throw new OverpassException(TooLarge());
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await ReadCappedAsync(body, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new OverpassException($"Overpass ist nicht erreichbar: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation nobody asked for.
            throw new OverpassException("Overpass hat nicht rechtzeitig geantwortet. Später erneut versuchen.", e);
        }
    }

    private static string Refusal(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => "Overpass ist ausgelastet (HTTP 429). In ein paar Minuten erneut versuchen.",
        HttpStatusCode.GatewayTimeout => "Overpass hat die Abfrage abgebrochen (HTTP 504). Später erneut versuchen.",
        HttpStatusCode.BadRequest => "Overpass hat die Abfrage abgelehnt (HTTP 400).",
        _ => $"Overpass hat mit HTTP {(int)status} geantwortet.",
    };

    private string TooLarge() =>
        $"Die Antwort von Overpass ist größer als {_maxResponseBytes / (1024 * 1024)} MB -- ist das wirklich eine Gemeinde?";

    // A missing or lying Content-Length must not let the body grow without bound.
    private async Task<string> ReadCappedAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > _maxResponseBytes)
            {
                throw new OverpassException(TooLarge());
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
