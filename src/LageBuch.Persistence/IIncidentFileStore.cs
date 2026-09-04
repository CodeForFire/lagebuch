namespace LageBuch.Persistence;

/// <summary>
/// Stores an attached file's bytes on disk in a sibling folder next to its <c>.fwincident</c>
/// file, keeping large binary content out of <see cref="IncidentRepository"/>'s full-rewrite-
/// per-save SQLite tables. Only the metadata row (see <c>incident_files</c>) lives in SQLite.
/// Every read/write here is stream-based (issue #167 P1 file-ops slimdown) — a caller that needs
/// bytes in memory (e.g. QuestPDF image embedding) opens its own stream via
/// <see cref="ResolveDiskPath"/> rather than this store ever materializing a whole attachment.
/// </summary>
public interface IIncidentFileStore
{
    /// <summary>
    /// Writes <paramref name="source"/> straight to disk without ever materializing the whole
    /// attachment in memory (issue #167 P1 #2) — used for a joined client's upload, whose bytes arrive
    /// as a raw HTTP request body rather than a JSON-decoded <c>byte[]</c>.
    /// </summary>
    Task SaveStreamAsync(string incidentPath, string storageFileName, Stream source, CancellationToken cancellationToken = default);

    /// <summary>The real path on disk, for APIs that require a file path rather than bytes
    /// (QuestPDF's <c>DocumentOperation</c> and <c>Image</c>). Does not guarantee the file exists.</summary>
    string ResolveDiskPath(string incidentPath, string storageFileName);
}

public sealed class IncidentFileStore : IIncidentFileStore
{
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

    public string ResolveDiskPath(string incidentPath, string storageFileName) =>
        Path.Combine(FolderFor(incidentPath), storageFileName);

    private static string FolderFor(string incidentPath)
    {
        var directory = Path.GetDirectoryName(incidentPath);
        var stem = Path.GetFileNameWithoutExtension(incidentPath);
        return Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, stem + ".files");
    }
}
