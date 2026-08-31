using System.Net;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class RegionPackCatalogServiceTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static readonly string ValidManifest = """
        [
          { "name": "Landkreis Fürstenfeldbruck", "slug": "ffb", "downloadUrl": "https://example.org/ffb.zip",
            "sizeBytes": 42, "boundingBox": { "minLat": 48.08, "minLon": 10.99, "maxLat": 48.29, "maxLon": 11.41 },
            "builtAt": "2026-09-01", "attribution": "OSM" }
        ]
        """;

    [Fact]
    public async Task GetAvailableRegionsAsync_returns_the_parsed_manifest_on_success()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidManifest),
        });
        var service = new RegionPackCatalogService(new HttpClient(handler), "https://example.org/regions.json");

        var regions = await service.GetAvailableRegionsAsync();

        Assert.Equal("ffb", Assert.Single(regions).Slug);
    }

    [Fact]
    public async Task GetAvailableRegionsAsync_fetches_the_configured_manifest_url()
    {
        HttpRequestMessage? seenRequest = null;
        var handler = new FakeHandler(r =>
        {
            seenRequest = r;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });
        var service = new RegionPackCatalogService(new HttpClient(handler), "https://example.org/regions.json");

        await service.GetAvailableRegionsAsync();

        Assert.Equal("https://example.org/regions.json", seenRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetAvailableRegionsAsync_returns_empty_list_never_throws_on_http_failure()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new RegionPackCatalogService(new HttpClient(handler), "https://example.org/regions.json");

        var regions = await service.GetAvailableRegionsAsync();

        Assert.Empty(regions);
    }

    [Fact]
    public async Task GetAvailableRegionsAsync_returns_empty_list_never_throws_when_the_handler_throws()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route to host"));
        var service = new RegionPackCatalogService(new HttpClient(handler), "https://example.org/regions.json");

        var regions = await service.GetAvailableRegionsAsync();

        Assert.Empty(regions);
    }
}
