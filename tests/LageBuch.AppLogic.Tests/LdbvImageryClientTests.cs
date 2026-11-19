using System.Net;
using System.Text;
using System.Web;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.Files;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public sealed class LdbvImageryClientTests : IDisposable
{
    // What the LDBV's WMS answers with status 200 when it refuses a request.
    private const string ServiceException = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ServiceExceptionReport version="1.3.0" xmlns="http://www.opengis.net/ogc">
          <ServiceException code="InvalidCRS">msWMSLoadGetMapParams(): Invalid CRS given</ServiceException>
        </ServiceExceptionReport>
        """;

    private static readonly LuftbildArea Area = AreaAroundFuerstenfeldbruck();

    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    private static LuftbildArea AreaAroundFuerstenfeldbruck()
    {
        var (x, y) = WebMercator.ToMetres(new GeoPoint(48.1785, 11.2555));
        var southWest = WebMercator.FromMetres(x - 600, y - 300);
        var northEast = WebMercator.FromMetres(x + 600, y + 300);
        return LuftbildArea.For(new GeoBox(southWest.Longitude, southWest.Latitude, northEast.Longitude, northEast.Latitude))
            ?? throw new InvalidOperationException("Fürstenfeldbruck lies in Bavaria.");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Never answers; only the client's own timeout ends the wait.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>A body that never says how long it is and runs on past any cap.</summary>
    private sealed class EndlessStream(byte[] start) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++, _position++)
            {
                buffer[offset + i] = _position < start.Length ? start[_position] : (byte)0;
            }

            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Image(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // The right picture for whichever layer was asked for, at the size the area asks for.
    private static HttpResponseMessage Answer(HttpRequestMessage request) =>
        Query(request)["LAYERS"] == "by_dop40c"
            ? Image(TestImages.Jpeg(Area.Width, Area.Height), "image/jpeg")
            : Image(TestImages.Png(Area.Width, Area.Height), "image/png");

    private static System.Collections.Specialized.NameValueCollection Query(HttpRequestMessage request) =>
        HttpUtility.ParseQueryString(Assert.IsType<Uri>(request.RequestUri).Query);

    private (LdbvImageryClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler);
        _clients.Add(http);
        return (new LdbvImageryClient(http), handler);
    }

    [Fact]
    public async Task The_Luftbild_is_asked_for_as_a_WMS_GetMap_of_the_DOP40_in_Web_Mercator()
    {
        var (client, handler) = Create(Answer);

        var bytes = await client.GetMapAsync(LuftbildLayer.Luftbild, Area);

        Assert.Equal(TestImages.Jpeg(Area.Width, Area.Height), bytes);
        var request = Assert.Single(handler.Requests);
        var uri = Assert.IsType<Uri>(request.RequestUri);
        Assert.Equal("https://geoservices.bayern.de/od/wms/dop/v1/dop40", uri.GetLeftPart(UriPartial.Path));
        var query = Query(request);
        Assert.Equal("WMS", query["SERVICE"]);
        Assert.Equal("1.3.0", query["VERSION"]);
        Assert.Equal("GetMap", query["REQUEST"]);
        Assert.Equal("by_dop40c", query["LAYERS"]);
        Assert.Equal(string.Empty, query["STYLES"]);
        Assert.Equal("EPSG:3857", query["CRS"]);
        Assert.Equal("image/jpeg", query["FORMAT"]);
        Assert.Null(query["TRANSPARENT"]);
        Assert.Equal("2000", query["WIDTH"]);
        Assert.Equal("1000", query["HEIGHT"]);
        Assert.Contains("LageBuch", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_box_is_sent_as_x_and_y_in_metres_with_no_axis_swap_and_in_invariant_culture()
    {
        var (client, handler) = Create(Answer);

        await client.GetMapAsync(LuftbildLayer.Luftbild, Area);

        var box = Assert.IsType<string>(Query(Assert.Single(handler.Requests))["BBOX"]).Split(',');
        Assert.Equal(4, box.Length);
        var edges = box.Select(e => double.Parse(e, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.True(Math.Abs(edges[0] - Area.MinX) < 0.01);
        Assert.True(Math.Abs(edges[1] - Area.MinY) < 0.01);
        Assert.True(Math.Abs(edges[2] - Area.MaxX) < 0.01);
        Assert.True(Math.Abs(edges[3] - Area.MaxY) < 0.01);

        // x is east, about 1.25 million metres from Greenwich; y north, about 6.13 million from the equator.
        Assert.InRange(edges[0], 1_250_000, 1_255_000);
        Assert.InRange(edges[1], 6_130_000, 6_140_000);
    }

    [Fact]
    public async Task The_Flurstuecksgrenzen_are_asked_for_as_a_transparent_PNG_of_the_Parzellarkarte()
    {
        var (client, handler) = Create(Answer);

        var bytes = await client.GetMapAsync(LuftbildLayer.Flurstuecke, Area);

        Assert.Equal(TestImages.Png(Area.Width, Area.Height), bytes);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://geoservices.bayern.de/od/wms/alkis/v1/parzellarkarte", Assert.IsType<Uri>(request.RequestUri).GetLeftPart(UriPartial.Path));
        var query = Query(request);
        Assert.Equal("by_alkis_parzellarkarte_umr_gelb", query["LAYERS"]);
        Assert.Equal("image/png", query["FORMAT"]);
        Assert.Equal("TRUE", query["TRANSPARENT"]);
        Assert.Equal("EPSG:3857", query["CRS"]);
    }

    [Fact]
    public async Task A_ServiceException_sent_with_status_200_becomes_a_German_error()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ServiceException, Encoding.UTF8, "application/vnd.ogc.se_xml"),
        });

        var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));

        Assert.Equal("Der Kartendienst der Vermessungsverwaltung hat die Anfrage abgelehnt.", e.Message);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    public async Task An_answer_that_is_not_an_image_is_refused(string contentType)
    {
        var (client, _) = Create(_ => Image(Encoding.UTF8.GetBytes("<html>Wartungsarbeiten</html>"), contentType));

        var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));

        Assert.Equal("Der Kartendienst der Vermessungsverwaltung hat kein Bild geliefert.", e.Message);
    }

    [Fact]
    public async Task An_image_that_is_not_what_was_asked_for_is_refused()
    {
        var (wrongFormat, _) = Create(_ => Image(TestImages.Png(Area.Width, Area.Height), "image/jpeg"));
        var (wrongSize, _) = Create(_ => Image(TestImages.Jpeg(Area.Width / 2, Area.Height), "image/jpeg"));
        var (noImage, _) = Create(_ => Image(Encoding.UTF8.GetBytes("GIF89a"), "image/jpeg"));

        foreach (var client in new[] { wrongFormat, wrongSize, noImage })
        {
            var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));
            Assert.Equal("Das Bild des Kartendienstes ist unlesbar.", e.Message);
        }
    }

    [Fact]
    public async Task An_oversized_body_is_refused_by_its_content_length()
    {
        var (client, _) = Create(_ =>
        {
            var answer = Image(TestImages.Jpeg(Area.Width, Area.Height), "image/jpeg");
            answer.Content.Headers.ContentLength = IncidentFile.MaxSizeBytes + 1;
            return answer;
        });

        var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));

        Assert.Equal("Das Bild des Kartendienstes ist zu groß.", e.Message);
    }

    [Fact]
    public async Task An_endless_body_without_a_length_is_cut_off_at_the_cap()
    {
        var (client, _) = Create(_ =>
        {
            var content = new StreamContent(new EndlessStream(TestImages.Jpeg(Area.Width, Area.Height)));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));

        Assert.Equal("Das Bild des Kartendienstes ist zu groß.", e.Message);
    }

    [Fact]
    public async Task A_refusal_names_its_status()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));

        Assert.Equal("Der Kartendienst der Vermessungsverwaltung hat mit HTTP 503 geantwortet.", e.Message);
    }

    [Fact]
    public async Task An_unreachable_service_says_to_check_the_connection_without_the_raw_error()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("No such host is known. (geoservices.bayern.de:443)"));

        var e = await Assert.ThrowsAsync<LuftbildException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area));

        Assert.Equal("Kartendienst der Vermessungsverwaltung nicht erreichbar. Internetverbindung prüfen.", e.Message);
    }

    [Fact]
    public async Task A_cancel_the_caller_asked_for_stays_a_cancel()
    {
        var (client, _) = Create(Answer);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMapAsync(LuftbildLayer.Luftbild, Area, cts.Token));
    }

    [Fact]
    public async Task A_service_that_never_answers_runs_into_the_clients_own_timeout()
    {
        var time = new ManualTimeProvider();
        var http = new HttpClient(new HangingHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        _clients.Add(http);
        var client = new LdbvImageryClient(http, time);

        var load = client.GetMapAsync(LuftbildLayer.Luftbild, Area);
        Assert.False(load.IsCompleted);
        time.Tick();

        var e = await Assert.ThrowsAsync<LuftbildException>(() => load);
        Assert.Equal("Der Kartendienst der Vermessungsverwaltung hat nicht rechtzeitig geantwortet. Später erneut versuchen.", e.Message);
    }
}
