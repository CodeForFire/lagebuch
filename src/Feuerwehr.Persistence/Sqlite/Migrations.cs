using Feuerwehr.Domain.Atemschutz;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Sqlite;

public static class Migrations
{
    public const int CurrentVersion = 5;

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

        // Refuse a file from the future rather than treating "no migration applies" as "already
        // current". Without this, SetVersion below stamps CurrentVersion over the higher marker,
        // so the file silently claims a schema it does not have -- and the next read fails deep in
        // a SELECT against a column the newer build had already dropped.
        if (version > CurrentVersion)
            throw new UnsupportedSchemaVersionException(version, CurrentVersion);

        using var tx = cn.BeginTransaction();
        if (version < 1)
        {
            ApplyV1(cn, tx);
        }
        if (version < 2)
        {
            ApplyV2(cn, tx);
        }
        if (version < 3)
        {
            ApplyV3(cn, tx);
        }
        if (version < 4)
        {
            ApplyV4(cn, tx);
        }
        if (version < 5)
        {
            ApplyV5(cn, tx);
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

    private static void ApplyV3(SqliteConnection cn, SqliteTransaction tx)
    {
        // Reshape scba_trupps: registration is now separate from going under air. A Trupp gains a
        // registered_at and a nullable start_time/start_pressure (null while on standby), plus a
        // pressure-control interval. Rebuild the table (portable across SQLite versions) and map
        // any existing V2 rows — those were already "under air", so start == entry.
        Exec(cn, tx, """
            CREATE TABLE scba_trupps_v3 (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                designation TEXT NOT NULL,
                members TEXT NOT NULL,
                call_sign TEXT,
                task TEXT,
                registered_at TEXT NOT NULL,
                start_time TEXT,
                start_pressure INTEGER,
                max_duration_minutes INTEGER NOT NULL,
                return_pressure_bar INTEGER NOT NULL,
                pressure_control_interval_minutes INTEGER NOT NULL,
                exit_time TEXT
            );
            """);
        Exec(cn, tx, $"""
            INSERT INTO scba_trupps_v3
                (id, ordinal, designation, members, call_sign, task, registered_at, start_time,
                 start_pressure, max_duration_minutes, return_pressure_bar,
                 pressure_control_interval_minutes, exit_time)
            SELECT id, ordinal, designation, members, call_sign, task, entry_time, entry_time,
                   entry_pressure, max_duration_minutes, return_pressure_bar,
                   {AtemschutzTrupp.DefaultPressureControlIntervalMinutes}, exit_time
            FROM scba_trupps;
            """);
        Exec(cn, tx, "DROP TABLE scba_trupps;");
        Exec(cn, tx, "ALTER TABLE scba_trupps_v3 RENAME TO scba_trupps;");
    }

    private static void ApplyV4(SqliteConnection cn, SqliteTransaction tx)
    {
        // Funktionszuweisung gains the two columns the paper form always had: Abschnitt (the
        // "Zusatz" field) and the person's mobile number. Both are nullable, so widening the
        // table in place is enough — no rebuild-and-copy as in V3.
        AddColumnIfMissing(cn, tx, "role_assignments", "section", "TEXT");
        AddColumnIfMissing(cn, tx, "role_assignments", "phone", "TEXT");
    }

    private static void ApplyV5(SqliteConnection cn, SqliteTransaction tx)
    {
        // Kräfteübersicht counts Atemschutzgeräteträger alongside the crew total. NOT NULL with a
        // default of 0 is safe here: existing rows genuinely have no recorded AGT count, and 0
        // reads the same as "none recorded" for the sum that feeds the header tile.
        AddColumnIfMissing(cn, tx, "force_units", "scba_count", "INTEGER NOT NULL DEFAULT 0");
    }

    /// <summary>
    /// ALTER TABLE ADD COLUMN, made safe to re-run. SQLite has no IF NOT EXISTS for columns, and a
    /// duplicate ADD is a hard error that aborts the whole migration transaction.
    ///
    /// Both guards earn their keep against real states this repo already produces. A file can
    /// carry a version marker older than its physical schema (a downgrade, a restored backup, or
    /// a partially-completed upgrade), which re-runs this migration over columns that exist. And a
    /// file can reach here without the table at all, because a version marker only promises that
    /// the *numbered* migrations ran, not that every table survived. Skipping is the right answer
    /// in both cases: the goal is that the column exists afterwards, not that this particular
    /// statement executed.
    /// </summary>
    private static void AddColumnIfMissing(
        SqliteConnection cn, SqliteTransaction tx, string table, string column, string type)
    {
        if (!TableExists(cn, tx, table) || ColumnExists(cn, tx, table, column))
            return;
        Exec(cn, tx, $"ALTER TABLE {table} ADD COLUMN {column} {type};");
    }

    private static bool TableExists(SqliteConnection cn, SqliteTransaction tx, string table)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private static bool ColumnExists(SqliteConnection cn, SqliteTransaction tx, string table, string column)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name=$c;";
        cmd.Parameters.AddWithValue("$c", column);
        return (long)cmd.ExecuteScalar()! > 0;
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
