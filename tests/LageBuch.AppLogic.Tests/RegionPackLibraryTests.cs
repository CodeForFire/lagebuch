using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public sealed class RegionPackLibraryTests : IDisposable
{
    private const string KnownVectorMap = """{"minZoom": 0, "maxZoom": 15, "schema": "protomaps-basemap", "schemaVersion": "4.15.2"}""";

    private const string Area = """{"type": "Feature", "geometry": {"type": "Polygon", "coordinates": [[[11.0, 48.0], [11.4, 48.0], [11.4, 48.3], [11.0, 48.0]]]}}""";

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
    public async Task An_installed_pack_holds_its_six_files_and_is_listed_with_what_the_catalogue_said()
    {
        var zip = PackZip("tiles-v1");
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var installed = await library.InstallAsync(Pack(zip));

        Assert.Equal(Path.Join(Root, "ffb"), installed.FolderPath);
        Assert.Equal("tiles-v1", await File.ReadAllTextAsync(installed.VectorTilesPath));
        Assert.Equal("water", await File.ReadAllTextAsync(installed.WaterPath));
        Assert.Equal("addresses", await File.ReadAllTextAsync(installed.AddressesPath));
        Assert.Equal(
            ["ATTRIBUTION.md", "addresses.sqlite", "boundary.geojson", "elevation.tif", "installed.json", "map.pmtiles", "pack.json", "water.geojson"],
            Directory.GetFiles(installed.FolderPath).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var listed = Assert.Single(library.GetInstalled());
        Assert.Equal(Pack(zip), listed.Info);
    }

    [Fact]
    public async Task A_pack_built_with_raster_tiles_too_installs_without_them()
    {
        var zip = Zip([.. Files(rasterTiles: "raster")]);
        _handler.Respond(_ => Ok(zip));

        var installed = await Library().InstallAsync(Pack(zip));

        Assert.Equal("tiles", await File.ReadAllTextAsync(installed.VectorTilesPath));
        Assert.False(File.Exists(Path.Join(installed.FolderPath, "map.mbtiles")));
    }

    [Theory]
    [InlineData("""{"minZoom": 0, "maxZoom": 15, "schema": "protomaps-basemap", "schemaVersion": "5.0.0"}""")]
    [InlineData("""{"minZoom": 0, "maxZoom": 14, "schema": "openmaptiles", "schemaVersion": "4.0"}""")]
    [InlineData("""{"minZoom": 0, "maxZoom": 15}""")]
    [InlineData("\"protomaps-basemap\"")]
    public async Task A_pack_whose_vector_tiles_this_app_cannot_style_asks_for_a_newer_app_and_installs_nothing(string vectorMap)
    {
        var zip = Zip([.. Files(vectorMap: vectorMap)]);
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket braucht eine neuere Lagebuch-Version. Lagebuch aktualisieren.", e.Message);
        Assert.Empty(library.GetInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task A_pack_folder_without_vector_tiles_is_not_listed_so_it_is_offered_for_download_again()
    {
        var zip = PackZip();
        _handler.Respond(_ => Ok(zip));
        var library = Library();
        var installed = await library.InstallAsync(Pack(zip));

        // What a pack installed before packs carried vector tiles looks like.
        File.Delete(installed.VectorTilesPath);

        Assert.Empty(library.GetInstalled());
    }

    [Fact]
    public async Task Vector_tiles_whose_bytes_differ_from_pack_json_refuse_the_pack()
    {
        var files = Files();
        var zip = Zip([.. files.Select(f => f.Name == "map.pmtiles" ? (f.Name, "tileS") : f)]);
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).", e.Message);
        Assert.Empty(library.GetInstalled());
    }

    [Fact]
    public async Task A_newer_edition_replaces_the_old_one_and_leaves_none_of_its_files()
    {
        var v1 = PackZip("tiles-v1");
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        var old = await library.InstallAsync(Pack(v1));
        await File.WriteAllTextAsync(Path.Join(old.FolderPath, "stray.txt"), "left by hand");

        var v2 = PackZip("tiles-v2");
        _handler.Respond(_ => Ok(v2));
        var installed = await library.InstallAsync(Pack(v2) with { Version = 2 });

        Assert.Equal("tiles-v2", await File.ReadAllTextAsync(installed.VectorTilesPath));
        Assert.False(File.Exists(Path.Join(installed.FolderPath, "stray.txt")));
        Assert.Equal(2, Assert.Single(library.GetInstalled()).Info.Version);
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
        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(RegionPackLibrary.MaxPackBytes + 1)));

        Assert.Equal("Das Kartenpaket ist größer als 1024 MB und wird nicht geladen.", e.Message);
        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task A_pack_announced_as_empty_is_refused_before_any_request()
    {
        await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(0)));

        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task A_pack_announced_at_exactly_the_cap_is_requested()
    {
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(RegionPackLibrary.MaxPackBytes)));

        Assert.Equal(1, _handler.Requests);
    }

    [Fact]
    public async Task A_body_larger_than_the_catalogue_announced_is_cut_off_and_the_older_copy_stays()
    {
        var v1 = PackZip("tiles-v1");
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        await library.InstallAsync(Pack(v1));

        // No Content-Length, so only counting the bytes as they arrive can catch it.
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new DeclaredLengthContent(new byte[4096], declaredLength: null) });
        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(100)));

        Assert.Contains("größer als angekündigt", e.Message, StringComparison.Ordinal);
        Assert.Equal("tiles-v1", await File.ReadAllTextAsync(Assert.Single(library.GetInstalled()).VectorTilesPath));
        Assert.Equal(["ffb"], Directory.GetFileSystemEntries(Root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Leftovers_of_an_interrupted_install_do_not_block_the_next_one()
    {
        var v1 = PackZip("tiles-v1");
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        await library.InstallAsync(Pack(v1));

        // What an install killed halfway leaves behind.
        Directory.CreateDirectory(Path.Join(Root, ".ffb.staging"));
        await File.WriteAllTextAsync(Path.Join(Root, ".ffb.staging", "map.pmtiles"), "half");
        Directory.CreateDirectory(Path.Join(Root, ".ffb.previous"));
        await File.WriteAllTextAsync(Path.Join(Root, ".ffb.previous", "map.pmtiles"), "old");
        var v2 = PackZip("tiles-v2");
        _handler.Respond(_ => Ok(v2));
        var installed = await library.InstallAsync(Pack(v2));

        Assert.Equal("tiles-v2", await File.ReadAllTextAsync(installed.VectorTilesPath));
        Assert.Equal(["ffb"], Directory.GetFileSystemEntries(Root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task An_install_reports_progress_from_nothing_to_done()
    {
        var zip = PackZip();
        _handler.Respond(_ => Ok(zip));
        var progress = new RecordingProgress();

        await Library().InstallAsync(Pack(zip), progress);

        Assert.Equal(0, progress.Reports[0], precision: 6);
        Assert.Equal(1, progress.Reports[^1], precision: 6);
    }

    [Fact]
    public async Task The_download_fills_nine_tenths_of_the_bar_by_the_bytes_the_server_announced()
    {
        // Two read chunks. The catalogue overstates the size; the bar follows the Content-Length.
        var body = new byte[2 * 81920];
        _handler.Respond(_ => Ok(body));
        var progress = new RecordingProgress();

        await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(4L * body.Length), progress));

        Assert.Collection(
            progress.Reports,
            p => Assert.Equal(0, p, precision: 6),
            p => Assert.Equal(0.45, p, precision: 6),
            p => Assert.Equal(0.9, p, precision: 6));
    }

    [Fact]
    public async Task A_pack_folder_that_cannot_be_created_is_reported_in_german()
    {
        await File.WriteAllTextAsync(Root, "a file where the folder belongs");
        _handler.Respond(_ => Ok(PackZip()));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(10)));

        Assert.Equal("Das Kartenpaket konnte nicht gespeichert werden. Freien Speicher prüfen.", e.Message);
    }

    [Fact]
    public async Task A_download_whose_checksum_is_not_the_catalogues_is_never_unpacked_and_the_older_copy_stays()
    {
        var v1 = PackZip("tiles-v1");
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        await library.InstallAsync(Pack(v1));

        // The same size as announced, so only the hash can tell it was swapped on the way.
        var swapped = PackZip("tiles-XX");
        _handler.Respond(_ => Ok(swapped));
        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(swapped) with { Sha256 = Sha(v1) }));

        Assert.Equal("Das Kartenpaket ist beschädigt (Prüfsumme stimmt nicht). Erneut herunterladen.", e.Message);
        Assert.Equal("tiles-v1", await File.ReadAllTextAsync(Assert.Single(library.GetInstalled()).VectorTilesPath));
        Assert.Equal(["ffb"], Directory.GetFileSystemEntries(Root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("../../evil.txt")]
    [InlineData("sub/../../../evil.txt")]
    [InlineData("readme.txt")]
    [InlineData("sub/map.pmtiles")]
    public async Task A_zip_with_an_entry_format_md_does_not_name_is_refused_and_nothing_is_written(string extra)
    {
        var zip = Zip([.. Files(), (extra, "x")]);
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket hat einen unerwarteten Inhalt und wird nicht installiert.", e.Message);
        Assert.Empty(Directory.GetFiles(_dir, "evil*", SearchOption.AllDirectories));
        Assert.Empty(library.GetInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Theory]
    [InlineData("map.pmtiles")]
    [InlineData("pack.json")]
    [InlineData("ATTRIBUTION.md")]
    [InlineData("boundary.geojson")]
    public async Task A_zip_missing_one_of_its_files_is_refused_and_installs_nothing(string missing)
    {
        var zip = Zip([.. Files().Where(f => f.Name != missing)]);
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket hat einen unerwarteten Inhalt und wird nicht installiert.", e.Message);
        Assert.Empty(library.GetInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task A_zip_holding_a_file_twice_is_refused()
    {
        var zip = Zip([.. Files(), ("map.pmtiles", "second")]);
        _handler.Respond(_ => Ok(zip));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket hat einen unerwarteten Inhalt und wird nicht installiert.", e.Message);
    }

    [Theory]
    [InlineData("\"slug\": \"ffb\"", "\"slug\": \"sta\"")]
    [InlineData("\"slug\": \"ffb\", ", "")]
    [InlineData("\"map.pmtiles\": {\"bytes\": 5", "\"map.pmtiles\": {\"bytes\": 6")]
    [InlineData("\"water.geojson\": {\"bytes\": 5, \"sha256\": \"", "\"water.geojson\": {\"bytes\": 5, \"sha256\": \"0")]
    [InlineData("\"addresses.sqlite\"", "\"other.sqlite\"")]
    [InlineData("{\"formatVersion\"", "[{\"formatVersion\"")]
    public async Task A_pack_json_that_does_not_describe_what_was_unpacked_refuses_the_pack(string field, string replacement)
    {
        var zip = Zip([.. Files("tiles", manifest => manifest.Replace(field, replacement, StringComparison.Ordinal))]);
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).", e.Message);
        Assert.Empty(library.GetInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    // The catalogue's checksum matches, so only the zip itself can tell it was cut off on the way.
    [Fact]
    public async Task A_truncated_zip_is_refused_and_the_older_copy_stays()
    {
        var v1 = PackZip("tiles-v1");
        _handler.Respond(_ => Ok(v1));
        var library = Library();
        await library.InstallAsync(Pack(v1));

        var whole = PackZip(new string('x', 200_000));
        var truncated = whole[..(whole.Length / 2)];
        _handler.Respond(_ => Ok(truncated));
        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(truncated)));

        Assert.Equal("Das Kartenpaket ist beschädigt.", e.Message);
        Assert.Equal("tiles-v1", await File.ReadAllTextAsync(Assert.Single(library.GetInstalled()).VectorTilesPath));
        Assert.Equal(["ffb"], Directory.GetFileSystemEntries(Root).Select(Path.GetFileName));
    }

    // A zip bomb with a valid outer checksum: more zeros than the whole 2 GB cap, compressed to a
    // few MB, where pack.json promises five bytes. Cut off at the cap, it would read as "entpackt
    // zu groß"; cut off at its declared size, it reads as not matching pack.json.
    [Fact]
    public async Task An_entry_that_expands_past_its_declared_size_is_cut_off_there()
    {
        var zip = ZipWithBomb(InstalledRegionPack.VectorTilesFileName, RegionPackLibrary.MaxUnpackedBytes + (1024 * 1024));
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).", e.Message);
        Assert.Empty(library.GetInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Theory]
    [InlineData("\"map.pmtiles\": {\"bytes\": 5", "\"map.pmtiles\": {\"bytes\": -5")]
    [InlineData("\"map.pmtiles\": {\"bytes\": 5", "\"map.pmtiles\": {\"bytes\": \"5\"")]
    [InlineData("\"map.pmtiles\": {\"bytes\": 5, \"sha256\": \"", "\"map.pmtiles\": {\"bytes\": 5, \"sha256\": \"X")]
    public async Task A_declared_size_or_checksum_that_is_no_number_or_hash_refuses_the_pack(string field, string replacement)
    {
        var zip = Zip([.. Files("tiles", manifest => manifest.Replace(field, replacement, StringComparison.Ordinal))]);
        _handler.Respond(_ => Ok(zip));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).", e.Message);
    }

    [Fact]
    public async Task A_pack_of_a_newer_format_asks_for_a_newer_app()
    {
        var zip = Zip([.. Files("tiles", manifest => manifest.Replace("\"formatVersion\": 2", "\"formatVersion\": 3", StringComparison.Ordinal))]);
        _handler.Respond(_ => Ok(zip));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket braucht eine neuere Lagebuch-Version. Lagebuch aktualisieren.", e.Message);
    }

    [Fact]
    public async Task A_pack_json_larger_than_its_cap_is_not_read()
    {
        var zip = Zip([.. Files("tiles", manifest => manifest.Replace("{\"formatVersion\"", "{\"pad\": \"" + new string('x', (int)RegionPackLibrary.MaxManifestBytes) + "\", \"formatVersion\"", StringComparison.Ordinal))]);
        _handler.Respond(_ => Ok(zip));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).", e.Message);
    }

    [Fact]
    public async Task A_download_that_is_no_zip_is_reported_as_damaged()
    {
        var garbage = Encoding.UTF8.GetBytes("<html>not a zip</html>");
        _handler.Respond(_ => Ok(garbage));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(garbage)));

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
    public async Task A_server_that_does_not_answer_in_time_is_reported_in_german()
    {
        _handler.Respond(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetCatalogAsync());

        Assert.Equal("Die Liste der Kartenpakete hat nicht rechtzeitig geantwortet. Später erneut versuchen.", e.Message);
    }

    [Fact]
    public async Task A_catalogue_address_given_to_the_library_is_the_one_fetched()
    {
        var address = new Uri("https://example.org/packs.json");
        Uri? requested = null;
        _handler.Respond(r =>
        {
            requested = r.RequestUri;
            return Ok(Encoding.UTF8.GetBytes("""{"formatVersion":2,"packs":[]}"""));
        });

        await new RegionPackLibrary(_http, Root, address).GetCatalogAsync();

        Assert.Equal(address, requested);
    }

    [Fact]
    public async Task A_catalogue_announced_larger_than_its_cap_is_refused_before_its_body_is_read()
    {
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DeclaredLengthContent(Encoding.UTF8.GetBytes("[]"), RegionPackLibrary.MaxCatalogBytes + 1),
        });

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetCatalogAsync());

        Assert.Equal("Die Liste der Kartenpakete ist größer als angekündigt. Download abgebrochen.", e.Message);
    }

    [Fact]
    public async Task The_catalogue_is_fetched_and_read()
    {
        _handler.Respond(r =>
        {
            Assert.Equal(RegionPackLibrary.DefaultCatalogUrl, r.RequestUri);
            return Ok(Encoding.UTF8.GetBytes($$"""{"formatVersion":2,"packs":[{"name":"Landkreis Starnberg","slug":"sta","version":1,"downloadUrl":"https://example.org/sta.zip","sizeBytes":5,"sha256":"{{new string('a', 64)}}","bounds":{"west":11.1,"south":47.8,"east":11.4,"north":48.1},"minZoom":11,"maxZoom":17,"builtAt":"2026-10-27T20:15:49Z","attribution":"OSM"}]}"""));
        });

        var pack = Assert.Single(await Library().GetCatalogAsync());

        Assert.Equal("sta", pack.Slug);
    }

    [Fact]
    public async Task A_catalogue_that_is_no_json_is_reported_as_damaged()
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
    public async Task A_packs_area_comes_from_its_zip_and_nothing_more_is_fetched()
    {
        var zip = PackZip();
        _handler.Respond(_ => Ok(zip));

        var installed = await Library().InstallAsync(Pack(zip));

        Assert.Equal(Area, await File.ReadAllTextAsync(installed.BoundaryPath));
        Assert.Equal(1, _handler.Requests);
    }

    [Fact]
    public async Task An_area_inside_the_zip_that_pack_json_does_not_declare_refuses_the_pack()
    {
        var zip = Zip([.. Files("tiles", manifest => Regex.Replace(manifest, @", ""boundary\.geojson"": \{[^}]*\}", string.Empty))]);
        _handler.Respond(_ => Ok(zip));

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().InstallAsync(Pack(zip)));

        Assert.Equal("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).", e.Message);
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("""{"type": "Point", "coordinates": [11, 48]}""")]
    public async Task An_area_that_is_no_polygon_installs_nothing(string body)
    {
        var zip = Zip([.. Files(boundary: body)]);
        _handler.Respond(_ => Ok(zip));
        var library = Library();

        var e = await Assert.ThrowsAsync<RegionPackException>(() => library.InstallAsync(Pack(zip)));

        Assert.Equal("Die Gebietsgrenze des Kartenpakets ist beschädigt.", e.Message);
        Assert.Empty(library.GetInstalled());
    }

    [Fact]
    public async Task An_installed_packs_water_points_are_read_from_its_file()
    {
        var pack = WithWater("""{"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2, 48.1]}, "properties": {"emergency": "fire_hydrant"}}]}""");

        var point = Assert.Single(await Library().GetWaterPointsAsync(pack));

        Assert.Equal(WaterPointKind.Hydrant, point.Kind);
    }

    [Fact]
    public async Task Water_points_that_are_no_feature_collection_are_reported_as_damaged()
    {
        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetWaterPointsAsync(WithWater("<html>")));

        Assert.Equal("Die Wasserentnahmestellen des Kartenpakets sind beschädigt.", e.Message);
    }

    [Fact]
    public async Task A_pack_without_its_water_file_reports_it_unreadable()
    {
        var pack = new InstalledRegionPack(Pack(1), Directory.CreateDirectory(Path.Join(Root, "ffb")).FullName);

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetWaterPointsAsync(pack));

        Assert.Equal("Die Wasserentnahmestellen des Kartenpakets sind nicht lesbar.", e.Message);
    }

    [Fact]
    public async Task A_water_file_larger_than_its_cap_is_never_read()
    {
        var pack = WithWater(string.Empty);
        await using (var file = new FileStream(pack.WaterPath, FileMode.Open))
        {
            file.SetLength(WaterPointsGeoJson.MaxFileBytes + 1);
        }

        var e = await Assert.ThrowsAsync<RegionPackException>(() => Library().GetWaterPointsAsync(pack));

        Assert.Equal("Die Wasserentnahmestellen des Kartenpakets sind zu groß und werden nicht gezeigt.", e.Message);
    }

    [Fact]
    public async Task A_pack_without_its_address_file_finds_nothing() =>
        Assert.Empty(await Library().SearchAddressesAsync(WithWater("{}"), "Hauptstraße 1"));

    [Fact]
    public void Without_a_pack_folder_nothing_is_installed() => Assert.Empty(Library().GetInstalled());

    [Fact]
    public async Task A_folder_whose_record_names_another_pack_is_not_listed()
    {
        var zip = PackZip();
        _handler.Respond(_ => Ok(zip));
        var library = Library();
        var installed = await library.InstallAsync(Pack(zip));
        Directory.Move(installed.FolderPath, Path.Join(Root, "sta"));
        Directory.CreateDirectory(Path.Join(Root, "empty"));

        Assert.Empty(library.GetInstalled());
    }

    [Fact]
    public async Task Removing_a_pack_deletes_its_folder()
    {
        var zip = PackZip();
        _handler.Respond(_ => Ok(zip));
        var library = Library();
        var installed = await library.InstallAsync(Pack(zip));

        library.Remove(installed);

        Assert.False(Directory.Exists(installed.FolderPath));
        Assert.Empty(library.GetInstalled());
    }

    [Fact]
    public async Task A_pack_that_cannot_be_deleted_is_reported_by_name()
    {
        var zip = PackZip();
        _handler.Respond(_ => Ok(zip));
        var library = Library();
        var installed = await library.InstallAsync(Pack(zip));

        var e = WhileUndeletable(installed, () => Assert.Throws<RegionPackException>(() => library.Remove(installed)));

        Assert.Equal("„Landkreis Fürstenfeldbruck“ konnte nicht gelöscht werden. Wird die Karte gerade angezeigt?", e.Message);
    }

    [Fact]
    public void Removing_a_pack_whose_name_is_no_safe_slug_deletes_nothing_even_inside_the_pack_folder()
    {
        var other = Directory.CreateDirectory(Path.Join(Root, "KEEP")).FullName;

        Assert.Throws<RegionPackException>(() => Library().Remove(new InstalledRegionPack(Pack(1) with { Slug = "KEEP" }, other)));

        Assert.True(Directory.Exists(other));
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

    private InstalledRegionPack WithWater(string json)
    {
        var folder = Directory.CreateDirectory(Path.Join(Root, "ffb")).FullName;
        File.WriteAllText(Path.Join(folder, InstalledRegionPack.WaterFileName), json);
        return new InstalledRegionPack(Pack(1), folder);
    }

    private static RegionPackInfo Pack(long size) =>
        new("Landkreis Fürstenfeldbruck", "ffb", 1, "https://example.org/ffb-v1.zip", size, new string('a', 64), 10.95, 48.06, 11.45, 48.32, 11, 17, "2026-10-27T20:15:49Z", "© OpenStreetMap-Mitwirkende (ODbL)");

    // The catalogue entry for exactly this zip: its size and its SHA-256.
    private static RegionPackInfo Pack(byte[] zip) => Pack(zip.Length) with { Sha256 = Sha(zip) };

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Sha(string text) => Sha(Encoding.UTF8.GetBytes(text));

    private static byte[] PackZip(string tiles = "tiles") => Zip([.. Files(tiles)]);

    // The files of a well-formed pack, its pack.json declaring each data file truly.
    // shapeManifest bends that pack.json into one that lies. vectorMap describes map.pmtiles.
    // With rasterTiles, the pack also holds the map.mbtiles packs carried until lagebuch-geodata
    // stopped building them, declared as they declared it.
    private static List<(string Name, string Content)> Files(string tiles = "tiles", Func<string, string>? shapeManifest = null, string boundary = Area, string vectorMap = KnownVectorMap, string? rasterTiles = null)
    {
        (string Name, string Content)[] data = [("map.pmtiles", tiles), ("elevation.tif", "dem"), ("water.geojson", "water"), ("addresses.sqlite", "addresses"), ("boundary.geojson", boundary)];
        if (rasterTiles is not null)
        {
            data = [.. data, ("map.mbtiles", rasterTiles)];
        }

        var files = string.Join(", ", data.Select(d => $$"""
            "{{d.Name}}": {"bytes": {{Encoding.UTF8.GetByteCount(d.Content)}}, "sha256": "{{Sha(d.Content)}}"}
            """));
        var manifest = """{"formatVersion": 2, "slug": "ffb", "name": "Landkreis Fürstenfeldbruck", "version": 1, "vectorMap": """ + vectorMap + ", \"files\": {" + files + "}}";
        return [("pack.json", shapeManifest?.Invoke(manifest) ?? manifest), ("ATTRIBUTION.md", "© OpenStreetMap-Mitwirkende"), .. data];
    }

    // Windows refuses to delete a file someone holds open; Unix refuses to unlink from a folder
    // without write permission.
    private T WhileUndeletable<T>(InstalledRegionPack pack, Func<T> act)
    {
        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(pack.VectorTilesPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return act();
            }
        }

        var mode = File.GetUnixFileMode(Root);
        File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            return act();
        }
        finally
        {
            File.SetUnixFileMode(Root, mode);
        }
    }

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

    // A well-formed pack whose entry `name` holds `size` zeros instead, written in chunks so the
    // bomb never sits in memory whole.
    private static byte[] ZipWithBomb(string name, long size)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in Files())
            {
                using var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest).Open();
                if (entryName != name)
                {
                    entry.Write(Encoding.UTF8.GetBytes(content));
                    continue;
                }

                var zeros = new byte[1024 * 1024];
                for (long written = 0; written < size; written += zeros.Length)
                {
                    entry.Write(zeros, 0, (int)Math.Min(zeros.Length, size - written));
                }
            }
        }

        return buffer.ToArray();
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Reports { get; } = [];

        public void Report(double value) => Reports.Add(value);
    }

    // A body sent with no Content-Length (null), or with one that need not be true.
    private sealed class DeclaredLengthContent : HttpContent
    {
        private readonly byte[] _body;

        public DeclaredLengthContent(byte[] body, long? declaredLength)
        {
            _body = body;
            Headers.ContentLength = declaredLength;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
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
