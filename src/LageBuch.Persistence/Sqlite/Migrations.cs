using LageBuch.Domain.Atemschutz;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Sqlite;

public static class Migrations
{
    public const int CurrentVersion = 13;

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
        if (version < 6)
        {
            ApplyV6(cn, tx);
        }
        if (version < 7)
        {
            ApplyV7(cn, tx);
        }
        if (version < 8)
        {
            ApplyV8(cn, tx);
        }
        if (version < 9)
        {
            ApplyV9(cn, tx);
        }
        if (version < 10)
        {
            ApplyV10(cn, tx);
        }
        if (version < 11)
        {
            ApplyV11(cn, tx);
        }
        if (version < 12)
        {
            ApplyV12(cn, tx);
        }
        if (version < 13)
        {
            ApplyV13(cn, tx);
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
        SchemaHelpers.AddColumnIfMissing(cn, tx, "role_assignments", "section", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, tx, "role_assignments", "phone", "TEXT");
    }

    private static void ApplyV5(SqliteConnection cn, SqliteTransaction tx)
    {
        // Kräfteübersicht counts Atemschutzgeräteträger alongside the crew total. NOT NULL with a
        // default of 0 is safe here: existing rows genuinely have no recorded AGT count, and 0
        // reads the same as "none recorded" for the sum that feeds the header tile.
        SchemaHelpers.AddColumnIfMissing(cn, tx, "force_units", "scba_count", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void ApplyV6(SqliteConnection cn, SqliteTransaction tx)
    {
        // A Trupp's crew stops being one free-text string and becomes addressable rows, mirroring
        // how scba_pressure_readings already hangs off a Trupp.
        Exec(cn, tx, """
            CREATE TABLE IF NOT EXISTS scba_trupp_members (
                trupp_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                role INTEGER NOT NULL,
                name TEXT NOT NULL
            );
            """);

        if (!SchemaHelpers.TableExists(cn, tx, "scba_trupps") || !SchemaHelpers.ColumnExists(cn, tx, "scba_trupps", "members"))
            return;

        // Split the old "Müller / Schmidt" convention into rows. The separator was only ever a
        // watermark hint, so anything that does not split cleanly is kept whole as the Truppführer
        // rather than discarded -- an imperfect record beats a lost one, and Rehydrate does not
        // re-validate crew size precisely so that these rows stay loadable.
        var legacy = new List<(string Id, string Members)>();
        using (var read = cn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT id, members FROM scba_trupps;";
            using var r = read.ExecuteReader();
            while (r.Read())
                legacy.Add((r.GetString(0), r.IsDBNull(1) ? string.Empty : r.GetString(1)));
        }

        foreach (var (id, members) in legacy)
        {
            var names = members
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (names.Count == 0)
                names.Add(string.IsNullOrWhiteSpace(members) ? "Unbekannt" : members.Trim());

            for (var i = 0; i < names.Count; i++)
            {
                using var insert = cn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText =
                    "INSERT INTO scba_trupp_members (trupp_id, ordinal, role, name) VALUES ($t,$o,$r,$n);";
                insert.Parameters.AddWithValue("$t", id);
                insert.Parameters.AddWithValue("$o", i);
                insert.Parameters.AddWithValue("$r", i);
                insert.Parameters.AddWithValue("$n", names[i]);
                insert.ExecuteNonQuery();
            }
        }

        // Drop the members column by rebuilding, exactly as V3 did: portable across SQLite
        // versions, and the explicit DDL keeps the resulting shape visible in the diff.
        Exec(cn, tx, """
            CREATE TABLE scba_trupps_v6 (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                designation TEXT NOT NULL,
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
        Exec(cn, tx, """
            INSERT INTO scba_trupps_v6
                (id, ordinal, designation, call_sign, task, registered_at, start_time,
                 start_pressure, max_duration_minutes, return_pressure_bar,
                 pressure_control_interval_minutes, exit_time)
            SELECT id, ordinal, designation, call_sign, task, registered_at, start_time,
                   start_pressure, max_duration_minutes, return_pressure_bar,
                   pressure_control_interval_minutes, exit_time
            FROM scba_trupps;
            """);
        Exec(cn, tx, "DROP TABLE scba_trupps;");
        Exec(cn, tx, "ALTER TABLE scba_trupps_v6 RENAME TO scba_trupps;");
    }

    // Deliberately a no-op: no table changed shape. The bump to 7 exists solely so that a file
    // written by this build -- which may store the new EtbDirection.System (ordinal 3) in
    // etb_entries.direction -- is refused by an older build via the version > CurrentVersion guard
    // in Migrate, instead of silently mis-rendering the unknown ordinal as "3".
    private static void ApplyV7(SqliteConnection cn, SqliteTransaction tx)
    {
    }

    // Adds the generic incident-level timer store. Old files gain the empty table on their next
    // open (Load migrates before reading), so nothing breaks. Keyed by the timer's identity; only
    // the anchor + cadence are stored, live values are recomputed from now (like the SCBA countdowns).
    private static void ApplyV8(SqliteConnection cn, SqliteTransaction tx)
    {
        Exec(cn, tx, """
            CREATE TABLE IF NOT EXISTS incident_timers (
                key TEXT PRIMARY KEY,
                cycle_anchor TEXT NOT NULL,
                interval_minutes INTEGER NOT NULL,
                recurring_interval_minutes INTEGER NOT NULL,
                is_running INTEGER NOT NULL
            );
            """);
    }

    // Checkliste splits into two independent lists (Aufbau/Abbau) with a mandatory flag per item.
    // Existing rows get kind 0 (Aufbau) and is_mandatory 0 (optional) by default — consistent with
    // the Stammdaten template's own back-compat mapping (MasterDataJson.ParseChecklistTemplate).
    private static void ApplyV9(SqliteConnection cn, SqliteTransaction tx)
    {
        SchemaHelpers.AddColumnIfMissing(cn, tx, "checklist_items", "is_mandatory", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, tx, "checklist_items", "kind", "INTEGER NOT NULL DEFAULT 0");
    }

    // Adds the metadata store for attached files (#62). Only metadata lives here — the actual
    // bytes sit in a sibling ".files" folder next to the .fwincident file, kept out of this
    // full-rewrite-per-save table set so an unrelated edit never rewrites attachment content.
    private static void ApplyV10(SqliteConnection cn, SqliteTransaction tx)
    {
        Exec(cn, tx, """
            CREATE TABLE IF NOT EXISTS incident_files (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                file_name TEXT NOT NULL,
                content_type TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                added_at TEXT NOT NULL,
                added_by TEXT NOT NULL
            );
            """);
    }

    // A file's display label, editable independently of its original file_name (#62 follow-up).
    // Nullable: existing rows predate the column, and Load falls back to file_name for those.
    private static void ApplyV11(SqliteConnection cn, SqliteTransaction tx) =>
        SchemaHelpers.AddColumnIfMissing(cn, tx, "incident_files", "display_name", "TEXT");

    // Preserves every prior version of a manually-edited ETB entry's text, plus who edited it and
    // when -- the ETB itself only ever shows the current text plus a Verlauf affordance; the full
    // history lives in this table (#73).
    private static void ApplyV12(SqliteConnection cn, SqliteTransaction tx)
    {
        Exec(cn, tx, """
            CREATE TABLE IF NOT EXISTS etb_entry_edits (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                previous_text TEXT NOT NULL,
                edited_by TEXT NOT NULL,
                edited_at TEXT NOT NULL
            );
            """);
    }

    // Kräfte gain a Führungskräfte counter and corrigible Stärke (#76). officer_count is NOT NULL
    // with default 0: rows written before the split genuinely have no recorded GF count, and 0/x/x
    // keeps their Gesamtstärke exact. force_unit_edits retains every prior Stärke the same way
    // etb_entry_edits (V12) retains prior ETB wording.
    private static void ApplyV13(SqliteConnection cn, SqliteTransaction tx)
    {
        SchemaHelpers.AddColumnIfMissing(cn, tx, "force_units", "officer_count", "INTEGER NOT NULL DEFAULT 0");
        Exec(cn, tx, """
            CREATE TABLE IF NOT EXISTS force_unit_edits (
                id TEXT PRIMARY KEY,
                unit_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                previous_officer_count INTEGER NOT NULL,
                previous_personnel_count INTEGER NOT NULL,
                previous_scba_count INTEGER NOT NULL,
                edited_by TEXT NOT NULL,
                edited_at TEXT NOT NULL
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
