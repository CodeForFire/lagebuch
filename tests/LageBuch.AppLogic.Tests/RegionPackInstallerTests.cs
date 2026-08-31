using System.IO.Compression;
using System.Net;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class RegionPackInstallerTests : IDisposable
{
    private readonly string _regionsBaseDir = Path.Combine(Path.GetTempPath(), $"regions-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_regionsBaseDir))
            Directory.Delete(_regionsBaseDir, recursive: true);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static byte[] BuildFakePackZip(string mbtilesContent = "fake-mbtiles", string demContent = "fake-dem")
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry("region.mbtiles").Open())
            using (var writer = new StreamWriter(entry))
                writer.Write(mbtilesContent);
            using (var entry = zip.CreateEntry("region.dem").Open())
            using (var writer = new StreamWriter(entry))
                writer.Write(demContent);
        }
        return ms.ToArray();
    }

    private static RegionPackInfo FfbPack(string downloadUrl = "https://example.org/ffb.zip") => new(
        "Landkreis Fürstenfeldbruck", "ffb", downloadUrl, 42, 48.08, 10.99, 48.29, 11.41, "2026-09-01", "OSM");

    [Fact]
    public async Task DownloadAndInstallAsync_extracts_the_zip_into_a_slug_named_folder()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(BuildFakePackZip()),
        });
        var installer = new RegionPackInstaller(new HttpClient(handler), _regionsBaseDir);

        var folder = await installer.DownloadAndInstallAsync(FfbPack(), progress: null);

        Assert.Equal(Path.Combine(_regionsBaseDir, "ffb"), folder);
        Assert.Equal("fake-mbtiles", await File.ReadAllTextAsync(Path.Combine(folder, "region.mbtiles")));
        Assert.Equal("fake-dem", await File.ReadAllTextAsync(Path.Combine(folder, "region.dem")));
    }

    [Fact]
    public async Task DownloadAndInstallAsync_requests_the_packs_download_url()
    {
        HttpRequestMessage? seenRequest = null;
        var handler = new FakeHandler(r =>
        {
            seenRequest = r;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(BuildFakePackZip()) };
        });
        var installer = new RegionPackInstaller(new HttpClient(handler), _regionsBaseDir);

        await installer.DownloadAndInstallAsync(FfbPack("https://example.org/specific-ffb.zip"), progress: null);

        Assert.Equal("https://example.org/specific-ffb.zip", seenRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task DownloadAndInstallAsync_re_installing_the_same_slug_removes_stale_files()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(BuildFakePackZip()),
        });
        var installer = new RegionPackInstaller(new HttpClient(handler), _regionsBaseDir);
        var folder = await installer.DownloadAndInstallAsync(FfbPack(), progress: null);
        var staleFile = Path.Combine(folder, "old-junk.txt");
        await File.WriteAllTextAsync(staleFile, "leftover from a previous version");

        await installer.DownloadAndInstallAsync(FfbPack(), progress: null);

        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(Path.Combine(folder, "region.mbtiles")));
    }

    [Fact]
    public async Task DownloadAndInstallAsync_reports_progress_from_zero_to_one()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(BuildFakePackZip()),
        });
        var installer = new RegionPackInstaller(new HttpClient(handler), _regionsBaseDir);
        var reported = new List<double>();
        var progress = new Progress<double>(reported.Add);

        await installer.DownloadAndInstallAsync(FfbPack(), progress);
        // Progress<T> marshals via SynchronizationContext.Post when one is captured; without one
        // (the xunit test-runner thread has none) it invokes synchronously, so this is deterministic.

        Assert.NotEmpty(reported);
        Assert.Equal(1.0, reported[^1]);
    }
}
