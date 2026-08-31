using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Sqlite;

public static class SqliteConnectionFactory
{
    public static SqliteConnection OpenReadWrite(string path) =>
        Open(path, SqliteOpenMode.ReadWriteCreate, journal: true);

    /// <summary>
    /// Read-write, but never creates. For paths that are supposed to already hold an incident --
    /// where conjuring an empty database silently replaces "this file is gone" with a valid-looking
    /// empty Einsatz. <see cref="OpenReadWrite"/> stays creating for Save and for seeding master data.
    /// </summary>
    public static SqliteConnection OpenExisting(string path) =>
        Open(path, SqliteOpenMode.ReadWrite, journal: true);

    public static SqliteConnection OpenReadOnly(string path) =>
        Open(path, SqliteOpenMode.ReadOnly, journal: false);

    /// <summary>
    /// Opens a connection and applies the standard PRAGMAs, disposing it if any of that fails.
    ///
    /// The cleanup is the point. Open() succeeds for any file that exists, so a file that is not a
    /// database only fails at the first PRAGMA -- and the connection is then already open but not
    /// yet returned, so the caller's `using` never receives it and nothing closes it. Windows keeps
    /// the file locked for the rest of the process: undeletable, and unreadable by anything else.
    /// Linux hides the leak entirely, which is why it survived until a Windows CI run caught it.
    /// </summary>
    private static SqliteConnection Open(string path, SqliteOpenMode mode, bool journal)
    {
        var cn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,

            // Pooling keeps the sqlite handle open after Dispose, which on Windows keeps the file
            // locked. For a desktop app whose whole job is opening and closing the user's Einsatz
            // files, a handle outliving its connection is a bug, not an optimisation -- and the
            // pool saves nothing here, since a file is opened a handful of times per session.
            Pooling = false,
        }.ToString());

        try
        {
            cn.Open();

            // WAL is a write to the database header, so it is meaningless -- and refused -- on a
            // read-only connection.
            if (journal)
            {
                Execute(cn, "PRAGMA journal_mode=WAL;");
            }

            Execute(cn, "PRAGMA foreign_keys=ON;");
            return cn;
        }
        catch
        {
            cn.Dispose();
            throw;
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Audited: PRAGMA statements are compile-time constants.")]
    private static void Execute(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
