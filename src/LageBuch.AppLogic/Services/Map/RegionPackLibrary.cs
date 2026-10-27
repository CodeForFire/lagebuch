using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Keeps the Kartenpakete under one folder, <c>&lt;root&gt;/&lt;slug&gt;/</c>, each with the
/// <c>pack.json</c> it was installed from. The catalogue and every zip come from the network, so
/// both are read against caps, and only the files a pack is known to hold are ever written.
/// </summary>
/// <remarks>
/// The only network calls Lagebuch makes on its own account, and only when the Lagebuchführer asks
/// for the list or a pack. Neither request carries anything about an Einsatz.
/// </remarks>
public sealed class RegionPackLibrary : IRegionPackLibrary
{
    /// <summary>A catalogue larger than this is not a list of Landkreise.</summary>
    public const long MaxCatalogBytes = 1024 * 1024;

    /// <summary>The largest zip accepted, whatever the catalogue claims. A zoom-15 Landkreis is about 16 MB.</summary>
    public const long MaxPackBytes = 1024L * 1024 * 1024;

    /// <summary>The most a pack may unpack to, all files together.</summary>
    public const long MaxUnpackedBytes = 2L * 1024 * 1024 * 1024;

    private const string InfoFileName = "pack.json";

    // Downloading is the slow part; the last tenth of the bar is left for unpacking.
    private const double DownloadShare = 0.9;

    /// <summary>The catalogue published from <c>CodeForFire/lagebuch-regions</c>.</summary>
    public static readonly Uri DefaultCatalogUrl = new("https://raw.githubusercontent.com/CodeForFire/lagebuch-regions/main/regions.json");

    // The files a pack is known to hold, each written under this fixed name. Nothing else in the
    // zip is unpacked, so no entry name ever reaches a path. region.dem is the Wasserförderung's
    // elevation grid (#587).
    private static readonly string[] PackFiles = [InstalledRegionPack.TilesFileName, "region.dem"];

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

            await using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await DownloadAsync(url, file, pack.SizeBytes, progress, "Das Kartenpaket", cancellationToken);
            }

            Unpack(zipPath, staging);
            if (!File.Exists(Path.Join(staging, InstalledRegionPack.TilesFileName)))
            {
                throw new RegionPackException("Das Kartenpaket enthält keine Karte (region.mbtiles fehlt).");
            }

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
    // lying Content-Length must not let it grow without bound.
    private async Task DownloadAsync(Uri url, Stream target, long maxBytes, IProgress<double>? progress, string what, CancellationToken cancellationToken)
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

    private static void Unpack(string zipPath, string staging)
    {
        Directory.CreateDirectory(staging);
        long unpacked = 0;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var name in PackFiles)
            {
                var entry = zip.GetEntry(name);
                if (entry is null)
                {
                    continue;
                }

                using var source = entry.Open();
                using var target = new FileStream(Path.Join(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var chunk = new byte[81920];
                int n;
                while ((n = source.Read(chunk, 0, chunk.Length)) > 0)
                {
                    unpacked += n;
                    if (unpacked > MaxUnpackedBytes)
                    {
                        throw new RegionPackException("Das Kartenpaket ist entpackt zu groß und wird nicht installiert.");
                    }

                    target.Write(chunk, 0, n);
                }
            }
        }
        catch (InvalidDataException e)
        {
            throw new RegionPackException("Das Kartenpaket ist beschädigt.", e);
        }
    }

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
