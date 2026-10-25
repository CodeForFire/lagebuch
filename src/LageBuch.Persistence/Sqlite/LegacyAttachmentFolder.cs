using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    private const string BlobLengthSql =
        "SELECT length(bytes) FROM " + IncidentFileBlobs.Table + " WHERE storage_name = $name;";

    private const string BlobDeleteSql =
        "DELETE FROM " + IncidentFileBlobs.Table + " WHERE storage_name = $name;";

    /// <summary>
    /// Copies every file the incident's attachments name into the database in one transaction,
    /// then deletes the originals. A file that cannot be read, is a link, or is over
    /// <paramref name="maxSizeBytes"/> is skipped and stays on disk: the Einsatz must open
    /// regardless. A SQLite failure still propagates, before anything on disk has been touched.
    /// Files nothing names are left alone.
    /// </summary>
    public static void Import(SqliteConnection cn, string incidentPath, long maxSizeBytes = IncidentFile.MaxSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(cn);
        ArgumentNullException.ThrowIfNull(incidentPath);

        var folder = FolderFor(incidentPath);
        if (!Directory.Exists(folder) || IsLink(new DirectoryInfo(folder)))
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
                if (!candidate.StartsWith(root, StringComparison.Ordinal))
                {
                    continue;
                }

                // A link could point at any file the user can read; copying it would put that
                // secret into an Einsatzdatei that gets synced or handed on.
                var info = new FileInfo(candidate);
                if (!info.Exists || IsLink(info))
                {
                    continue;
                }

                // A row that is already there means an earlier open imported this file and only its
                // cleanup failed; delete the original only if the stored bytes are the same size.
                var stored = BlobLength(cn, tx, storage);
                var done = stored is null
                    ? CopyIntoBlob(cn, tx, storage, candidate, maxSizeBytes)
                    : stored == info.Length;
                if (done)
                {
                    imported.Add(candidate);
                }
            }

            tx.Commit();
        }

        DeleteImported(imported, folder);
    }

    private static bool IsLink(FileSystemInfo info) =>
        info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    /// False when the file is over the cap or cannot be read: it stays on disk and the import goes on.
    /// A row begun for a file that then failed to read is removed again.
    /// </summary>
    private static bool CopyIntoBlob(SqliteConnection cn, SqliteTransaction tx, string storage, string path, long maxSizeBytes)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (source.Length > maxSizeBytes)
            {
                return false;
            }

            try
            {
                IncidentFileBlobs.Write(cn, tx, storage, source, source.Length);
            }
            catch (IOException)
            {
                RemoveBlob(cn, tx, storage);
                throw;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Cleanup after a committed import: a leftover file is retried on the next open and must never block opening the Einsatz.")]
    private static void DeleteImported(List<string> files, string folder)
    {
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                // Retried on the next open: the rows are already in the database.
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception)
        {
            // An undeletable folder is retried on the next open too.
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The table name is the compile-time constant IncidentFileBlobs.Table; the value is bound as a parameter.")]
    private static long? BlobLength(SqliteConnection cn, SqliteTransaction tx, string storage)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = BlobLengthSql;
        cmd.Parameters.AddWithValue("$name", storage);
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The table name is the compile-time constant IncidentFileBlobs.Table; the value is bound as a parameter.")]
    private static void RemoveBlob(SqliteConnection cn, SqliteTransaction tx, string storage)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = BlobDeleteSql;
        cmd.Parameters.AddWithValue("$name", storage);
        cmd.ExecuteNonQuery();
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
