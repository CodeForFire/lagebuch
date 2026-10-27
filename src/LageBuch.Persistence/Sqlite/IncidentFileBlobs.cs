using System.Globalization;
using LageBuch.Domain.Files;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Sqlite;

/// <summary>
/// The attachment bytes of an Einsatz, one row per stored file in <c>incident_file_blobs</c>.
/// The table is deliberately outside the table list <see cref="IncidentRepository"/> clears and
/// rewrites on every save: a save rewrites the metadata, never megabytes of photos.
/// </summary>
internal static class IncidentFileBlobs
{
    public const string Table = "incident_file_blobs";

    private const string CreateSql =
        "CREATE TABLE IF NOT EXISTS " + Table + " (storage_name TEXT PRIMARY KEY, bytes BLOB NOT NULL);";

    private const string ReplaceSql =
        "INSERT OR REPLACE INTO " + Table + " (storage_name, bytes) VALUES ($name, zeroblob($len));";

    private const string RowIdSql =
        "SELECT rowid FROM " + Table + " WHERE storage_name = $name;";

    private const string StoredNamesSql =
        "SELECT storage_name FROM " + Table + ";";

    private const string DeleteSql =
        "DELETE FROM " + Table + " WHERE storage_name = $name;";

    private const int BufferSize = 81920;

    /// <summary>
    /// Creates the table when the file lacks it. Called by <c>ApplyV31</c> and again before every
    /// store operation, because a file stamped with the current schema version by a parallel
    /// branch's build can be missing a table no <c>version &lt; N</c> step will ever add back.
    /// </summary>
    public static void EnsureTable(SqliteConnection cn, SqliteTransaction? tx)
    {
        ArgumentNullException.ThrowIfNull(cn);
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = CreateSql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The one way to open the incident file for a write that frees blob pages -- a delete, or a
    /// replace of an existing row. <c>secure_delete</c> makes SQLite zero the freed pages instead
    /// of leaving the old bytes readable inside a file that gets handed on. It is per connection
    /// and not persisted, so it is set here rather than once at migration. Also ensures the table.
    /// </summary>
    public static SqliteConnection OpenForWrite(string incidentPath)
    {
        var cn = SqliteConnectionFactory.OpenExisting(incidentPath);
        try
        {
            using (var pragma = cn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA secure_delete = ON;";
                pragma.ExecuteNonQuery();
            }

            EnsureTable(cn, null);
            return cn;
        }
        catch
        {
            cn.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The storage names the incident's attachments key their bytes under, one per
    /// <c>incident_files</c> row. Keyed on the name <c>Rehydrate</c> makes of the row, which is what
    /// every reader passes to <see cref="IncidentFile.StorageFileName"/>: the raw name can carry
    /// anything a crafted file puts there, a NUL or an extension the sanitiser drops. A row whose id
    /// is no Guid names nothing, since no reader could ever ask for its bytes.
    /// </summary>
    public static HashSet<string> AttachedNames(SqliteConnection cn, SqliteTransaction? tx)
    {
        ArgumentNullException.ThrowIfNull(cn);
        var names = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, file_name FROM incident_files;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
            {
                names.Add(IncidentFile.StorageFileName(id, IncidentFile.RehydratedFileName(id, reader.GetString(1))));
            }
        }

        return names;
    }

    /// <summary>
    /// Wipes, with <c>secure_delete</c>, every row of bytes no attachment names (#761): what a
    /// crash or a failed wipe between a removal's two deletes left behind, or an upload that raced
    /// the removal of its attachment. Run on open, when no write of this incident is queued, so it
    /// cannot take bytes whose metadata is still on its way. A file with nothing to wipe is only
    /// read, never written.
    /// </summary>
    public static void DeleteDetached(SqliteConnection cn)
    {
        ArgumentNullException.ThrowIfNull(cn);
        if (Detached(cn, null).Count == 0)
        {
            // Stryker disable once Statement : without the return an empty transaction commits, which changes nothing in the file.
            return;
        }

        using (var pragma = cn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA secure_delete = ON;";
            pragma.ExecuteNonQuery();
        }

        using var tx = cn.BeginTransaction();
        foreach (var name in Detached(cn, tx))
        {
            using var delete = cn.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = DeleteSql;
            delete.Parameters.AddWithValue("$name", name);
            delete.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// Stores <paramref name="length"/> bytes from <paramref name="source"/> under
    /// <paramref name="storageName"/>, replacing any earlier row. A <c>zeroblob</c> reserves the
    /// row and the bytes are then streamed into it, so the attachment is never held in memory whole.
    /// The caller owns the transaction, so a failed copy leaves the previous row untouched.
    /// Throws <see cref="KeyNotFoundException"/> when no attachment names
    /// <paramref name="storageName"/> (#761). Every caller saves the metadata first, so a missing
    /// row means the attachment was removed while its bytes were on their way; storing them would
    /// leave bytes in the Einsatzdatei that no removal will ever wipe. The check runs inside the
    /// write transaction, so a removal cannot slip in between it and the insert.
    /// </summary>
    public static void Write(SqliteConnection cn, SqliteTransaction tx, string storageName, Stream source, long length)
    {
        ArgumentNullException.ThrowIfNull(cn);
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(storageName);
        ArgumentNullException.ThrowIfNull(source);
        if (length is < 0 or > IncidentFile.MaxSizeBytes)
        {
            throw new InvalidDataException(IncidentFile.TooLargeMessage);
        }

        if (!AttachedNames(cn, tx).Contains(storageName))
        {
            // Stryker disable once String : callers act on the exception type; the text is only for a reader of a trace.
            throw new KeyNotFoundException("Der Anhang wurde inzwischen entfernt.");
        }

        using (var insert = cn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = ReplaceSql;
            insert.Parameters.AddWithValue("$name", storageName);
            insert.Parameters.AddWithValue("$len", length);
            insert.ExecuteNonQuery();
        }

        long rowId;
        using (var select = cn.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = RowIdSql;
            select.Parameters.AddWithValue("$name", storageName);
            rowId = Convert.ToInt64(select.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var blob = new SqliteBlob(cn, Table, "bytes", rowId, readOnly: false);
        var buffer = new byte[BufferSize];
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("Der Anhang endete vor der angegebenen Länge.");
            }

            blob.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static List<string> Detached(SqliteConnection cn, SqliteTransaction? tx)
    {
        var attached = AttachedNames(cn, tx);
        var detached = new List<string>();
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = StoredNamesSql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (!attached.Contains(name))
            {
                detached.Add(name);
            }
        }

        return detached;
    }
}
