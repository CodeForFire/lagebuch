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

    /// <summary>Null on any failure (missing file, unreadable folder) — never throws, so a
    /// caller degrades quietly, matching <see cref="IncidentRepository.TryReadState"/>.</summary>
    Task<byte[]?> TryReadBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default);

    /// <summary>The real path on disk, for APIs that require a file path rather than bytes
    /// (QuestPDF's <c>DocumentOperation</c>). Does not guarantee the file exists.</summary>
    string ResolveDiskPath(string incidentPath, string storageFileName);
}

public sealed class IncidentFileStore : IIncidentFileStore
{
    public async Task SaveBytesAsync(string incidentPath, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        var folder = FolderFor(incidentPath);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, storageFileName), bytes, cancellationToken);
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

    private static string FolderFor(string incidentPath)
    {
        var directory = Path.GetDirectoryName(incidentPath);
        var stem = Path.GetFileNameWithoutExtension(incidentPath);
        return Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, stem + ".files");
    }
}
