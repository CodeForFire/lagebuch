namespace Feuerwehr.Persistence;

/// <summary>
/// Stores an attached file's bytes on disk in a sibling folder next to its <c>.fwincident</c>
/// file, keeping large binary content out of <see cref="IncidentRepository"/>'s full-rewrite-
/// per-save SQLite tables. Only the metadata row (see <c>incident_files</c>) lives in SQLite.
/// </summary>
public interface IIncidentFileStore
{
    void SaveBytes(string incidentPath, string storageFileName, byte[] bytes);

    /// <summary>Null on any failure (missing file, unreadable folder) — never throws, so a
    /// caller degrades quietly, matching <see cref="IncidentRepository.TryReadState"/>.</summary>
    byte[]? TryReadBytes(string incidentPath, string storageFileName);

    /// <summary>The real path on disk, for APIs that require a file path rather than bytes
    /// (QuestPDF's <c>DocumentOperation</c>). Does not guarantee the file exists.</summary>
    string ResolveDiskPath(string incidentPath, string storageFileName);
}

public sealed class IncidentFileStore : IIncidentFileStore
{
    public void SaveBytes(string incidentPath, string storageFileName, byte[] bytes)
    {
        var folder = FolderFor(incidentPath);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, storageFileName), bytes);
    }

    public byte[]? TryReadBytes(string incidentPath, string storageFileName)
    {
        try
        {
            var path = ResolveDiskPath(incidentPath, storageFileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
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
