using System.Diagnostics.CodeAnalysis;

namespace LageBuch.Persistence;

/// <summary>
/// Stores an attached file's bytes on disk in a sibling folder next to its <c>.fwincident</c>
/// file, keeping large binary content out of <see cref="IncidentRepository"/>'s full-rewrite-
/// per-save SQLite tables. Only the metadata row (see <c>incident_files</c>) lives in SQLite.
/// </summary>
public interface IIncidentFileStore
{
    Task SaveBytesAsync(string incidentPath, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="source"/> straight to disk without ever materializing the whole
    /// attachment in memory (issue #167 P1 #2) — used for a joined client's upload, whose bytes arrive
    /// as a raw HTTP request body rather than a JSON-decoded <c>byte[]</c>.
    /// </summary>
    Task SaveStreamAsync(string incidentPath, string storageFileName, Stream source, CancellationToken cancellationToken = default);

    /// <summary>Null on any failure (missing file, unreadable folder) — never throws, so a
    /// caller degrades quietly, matching <see cref="IncidentRepository.TryReadState"/>.</summary>
    Task<byte[]?> TryReadBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default);

    /// <summary>The real path on disk, for APIs that require a file path rather than bytes
    /// (QuestPDF's <c>DocumentOperation</c>). Does not guarantee the file exists.</summary>
    string ResolveDiskPath(string incidentPath, string storageFileName);

    /// <summary>
    /// Best-effort: swallows any I/O failure (missing file, unreadable folder) rather than
    /// throwing. An orphaned blob left on disk is an acceptable degradation -- it must never block
    /// the metadata removal (<see cref="LageBuch.Domain.Incident.RemoveFile"/>) that already happened.
    /// </summary>
    Task DeleteBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default);
}

public sealed class IncidentFileStore : IIncidentFileStore
{
    public async Task SaveBytesAsync(string incidentPath, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        var folder = FolderFor(incidentPath);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, storageFileName), bytes, cancellationToken);
    }

    public async Task SaveStreamAsync(string incidentPath, string storageFileName, Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var folder = FolderFor(incidentPath);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, storageFileName);

        // Write to a sibling temp file first and move it into place only once the whole upload has
        // landed, so a same-id retry after a dropped connection can never leave a half-written file
        // at the real path (idempotent — the final Move overwrites any previous complete upload).
        var tempPath = path + ".upload";
        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await source.CopyToAsync(fileStream, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Try-read: an unreadable attachment degrades to null, never fails incident load.")]
    public async Task<byte[]?> TryReadBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = ResolveDiskPath(incidentPath, storageFileName);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
        }
        catch
        {
            return null;
        }
    }

    public string ResolveDiskPath(string incidentPath, string storageFileName) =>
        Path.Combine(FolderFor(incidentPath), storageFileName);

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort delete: an orphaned blob on disk is an acceptable degradation, never blocking the metadata removal that already happened.")]
    public Task DeleteBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default)
    {
        try
        {
            File.Delete(ResolveDiskPath(incidentPath, storageFileName));
        }
        catch
        {
            // Swallow -- see the doc comment on IIncidentFileStore.DeleteBytesAsync.
        }

        return Task.CompletedTask;
    }

    private static string FolderFor(string incidentPath)
    {
        var directory = Path.GetDirectoryName(incidentPath);
        var stem = Path.GetFileNameWithoutExtension(incidentPath);
        return Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, stem + ".files");
    }
}
