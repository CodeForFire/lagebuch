using System.IO.Compression;
using System.Net;
using System.Text;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public sealed class RegionPackLibraryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;

    public RegionPackLibraryTests() => _http = new HttpClient(_handler);

    private string Root => Path.Join(_dir, "regions");

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task An_installed_pack_holds_its_tiles_and_is_listed_with_what_the_catalogue_said()
    {
        var zip = Zip(("region.mbtiles", "tiles-v1"), ("region.dem", "dem"));
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var installed = await library.InstallAsync(Pack(zip.Length));

        Assert.Equal(Path.Join(Root, "ffb"), installed.FolderPath);
        Assert.Equal("tiles-v1", await File.ReadAllTextAsync(installed.TilesPath));
        var listed = Assert.Single(library.GetInstalled());
        Assert.Equal(Pack(zip.Length), listed.Info);
    }

    [Fact]
    public async Task A_newer_edition_replaces_the_old_one_and_leaves_none_of_its_files()
    {
        var v1 = Zip(("region.mbtiles", "tiles-v1"), ("region.dem", "dem"));
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        await library.InstallAsync(Pack(v1.Length));

        var v2 = Zip(("region.mbtiles", "tiles-v2"));
        _handler.Respond(_ => Ok(v2));
        var installed = await library.InstallAsync(Pack(v2.Length) with { BuiltAt = "2026-10-01" });

        Assert.Equal("tiles-v2", await File.ReadAllTextAsync(installed.TilesPath));
        Assert.False(File.Exists(Path.Join(installed.FolderPath, "region.dem")));
        Assert.Equal("2026-10-01", Assert.Single(library.GetInstalled()).Info.BuiltAt);
        Assert.Equal(["ffb"], Directory.GetFileSystemEntries(Root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("http://example.org/ffb.zip")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public async Task A_download_address_that_is_not_https_is_refused_before_any_request(string address)
    {
        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(10) with { DownloadUrl = address }));

        Assert.Contains("https", e.Message, StringComparison.Ordinal);
        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task A_pack_announced_larger_than_the_cap_is_refused_before_any_request()
    {
        await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(RegionPackLibrary.MaxPackBytes + 1)));

        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task A_body_larger_than_the_catalogue_announced_is_cut_off_and_the_older_copy_stays()
    {
        var v1 = Zip(("region.mbtiles", "tiles-v1"));
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        await library.InstallAsync(Pack(v1.Length));

        // No Content-Length, so only counting the bytes as they arrive can catch it.
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[4096])) });
        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(100)));

        Assert.Contains("größer als angekündigt", e.Message, StringComparison.Ordinal);
        Assert.Equal("tiles-v1", await File.ReadAllTextAsync(Assert.Single(library.GetInstalled()).TilesPath));
        Assert.Equal(["ffb"], Directory.GetFileSystemEntries(Root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_zip_entry_named_to_escape_the_pack_folder_is_never_written()
    {
        var zip = Zip(("../../evil.txt", "x"), ("sub/../../../evil2.txt", "x"), ("region.mbtiles", "tiles"), ("readme.txt", "x"));
        _handler.Respond(_ => Ok(zip));

        var installed = await Library().InstallAsync(Pack(zip.Length));

        Assert.Empty(Directory.GetFiles(_dir, "evil*", SearchOption.AllDirectories));
        Assert.Equal(
            ["pack.json", "region.mbtiles"],
            Directory.GetFiles(installed.FolderPath).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_zip_without_tiles_is_refused_and_installs_nothing()
    {
        var zip = Zip(("region.dem", "dem"));
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip.Length)));

        Assert.Contains("region.mbtiles", e.Message, StringComparison.Ordinal);
        Assert.Empty(library.GetInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task A_download_that_is_no_zip_is_reported_as_damaged()
    {
        var garbage = Encoding.UTF8.GetBytes("<html>not a zip</html>");
        _handler.Respond(_ => Ok(garbage));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(garbage.Length)));

        Assert.Equal("Das Kartenpaket ist beschädigt.", e.Message);
    }

    [Fact]
    public async Task A_refused_download_names_the_status_code()
    {
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(10)));

        Assert.Equal("Das Kartenpaket ist nicht abrufbar (HTTP 404).", e.Message);
    }

    [Fact]
    public async Task A_network_failure_is_reported_in_german_without_the_raw_exception_text()
    {
        _handler.Respond(_ => throw new HttpRequestException("Name or service not known (raw.githubusercontent.com:443)"));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetCatalogAsync());

        Assert.Equal("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen.", e.Message);
    }

    [Fact]
    public async Task A_cancelled_download_propagates_and_leaves_no_temporary_files()
    {
        using var cts = new CancellationTokenSource();
        _handler.Respond(_ =>
        {
            cts.Cancel();
            return Ok(new byte[10]);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Library().InstallAsync(Pack(10), cancellationToken: cts.Token));

        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task The_catalogue_is_fetched_and_read()
    {
        _handler.Respond(r =>
        {
            Assert.Equal(RegionPackLibrary.DefaultCatalogUrl, r.RequestUri);
            return Ok(Encoding.UTF8.GetBytes("""[{"name":"Landkreis Starnberg","slug":"sta","downloadUrl":"https://example.org/sta.zip","sizeBytes":5,"boundingBox":{"minLat":47.8,"minLon":11.1,"maxLat":48.1,"maxLon":11.4},"builtAt":"2026-09-01","attribution":"OSM"}]"""));
        });

        var pack = Assert.Single(await Library().GetCatalogAsync());

        Assert.Equal("sta", pack.Slug);
    }

    [Fact]
    public async Task A_catalogue_that_is_no_json_array_is_reported_as_damaged()
    {
        _handler.Respond(_ => Ok(Encoding.UTF8.GetBytes("<html>")));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetCatalogAsync());

        Assert.Equal("Die Liste der Kartenpakete ist beschädigt.", e.Message);
    }

    [Fact]
    public async Task A_catalogue_larger_than_its_cap_is_refused()
    {
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(new byte[RegionPackLibrary.MaxCatalogBytes + 1])),
        });

        await Assert.ThrowsAsync<RegionPackException>(() => Library().GetCatalogAsync());
    }

    [Fact]
    public void Without_a_pack_folder_nothing_is_installed() => Assert.Empty(Library().GetInstalled());

    [Fact]
    public async Task A_folder_whose_pack_json_names_another_pack_is_not_listed()
    {
        var zip = Zip(("region.mbtiles", "tiles"));
        _handler.Respond(_ => Ok(zip));
        var library = Library();
        var installed = await library.InstallAsync(Pack(zip.Length));
        Directory.Move(installed.FolderPath, Path.Join(Root, "sta"));
        Directory.CreateDirectory(Path.Join(Root, "empty"));

        Assert.Empty(library.GetInstalled());
    }

    [Fact]
    public async Task Removing_a_pack_deletes_its_folder()
    {
        var zip = Zip(("region.mbtiles", "tiles"));
        _handler.Respond(_ => Ok(zip));
        var library = Library();
        var installed = await library.InstallAsync(Pack(zip.Length));

        library.Remove(installed);

        Assert.False(Directory.Exists(installed.FolderPath));
        Assert.Empty(library.GetInstalled());
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../keep")]
    public void Removing_a_pack_whose_name_would_leave_the_pack_folder_deletes_nothing(string slug)
    {
        var keep = Directory.CreateDirectory(Path.Join(_dir, "keep")).FullName;
        Directory.CreateDirectory(Root);

        Assert.Throws<RegionPackException>(() => Library().Remove(new InstalledRegionPack(Pack(1) with { Slug = slug }, keep)));

        Assert.True(Directory.Exists(keep));
        Assert.True(Directory.Exists(Root));
    }

    private RegionPackLibrary Library() => new(_http, Root);

    private static RegionPackInfo Pack(long size) =>
        new("Landkreis Fürstenfeldbruck", "ffb", "https://example.org/ffb.zip", size, 48.08, 10.99, 48.29, 11.41, "2026-08-31", "© OpenStreetMap contributors");

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public int Requests { get; private set; }

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(_respond(request));
        }
    }
}
