using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LageBuch.Osm.Tests;

public class OverpassClientTests
{
    /// <summary>Answers every request with whatever the callback builds, and keeps the request.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    /// <summary>
    /// A body of <paramref name="length"/> bytes that cannot say how long it is, so no
    /// Content-Length goes ahead of it: what only the cap on the read itself can stop.
    /// </summary>
    private sealed class UnsizedStream(long length = long.MaxValue) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _remaining);
            Array.Fill(buffer, (byte)'x', offset, n);
            _remaining -= n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task The_query_is_posted_as_form_data_with_a_user_agent()
    {
        var handler = new FakeHandler(_ => Ok("""{"elements":[]}"""));
        using var http = new HttpClient(handler);

        var json = await new OverpassClient(http).QueryAsync("[out:json];out;");

        Assert.Equal("""{"elements":[]}""", json);
        Assert.Equal(HttpMethod.Post, handler.Request?.Method);
        Assert.Equal(OverpassClient.DefaultEndpoint, handler.Request?.RequestUri);
        Assert.Equal("data=%5Bout%3Ajson%5D%3Bout%3B", handler.Body);
        Assert.Contains("LageBuch", handler.Request?.Headers.UserAgent.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_endpoint_is_asked_when_one_is_given()
    {
        var handler = new FakeHandler(_ => Ok("{}"));
        using var http = new HttpClient(handler);
        var endpoint = new Uri("https://overpass.example.org/api/interpreter");

        await new OverpassClient(http, endpoint).QueryAsync("q");

        Assert.Equal(endpoint, handler.Request?.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "abgelehnt")]
    [InlineData(HttpStatusCode.NotAcceptable, "HTTP 406")]
    [InlineData(HttpStatusCode.TooManyRequests, "ausgelastet")]
    [InlineData(HttpStatusCode.GatewayTimeout, "abgebrochen")]
    public async Task A_refusal_surfaces_as_a_german_message(HttpStatusCode status, string expected)
    {
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(status)));

        var e = await Assert.ThrowsAsync<OverpassException>(() => new OverpassClient(http).QueryAsync("q"));

        Assert.Contains(expected, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_announced_larger_than_the_cap_is_not_read()
    {
        using var http = new HttpClient(new FakeHandler(_ =>
        {
            var response = Ok(new string('x', 10));
            response.Content.Headers.ContentLength = 2000;
            return response;
        }));

        await Assert.ThrowsAsync<OverpassException>(() => new OverpassClient(http, maxResponseBytes: 1000).QueryAsync("q"));
    }

    [Fact]
    public async Task An_answer_exactly_at_the_cap_is_read()
    {
        using var http = new HttpClient(new FakeHandler(_ => Ok(new string('x', 1000))));

        var json = await new OverpassClient(http, maxResponseBytes: 1000).QueryAsync("q");

        Assert.Equal(1000, json.Length);
    }

    [Fact]
    public async Task An_answer_one_byte_over_the_cap_is_refused_even_without_a_length()
    {
        using var http = new HttpClient(new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnsizedStream(1001)) }));

        await Assert.ThrowsAsync<OverpassException>(() => new OverpassClient(http, maxResponseBytes: 1000).QueryAsync("q"));
    }

    [Fact]
    public async Task An_answer_without_a_length_is_cut_off_at_the_cap()
    {
        using var http = new HttpClient(new FakeHandler(_ =>
        {
            var content = new StreamContent(new UnsizedStream());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));

        var e = await Assert.ThrowsAsync<OverpassException>(
            () => new OverpassClient(http, maxResponseBytes: 1024 * 1024).QueryAsync("q"));

        Assert.Contains("größer als 1 MB", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_server_surfaces_as_an_overpass_exception()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("Name or service not known")));

        var e = await Assert.ThrowsAsync<OverpassException>(() => new OverpassClient(http).QueryAsync("q"));

        Assert.Contains("nicht erreichbar", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_surfaces_as_an_overpass_exception()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new TaskCanceledException("timeout")));

        var e = await Assert.ThrowsAsync<OverpassException>(() => new OverpassClient(http).QueryAsync("q"));

        Assert.Contains("nicht rechtzeitig", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_stays_a_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var http = new HttpClient(new FakeHandler(_ => Ok("{}")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OverpassClient(http).QueryAsync("q", cts.Token));
    }
}
