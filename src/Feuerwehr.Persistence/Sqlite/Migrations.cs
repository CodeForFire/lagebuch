using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Sqlite;

public static class Migrations
{
    public const int CurrentVersion = 2;

    public static int GetVersion(SqliteConnection cn)
    {
        using (var create = cn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }
        using var read = cn.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var result = read.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    public static void Migrate(SqliteConnection cn)
    {
        var version = GetVersion(cn);
        using var tx = cn.BeginTransaction();
        if (version < 1)
        {
            ApplyV1(cn, tx);
        }
        if (version < 2)
        {
            ApplyV2(cn, tx);
        }
        SetVersion(cn, tx, CurrentVersion);
        tx.Commit();
    }

    private static void ApplyV1(SqliteConnection cn, SqliteTransaction tx)
    {
        Exec(cn, tx, """
            CREATE TABLE incident_meta (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                state INTEGER NOT NULL,
                incident_number TEXT,
                ils_number TEXT,
                keyword TEXT,
                street TEXT,
                district TEXT,
                status TEXT,
                closed_at TEXT,
                closed_by TEXT
            );
            """);
        Exec(cn, tx, """
            CREATE TABLE checklist_items (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                text TEXT NOT NULL,
                is_done INTEGER NOT NULL,
                note TEXT
            );
            """);
        Exec(cn, tx, """
            CREATE TABLE etb_entries (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                direction INTEGER NOT NULL,
                from_party TEXT,
                to_party TEXT,
                text TEXT NOT NULL,
                entered_by TEXT NOT NULL
            );
            """);
        Exec(cn, tx, """
            CREATE TABLE role_assignments (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                role TEXT NOT NULL,
                person_name TEXT NOT NULL,
                call_sign TEXT,
                from_time TEXT,
                to_time TEXT
            );
            """);
        Exec(cn, tx, """
            CREATE TABLE force_units (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                brigade TEXT NOT NULL,
                call_sign TEXT,
                personnel_count INTEGER NOT NULL,
                status TEXT,
                notes TEXT
            );
            """);
        Exec(cn, tx, """
            CREATE TABLE audit_events (
                ordinal INTEGER PRIMARY KEY,
                at TEXT NOT NULL,
                action TEXT NOT NULL,
                by_operator TEXT NOT NULL
            );
            """);
    }

    private static void ApplyV2(SqliteConnection cn, SqliteTransaction tx)
    {
        Exec(cn, tx, """
            CREATE TABLE scba_trupps (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                designation TEXT NOT NULL,
                members TEXT NOT NULL,
                call_sign TEXT,
                task TEXT,
                entry_time TEXT NOT NULL,
                entry_pressure INTEGER NOT NULL,
                max_duration_minutes INTEGER NOT NULL,
                return_pressure_bar INTEGER NOT NULL,
                exit_time TEXT
            );
            """);
        Exec(cn, tx, """
            CREATE TABLE scba_pressure_readings (
                id TEXT PRIMARY KEY,
                trupp_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                reading_time TEXT NOT NULL,
                bar INTEGER NOT NULL
            );
            """);
    }

    private static void SetVersion(SqliteConnection cn, SqliteTransaction tx, int version)
    {
        Exec(cn, tx, "DELETE FROM schema_version;");
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO schema_version (version) VALUES ($v);";
        cmd.Parameters.AddWithValue("$v", version);
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection cn, SqliteTransaction tx, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
