using System.Globalization;
using LageBuch.Domain.Files;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Fetches a Luftbild and the Flurstücksgrenzen from the Bayerische Vermessungsverwaltung's open
/// WMS services on <c>geoservices.bayern.de</c>: the data BayernAtlas shows, free and without a
/// key under CC BY 4.0. Asked only when someone presses LUFTBILD LADEN, for the section the
/// Lagekarte shows; the service sees that section and the device's IP address, nothing else.
/// </summary>
/// <remarks>
/// The answer is untrusted input from the network: it is read against
/// <see cref="IncidentFile.MaxSizeBytes"/>, must say it is an image, and must be the format and
/// size that was asked for. Every way a request can fail surfaces as a
/// <see cref="LuftbildException"/> with a German message, except a cancel the caller asked for.
/// </remarks>
public sealed class LdbvImageryClient
{
    /// <summary>The licence line CC BY 4.0 asks for wherever the pictures are shown.</summary>
    public const string Attribution = "Luftbild, Flurstücke: © Bayerische Vermessungsverwaltung (CC BY 4.0)";

    private const string UserAgent = "LageBuch (+https://github.com/CodeForFire/lagebuch)";

    private const string Service = "Kartendienst der Vermessungsverwaltung";

    private const string TooLarge = "Das Bild des Kartendienstes ist zu groß.";

    private const string Unreadable = "Das Bild des Kartendienstes ist unlesbar.";

    /// <summary>The DOP40 WMS.</summary>
    public static readonly Uri DopEndpoint = new("https://geoservices.bayern.de/od/wms/dop/v1/dop40");

    /// <summary>The ALKIS-Parzellarkarte WMS.</summary>
    public static readonly Uri ParzellarkarteEndpoint = new("https://geoservices.bayern.de/od/wms/alkis/v1/parzellarkarte");

    /// <summary>How long one picture may take; a 4096 px JPEG is a few megabytes.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public LdbvImageryClient(HttpClient http, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The picture of <paramref name="layer"/> over <paramref name="area"/>: a JPEG for the Luftbild, a PNG for the Flurstücke.</summary>
    public async Task<byte[]> GetMapAsync(LuftbildLayer layer, LuftbildArea area, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        using var request = new HttpRequestMessage(HttpMethod.Get, GetMapUri(layer, area));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var timer = new CancellationTokenSource(RequestTimeout, _time);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);

        byte[] bytes;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new LuftbildException($"Der {Service} hat mit HTTP {(int)response.StatusCode} geantwortet.");
            }

            // A WMS refuses with status 200 and an XML ServiceExceptionReport in place of the picture.
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new LuftbildException($"Der {Service} hat die Anfrage abgelehnt.");
            }

            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new LuftbildException($"Der {Service} hat kein Bild geliefert.");
            }

            // Stryker disable once Statement : the capped read below refuses the same body, only later.
            if (response.Content.Headers.ContentLength > IncidentFile.MaxSizeBytes)
            {
                throw new LuftbildException(TooLarge);
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            bytes = await ReadCappedAsync(body, timeout.Token);
        }
        catch (HttpRequestException e)
        {
            throw new LuftbildException($"{Service} nicht erreichbar. Internetverbindung prüfen.", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout, or HttpClient's, reported as a cancellation nobody asked for.
            throw new LuftbildException($"Der {Service} hat nicht rechtzeitig geantwortet. Später erneut versuchen.", e);
        }

        var expected = layer == LuftbildLayer.Luftbild ? RasterFormat.Jpeg : RasterFormat.Png;
        if (RasterImageInfo.Read(bytes) != new RasterImageInfo(expected, area.Width, area.Height))
        {
            throw new LuftbildException(Unreadable);
        }

        return bytes;
    }

    /// <summary>The WMS 1.3.0 GetMap request for <paramref name="layer"/> over <paramref name="area"/>.</summary>
    /// <remarks>
    /// EPSG:3857 is easting before northing, so the box is x,y with no axis swap; WMS 1.3.0 swaps
    /// only for a CRS whose axis order says so, such as EPSG:4326.
    /// </remarks>
    public static Uri GetMapUri(LuftbildLayer layer, LuftbildArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        var (endpoint, layers, format) = layer == LuftbildLayer.Luftbild
            ? (DopEndpoint, "by_dop40c", "image/jpeg")
            : (ParzellarkarteEndpoint, "by_alkis_parzellarkarte_umr_gelb", "image/png");
        var box = string.Join(',', new[] { area.MinX, area.MinY, area.MaxX, area.MaxY }.Select(Metres));
        var query = "?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap"
            + "&LAYERS=" + layers
            + "&STYLES="
            + "&CRS=" + Uri.EscapeDataString("EPSG:3857")
            + "&BBOX=" + Uri.EscapeDataString(box)
            + "&WIDTH=" + area.Width.ToString(CultureInfo.InvariantCulture)
            + "&HEIGHT=" + area.Height.ToString(CultureInfo.InvariantCulture)
            + "&FORMAT=" + Uri.EscapeDataString(format)
            + (layer == LuftbildLayer.Flurstuecke ? "&TRANSPARENT=TRUE" : string.Empty);
        return new Uri(endpoint.AbsoluteUri + query);
    }

    // To the centimetre: finer than any pixel, and the box stays exactly the area's.
    private static string Metres(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // A missing or lying Content-Length must not let the body grow without bound.
    private static async Task<byte[]> ReadCappedAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > IncidentFile.MaxSizeBytes)
            {
                throw new LuftbildException(TooLarge);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
