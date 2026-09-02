using System.IO.Compression;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Downloads a region pack's zip (region.mbtiles + region.dem) and extracts it into
/// <c>&lt;regionsBaseDir&gt;/&lt;slug&gt;</c> (#150 follow-up). A re-install of the same slug replaces the
/// folder outright, so a pack update never leaves stale files from a previous version behind.
/// </summary>
public sealed class RegionPackInstaller(HttpClient httpClient, string regionsBaseDir) : IRegionPackInstaller
{
    // Downloading is the slow part; reserve a small tail of the progress range for extraction so
    // the caller sees forward motion continue past "download done" instead of jumping straight to 1.0.
    private const double DownloadProgressShare = 0.9;

    public async Task<string> DownloadAndInstallAsync(RegionPackInfo pack, IProgress<double>? progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        progress?.Report(0.0);

        var zipBytes = await DownloadAsync(pack.DownloadUrl, progress, ct);

        // Defense in depth: RegionPackCatalogJson already rejects unsafe slugs when parsing the
        // manifest, but a slug ending up here from anywhere else must not be able to escape
        // regionsBaseDir either.
        var baseFull = Path.GetFullPath(regionsBaseDir) + Path.DirectorySeparatorChar;
        var folder = Path.GetFullPath(Path.Combine(regionsBaseDir, pack.Slug));
        if (!folder.StartsWith(baseFull, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Region slug '{pack.Slug}' escapes the regions directory.");
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);

        using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            zip.ExtractToDirectory(folder);
        }

        progress?.Report(1.0);
        return folder;
    }

    private async Task<byte[]> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        long readSoFar = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            readSoFar += read;
            if (totalBytes is > 0)
            {
                progress?.Report(Math.Min(DownloadProgressShare, (double)readSoFar / totalBytes.Value * DownloadProgressShare));
            }
        }

        return buffer.ToArray();
    }
}
