using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LageBuch.Persistence.Map;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Keeps the Kartenpakete under one folder, <c>&lt;root&gt;/&lt;slug&gt;/</c>, each with the
/// catalogue entry it was installed from in <c>installed.json</c>. The catalogue and every zip
/// come from the network, so both are read against caps; a zip is unpacked only when its SHA-256
/// is the catalogue's, it holds exactly the files FORMAT.md names, and each matches the size and
/// SHA-256 its own <c>pack.json</c> declares.
/// </summary>
/// <remarks>
/// The only network calls Lagebuch makes on its own account, and only when the Lagebuchführer asks
/// for the list or a pack. Neither request carries anything about an Einsatz.
/// </remarks>
public sealed class RegionPackLibrary : IRegionPackLibrary
{
    /// <summary>A catalogue larger than this is not a list of Landkreise.</summary>
    public const long MaxCatalogBytes = 1024 * 1024;

    /// <summary>The largest zip accepted, whatever the catalogue claims. A Landkreis at zoom 17 with its DGM5 is about 270 MB.</summary>
    public const long MaxPackBytes = 1024L * 1024 * 1024;

    /// <summary>The most a pack may unpack to, all files together.</summary>
    public const long MaxUnpackedBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>A pack's own <c>pack.json</c> larger than this is not a description of its files.</summary>
    public const long MaxManifestBytes = 64 * 1024;

    // The app's record of the catalogue entry; pack.json is the publisher's and is kept as it came.
    private const string InfoFileName = "installed.json";

    private const string ManifestFileName = "pack.json";

    // Downloading is the slow part; the last tenth of the bar is left for unpacking.
    private const double DownloadShare = 0.9;

    /// <summary>The catalogue published from <c>CodeForFire/lagebuch-geodata</c>.</summary>
    public static readonly Uri DefaultCatalogUrl = new("https://raw.githubusercontent.com/CodeForFire/lagebuch-geodata/main/packs.json");

    // The data files pack.json lists with size and SHA-256. elevation.tif is the Wasserförderung's
    // terrain model (#587); it is installed and checked, not yet read.
    private static readonly string[] DataFiles =
        [InstalledRegionPack.TilesFileName, "elevation.tif", InstalledRegionPack.WaterFileName, InstalledRegionPack.AddressesFileName, InstalledRegionPack.BoundaryFileName];

    // Everything a zip holds, at its root: FORMAT.md says exactly these, and a zip with any other
    // entry is refused. Each is written under this fixed name, so no entry name reaches a path.
    private static readonly string[] PackFiles = [ManifestFileName, "ATTRIBUTION.md", .. DataFiles];

    private readonly HttpClient _http;
    private readonly string _root;
    private readonly Uri _catalogUrl;

    public RegionPackLibrary(HttpClient http, string root, Uri? catalogUrl = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _http = http;
        _root = Path.GetFullPath(root);
        _catalogUrl = catalogUrl ?? DefaultCatalogUrl;
    }

    public async Task<IReadOnlyList<RegionPackInfo>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await DownloadAsync(
            _catalogUrl,
            buffer,
            MaxCatalogBytes,
            null,
            null,
            "Die Liste der Kartenpakete",
            cancellationToken);

        var json = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return RegionPackCatalogJson.Parse(json)
            ?? throw new RegionPackException("Die Liste der Kartenpakete ist beschädigt.");
    }

    public IReadOnlyList<InstalledRegionPack> GetInstalled()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        try
        {
            var packs = new List<InstalledRegionPack>();
            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                var slug = Path.GetFileName(folder);
                if (!RegionPackCatalogJson.IsSafeSlug(slug)
                    || !new JsonFileStore<RegionPackInfo>(Path.Join(folder, InfoFileName)).TryRead(out var info)
                    || !string.Equals(info.Slug, slug, StringComparison.Ordinal))
                {
                    continue;
                }

                var pack = new InstalledRegionPack(info, folder);
                if (File.Exists(pack.TilesPath))
                {
                    packs.Add(pack);
                }
            }

            return packs.OrderBy(p => p.Info.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new RegionPackException("Die installierten Kartenpakete sind nicht lesbar.", e);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Cleanup of a half-installed pack must not mask the failure that caused it; that failure is rethrown as a RegionPackException.")]
    public async Task<InstalledRegionPack> InstallAsync(RegionPackInfo pack, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var url = CheckedDownloadUrl(pack);
        var folder = PackFolder(pack.Slug);
        var zipPath = Path.Join(_root, $".{pack.Slug}.download");
        var staging = Path.Join(_root, $".{pack.Slug}.staging");
        var previous = Path.Join(_root, $".{pack.Slug}.previous");

        progress?.Report(0);
        try
        {
            Directory.CreateDirectory(_root);
            DeleteDirectory(staging);
            DeleteDirectory(previous);

            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await DownloadAsync(url, file, pack.SizeBytes, hash, progress, "Das Kartenpaket", cancellationToken);
                }

                if (!SameHash(hash.GetHashAndReset(), pack.Sha256))
                {
                    throw new RegionPackException("Das Kartenpaket ist beschädigt (Prüfsumme stimmt nicht). Erneut herunterladen.");
                }
            }

            Unpack(zipPath, staging, pack);
            CheckBoundary(Path.Join(staging, InstalledRegionPack.BoundaryFileName));

            new JsonFileStore<RegionPackInfo>(Path.Join(staging, InfoFileName)).Write(pack);

            // Swap the folders so that a failure here leaves the older copy rather than nothing.
            if (Directory.Exists(folder))
            {
                Directory.Move(folder, previous);
            }

            Directory.Move(staging, folder);
            DeleteDirectory(previous);
            progress?.Report(1);
            return new InstalledRegionPack(pack, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!Directory.Exists(folder) && Directory.Exists(previous))
            {
                try
                {
                    Directory.Move(previous, folder);
                }
                catch (Exception)
                {
                    // The original failure is what the Lagebuchführer needs to read; it follows.
                }
            }

            throw new RegionPackException("Das Kartenpaket konnte nicht gespeichert werden. Freien Speicher prüfen.", e);
        }
        finally
        {
            TryDelete(zipPath);
            TryDeleteDirectory(staging);
        }
    }

    public async Task<IReadOnlyList<WaterPoint>> GetWaterPointsAsync(InstalledRegionPack pack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        byte[] json;
        try
        {
            if (new FileInfo(pack.WaterPath).Length > WaterPointsGeoJson.MaxFileBytes)
            {
                throw new RegionPackException("Die Wasserentnahmestellen des Kartenpakets sind zu groß und werden nicht gezeigt.");
            }

            json = await File.ReadAllBytesAsync(pack.WaterPath, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new RegionPackException("Die Wasserentnahmestellen des Kartenpakets sind nicht lesbar.", e);
        }

        return await Task.Run(() => WaterPointsGeoJson.Parse(json), cancellationToken)
            ?? throw new RegionPackException("Die Wasserentnahmestellen des Kartenpakets sind beschädigt.");
    }

    public Task<IReadOnlyList<AddressHit>> SearchAddressesAsync(InstalledRegionPack pack, string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(query);
        var index = new AddressIndex(pack.AddressesPath);
        return Task.Run(() => index.Search(query, cancellationToken: cancellationToken), cancellationToken);
    }

    public void Remove(InstalledRegionPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        try
        {
            DeleteDirectory(PackFolder(pack.Info.Slug));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new RegionPackException($"„{pack.Info.Name}“ konnte nicht gelöscht werden. Wird die Karte gerade angezeigt?", e);
        }
    }

    // The slug was checked when the catalogue was read, but a pack can reach here from a pack.json
    // too, so the folder is contained again rather than trusted.
    private string PackFolder(string slug)
    {
        if (!RegionPackCatalogJson.IsSafeSlug(slug))
        {
            throw new RegionPackException("Das Kartenpaket hat einen ungültigen Namen.");
        }

        var folder = Path.GetFullPath(Path.Join(_root, slug));
        if (!folder.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new RegionPackException("Das Kartenpaket hat einen ungültigen Namen.");
        }

        return folder;
    }

    private static Uri CheckedDownloadUrl(RegionPackInfo pack)
    {
        if (!Uri.TryCreate(pack.DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new RegionPackException("Das Kartenpaket hat keine gültige https-Adresse.");
        }

        if (pack.SizeBytes is <= 0 or > MaxPackBytes)
        {
            throw new RegionPackException($"Das Kartenpaket ist größer als {MaxPackBytes / (1024 * 1024)} MB und wird nicht geladen.");
        }

        return url;
    }

    // Reads the body into target, refusing it as soon as it outgrows maxBytes -- a missing or
    // lying Content-Length must not let it grow without bound. Every byte also goes into hash.
    private async Task DownloadAsync(Uri url, Stream target, long maxBytes, IncrementalHash? hash, IProgress<double>? progress, string what, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new RegionPackException($"{what} ist nicht abrufbar (HTTP {(int)response.StatusCode}).");
            }

            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new RegionPackException($"{what} ist größer als angekündigt. Download abgebrochen.");
            }

            var total = response.Content.Headers.ContentLength ?? maxBytes;
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var chunk = new byte[81920];
            long read = 0;
            int n;
            while ((n = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                read += n;
                if (read > maxBytes)
                {
                    throw new RegionPackException($"{what} ist größer als angekündigt. Download abgebrochen.");
                }

                hash?.AppendData(chunk, 0, n);
                await target.WriteAsync(chunk.AsMemory(0, n), cancellationToken);
                progress?.Report(Math.Min(DownloadShare, (double)read / total * DownloadShare));
            }
        }
        catch (HttpRequestException e)
        {
            throw new RegionPackException($"{what} ist nicht erreichbar. Internetverbindung prüfen.", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation nobody asked for.
            throw new RegionPackException($"{what} hat nicht rechtzeitig geantwortet. Später erneut versuchen.", e);
        }
    }

    // Unpacks the pack under fixed names. pack.json comes first, so that every other file is read
    // only up to the size it declares: an entry that outgrows it is cut off where it lies, not at
    // the 2 GB cap, and is then checked against its declared SHA-256.
    private static void Unpack(string zipPath, string staging, RegionPackInfo pack)
    {
        Directory.CreateDirectory(staging);
        long total = 0;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            if (names.Count != PackFiles.Length
                || !names.Order(StringComparer.Ordinal).SequenceEqual(PackFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new RegionPackException("Das Kartenpaket hat einen unerwarteten Inhalt und wird nicht installiert.");
            }

            var manifestPath = Path.Join(staging, ManifestFileName);
            Extract(zip, ManifestFileName, manifestPath, MaxManifestBytes, ref total);
            var declared = ReadManifest(manifestPath, pack, DataFiles);
            Extract(zip, "ATTRIBUTION.md", Path.Join(staging, "ATTRIBUTION.md"), MaxManifestBytes, ref total);
            foreach (var (name, (bytes, sha256)) in declared)
            {
                var (written, hash) = Extract(zip, name, Path.Join(staging, name), bytes, ref total);
                if (written != bytes || !SameHash(hash, sha256))
                {
                    throw Damaged();
                }
            }
        }
        catch (InvalidDataException e)
        {
            throw new RegionPackException("Das Kartenpaket ist beschädigt.", e);
        }
    }

    // Writes one entry, refusing it once it outgrows limit or the pack outgrows MaxUnpackedBytes.
    private static (long Bytes, byte[] Sha256) Extract(ZipArchive zip, string name, string path, long limit, ref long total)
    {
        var entry = zip.GetEntry(name) ?? throw new RegionPackException("Das Kartenpaket ist beschädigt.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var source = entry.Open();
        using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var chunk = new byte[81920];
        long bytes = 0;
        int n;
        while ((n = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            bytes += n;
            total += n;
            if (total > MaxUnpackedBytes)
            {
                throw new RegionPackException("Das Kartenpaket ist entpackt zu groß und wird nicht installiert.");
            }

            if (bytes > limit)
            {
                throw Damaged();
            }

            hash.AppendData(chunk, 0, n);
            target.Write(chunk, 0, n);
        }

        return (bytes, hash.GetHashAndReset());
    }

    // The pack's own pack.json must describe the pack the catalogue offered and declare a size and
    // SHA-256 for each of names: a zip swapped on the way, or built wrongly, is not installed.
    private static Dictionary<string, (long Bytes, string Sha256)> ReadManifest(string path, RegionPackInfo pack, IReadOnlyList<string> names)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("formatVersion", out var format) || format.ValueKind != JsonValueKind.Number)
            {
                throw Damaged();
            }

            if (!format.TryGetInt32(out var version) || version != RegionPackCatalogJson.FormatVersion)
            {
                throw new RegionPackException("Das Kartenpaket braucht eine neuere Lagebuch-Version. Lagebuch aktualisieren.");
            }

            if (!root.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String
                || !string.Equals(slug.GetString(), pack.Slug, StringComparison.Ordinal)
                || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
            {
                throw Damaged();
            }

            var declared = new Dictionary<string, (long Bytes, string Sha256)>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!files.TryGetProperty(name, out var file) || file.ValueKind != JsonValueKind.Object
                    || !file.TryGetProperty("bytes", out var b) || b.ValueKind != JsonValueKind.Number
                    || !b.TryGetInt64(out var bytes) || bytes < 0
                    || !file.TryGetProperty("sha256", out var sha) || sha.ValueKind != JsonValueKind.String
                    || sha.GetString() is not { } sha256 || !RegionPackCatalogJson.IsSha256(sha256))
                {
                    throw Damaged();
                }

                declared[name] = (bytes, sha256);
            }

            return declared;
        }
        catch (JsonException e)
        {
            throw new RegionPackException(Damaged().Message, e);
        }
    }

    private static RegionPackException Damaged() => new("Das Kartenpaket ist beschädigt (pack.json passt nicht zum Inhalt).");

    // The pack's area, as its zip carried it: it has to be an area, or the pack is not installed.
    private static void CheckBoundary(string path)
    {
        if (new FileInfo(path).Length > BoundaryGeoJson.MaxFileBytes || BoundaryGeoJson.Parse(File.ReadAllBytes(path)) is null)
        {
            throw new RegionPackException("Die Gebietsgrenze des Kartenpakets ist beschädigt.");
        }
    }

    private static bool SameHash(byte[] actual, string? expectedHex) =>
        RegionPackCatalogJson.IsSha256(expectedHex)
        && CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedHex));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a leftover temporary file is deleted by the next install before it starts.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a leftover staging folder is deleted by the next install before it starts.
        }
    }
}
