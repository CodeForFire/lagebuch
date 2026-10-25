using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.Files;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Sqlite;

/// <summary>
/// Older builds kept attachment bytes in a <c>&lt;stem&gt;.files</c> folder beside the incident
/// file. <see cref="Import"/> moves them into <c>incident_file_blobs</c> when such a file is opened,
/// so the Einsatz becomes a single self-contained file.
/// </summary>
internal static class LegacyAttachmentFolder
{
    private const string BlobExistsSql =
        "SELECT 1 FROM " + IncidentFileBlobs.Table + " WHERE storage_name = $name;";

    /// <summary>
    /// Copies every file the incident's attachments name into the database in one transaction,
    /// then deletes the originals. A failure before the commit propagates and nothing on disk has
    /// been touched. Files nothing names are left alone.
    /// </summary>
    public static void Import(SqliteConnection cn, string incidentPath)
    {
        ArgumentNullException.ThrowIfNull(cn);
        ArgumentNullException.ThrowIfNull(incidentPath);

        var folder = FolderFor(incidentPath);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var imported = new List<string>();

        IncidentFileBlobs.EnsureTable(cn, null);
        using (var tx = cn.BeginTransaction())
        {
            foreach (var (id, fileName) in ReadRows(cn, tx))
            {
                if (!Guid.TryParse(id, out var guid))
                {
                    continue;
                }

                var storage = IncidentFile.StorageFileName(guid, fileName);
                var candidate = Path.GetFullPath(Path.Join(folder, storage));
                if (!candidate.StartsWith(root, StringComparison.Ordinal) || !File.Exists(candidate))
                {
                    continue;
                }

                // A row that is already there means an earlier open imported this file and only its
                // cleanup failed; the bytes are safe, so just finish the cleanup.
                if (BlobExists(cn, tx, storage) || CopyIntoBlob(cn, tx, storage, candidate))
                {
                    imported.Add(candidate);
                }
            }

            tx.Commit();
        }

        DeleteImported(imported, folder);
    }

    /// <summary>False when the file is over the cap: it stays on disk, as it never could have been attached.</summary>
    private static bool CopyIntoBlob(SqliteConnection cn, SqliteTransaction tx, string storage, string path)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length > IncidentFile.MaxSizeBytes)
        {
            return false;
        }

        IncidentFileBlobs.Write(cn, tx, storage, source, source.Length);
        return true;
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Cleanup after a committed import: a leftover file is retried on the next open and must never block opening the Einsatz.")]
    private static void DeleteImported(List<string> files, string folder)
    {
        try
        {
            foreach (var file in files)
            {
                File.Delete(file);
            }

            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception)
        {
            // Retried on the next open: the rows are already in the database.
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The table name is the compile-time constant IncidentFileBlobs.Table; the value is bound as a parameter.")]
    private static bool BlobExists(SqliteConnection cn, SqliteTransaction tx, string storage)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = BlobExistsSql;
        cmd.Parameters.AddWithValue("$name", storage);
        return cmd.ExecuteScalar() is not null;
    }

    private static List<(string Id, string FileName)> ReadRows(SqliteConnection cn, SqliteTransaction tx)
    {
        var rows = new List<(string, string)>();
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, file_name FROM incident_files;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private static string FolderFor(string incidentPath)
    {
        var directory = Path.GetDirectoryName(incidentPath);
        var stem = Path.GetFileNameWithoutExtension(incidentPath);
        return Path.Join(string.IsNullOrEmpty(directory) ? "." : directory, stem + ".files");
    }
}
