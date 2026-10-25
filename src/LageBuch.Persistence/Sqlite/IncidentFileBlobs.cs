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
    /// Stores <paramref name="length"/> bytes from <paramref name="source"/> under
    /// <paramref name="storageName"/>, replacing any earlier row. A <c>zeroblob</c> reserves the
    /// row and the bytes are then streamed into it, so the attachment is never held in memory whole.
    /// The caller owns the transaction, so a failed copy leaves the previous row untouched.
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
}
