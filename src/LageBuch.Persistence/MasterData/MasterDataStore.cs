using System.Diagnostics.CodeAnalysis;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.MasterData;

public sealed class MasterDataStore
{
    /// <summary>
    /// Opens (creating and schema-ensuring on first use) the local master-data database and returns
    /// its contents. Nothing is seeded: the app ships with no master data, so a fresh database comes
    /// back empty and is populated only by <see cref="Save"/> — i.e. the editor's Import.
    /// </summary>
    /// <summary>
    /// Marks a store as having had the one-time #398 Trupp-Typ translation applied. Kept in
    /// md_settings, whose rows are arbitrary key/value pairs, rather than as a schema version --
    /// this store deliberately has no version marker, and one flag is not a reason to grow one.
    /// It is not part of <see cref="IncidentSettings"/> and never reaches the editor.
    /// </summary>
    private const string TruppTypeDefaultsMigratedKey = "trupp_type_defaults_migrated";

    public static MasterDataSet GetOrCreate(string path)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        EnsureSchema(cn);
        return Read(cn);
    }

    /// <summary>
    /// Replaces the master data with <paramref name="set"/>, in the given order. A full transactional
    /// replace, so deletes and reorders take effect exactly as supplied.
    /// </summary>
    public static void Save(string path, MasterDataSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        EnsureSchema(cn);
        using var tx = cn.BeginTransaction();

        ReplaceList(cn, tx, "md_roles", set.Roles);
        ReplaceList(cn, tx, "md_unit_status", set.UnitStatus);

        Run(cn, tx, "DELETE FROM md_links;", _ => { });
        foreach (var l in set.Links)
        {
            Run(
                cn,
                tx,
                "INSERT INTO md_links (name, url) VALUES ($n,$u);",
                p =>
                {
                    p("$n", l.Name);
                    p("$u", l.Url);
                });
        }

        Run(cn, tx, "DELETE FROM md_trupp_types;", _ => { });
        foreach (var t in set.TruppTypes)
        {
            Run(
                cn,
                tx,
                "INSERT INTO md_trupp_types (value, member_count, max_duration_minutes) VALUES ($v,$m,$d);",
                p =>
                {
                    p("$v", t.Name);
                    p("$m", t.MemberCount);
                    p("$d", t.MaxDurationMinutes);
                });
        }

        Run(cn, tx, "DELETE FROM md_vehicles;", _ => { });
        foreach (var v in set.Vehicles)
        {
            Run(
                cn,
                tx,
                "INSERT INTO md_vehicles (wache, call_sign, seats, has_zugfuehrer) VALUES ($w,$c,$s,$z);",
                p =>
                {
                    p("$w", v.Wache);
                    p("$c", v.CallSign);
                    p("$s", v.Seats);
                    p("$z", v.HasZugfuehrer ? 1 : 0);
                });
        }

        Run(cn, tx, "DELETE FROM md_checklist_template;", _ => { });
        InsertChecklistTemplate(cn, tx, set.ChecklistTemplateAufbau, kind: 0, ordinalOffset: 0);
        InsertChecklistTemplate(cn, tx, set.ChecklistTemplateAbbau, kind: 1, ordinalOffset: set.ChecklistTemplateAufbau.Count);

        Run(cn, tx, "DELETE FROM md_personnel;", _ => { });
        foreach (var person in set.Personnel)
        {
            Run(
                cn,
                tx,
                "INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone) VALUES ($l,$f,$r,$c,$p);",
                p =>
                {
                    p("$l", person.LastName);
                    p("$f", person.FirstName);
                    p("$r", (object?)person.Role ?? DBNull.Value);
                    p("$c", (object?)person.CallSign ?? DBNull.Value);
                    p("$p", (object?)person.Phone ?? DBNull.Value);
                });
        }

        // Settings are a single row per key; UPSERT rather than delete-and-reinsert so a key the
        // store already carries keeps its identity, and so writing a subset never drops the rest.
        foreach (var (key, value) in SettingsRows(set.Settings))
            Run(
                cn,
                tx,
                "INSERT INTO md_settings (key, value) VALUES ($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
                p =>
                {
                    p("$k", key);
                    p("$v", value);
                });

        tx.Commit();
    }

    // Ordinal is the table's PRIMARY KEY, so Aufbau and Abbau rows share one running sequence
    // (Abbau continuing where Aufbau left off) rather than each restarting at 0.
    private static void InsertChecklistTemplate(
        SqliteConnection cn, SqliteTransaction tx, IReadOnlyList<ChecklistTemplateItem> items, int kind, int ordinalOffset)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var ordinal = ordinalOffset + i;
            Run(
                cn,
                tx,
                "INSERT INTO md_checklist_template (ordinal, text, is_mandatory, kind) VALUES ($o,$t,$m,$k);",
                p =>
                {
                    p("$o", ordinal);
                    p("$t", item.Text);
                    p("$m", item.IsMandatory ? 1 : 0);
                    p("$k", kind);
                });
        }
    }

    private static (string Key, int Value)[] SettingsRows(IncidentSettings s) => new[]
    {
        ("ils_reminder_interval_minutes", s.IlsReminderIntervalMinutes),
        ("ils_reminder_follow_up_interval_minutes", s.IlsReminderFollowUpIntervalMinutes),
        ("return_pressure_bar", s.ReturnPressureBar),
    };

    private static void ReplaceList(SqliteConnection cn, SqliteTransaction tx, string table, IReadOnlyList<string> values)
    {
        Run(cn, tx, $"DELETE FROM {table};", _ => { });
        InsertList(cn, tx, table, values);
    }

    private static void EnsureSchema(SqliteConnection cn)
    {
        const string schema = """
            CREATE TABLE IF NOT EXISTS md_roles (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_unit_status (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_links (name TEXT NOT NULL, url TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_vehicles (wache TEXT NOT NULL, call_sign TEXT NOT NULL, seats INTEGER NOT NULL DEFAULT 0, has_zugfuehrer INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS md_checklist_template (
                ordinal INTEGER PRIMARY KEY,
                text TEXT NOT NULL,
                is_mandatory INTEGER NOT NULL DEFAULT 0,
                kind INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS md_trupp_types (
                value TEXT NOT NULL,
                member_count INTEGER NOT NULL DEFAULT 2,
                max_duration_minutes INTEGER NOT NULL DEFAULT 30
            );
            CREATE TABLE IF NOT EXISTS md_personnel (
                last_name TEXT NOT NULL,
                first_name TEXT NOT NULL,
                role TEXT,
                call_sign TEXT,
                phone TEXT
            );
            CREATE TABLE IF NOT EXISTS md_settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
            """;
        Exec(cn, schema);

        // Widen a pre-existing md_checklist_template that predates the Aufbau/Abbau split — this
        // store has no version marker, so every open re-checks rather than gating on one.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_template", "is_mandatory", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_template", "kind", "INTEGER NOT NULL DEFAULT 0");

        // Widen a pre-existing md_vehicles that predates the ZF flag -- existing vehicles read as
        // "no Zugführer" rather than failing to load.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_vehicles", "seats", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_vehicles", "has_zugfuehrer", "INTEGER NOT NULL DEFAULT 0");

        // md_personnel's optional columns. No shipped version ever lacked them, so these repair
        // nothing today -- they are here because #397's sweep below holds every repairable column
        // to the same rule, and a line that is missing is only ever noticed by a user whose store
        // already broke. Cheap and idempotent; the alternative is finding out in the field.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "role", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "call_sign", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "phone", "TEXT");

        // Widen a pre-existing md_trupp_types that predates #398, when a Trupp-Typ was a bare name
        // and the crew size and Einsatzzeit were decided by comparing that name against a literal.
        // The constant DEFAULTs backfill every existing row as an ordinary two-person Trupp on the
        // standard Einsatzzeit; MigrateTruppTypeDefaults then restores what the old rule gave the
        // two named types. The literals here mirror AtemschutzTrupp.StandardMemberCount and
        // DefaultMaxDurationMinutes, which a const string cannot interpolate -- they are only a
        // backstop in any case, since Save always writes all three columns explicitly.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_trupp_types", "member_count", "INTEGER NOT NULL DEFAULT 2");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_trupp_types", "max_duration_minutes", "INTEGER NOT NULL DEFAULT 30");
        MigrateTruppTypeDefaults(cn);

        // Wachen and Funkrufnamen used to be lists of their own; they are derived from md_vehicles
        // (and md_personnel) now. Drop the stale tables a pre-change store still carries -- their
        // rows were never more than the names already on the vehicles, and nothing reads them.
        Exec(cn, "DROP TABLE IF EXISTS md_call_signs; DROP TABLE IF EXISTS md_brigades;");

        // Retired by #398: the Einsatzzeiten are per-Trupp-Typ now. Deleted rather than left to rot,
        // like the stale tables above, so nobody opening this file in a SQLite browser mistakes them
        // for live configuration. Runs after MigrateTruppTypeDefaults, which is allowed to read them.
        const string dropRetiredSettings =
            """
            DELETE FROM md_settings WHERE key IN
                ('agt_max_duration_minutes', 'csa_max_duration_minutes', 'lpa_max_duration_minutes');
            """;
        Exec(cn, dropRetiredSettings);
    }

    /// <summary>
    /// Restores, exactly once per store, the crew size and Einsatzzeit that the two named Trupp-Typen
    /// had before #398 moved those numbers onto the Stammdaten row. Everything else keeps the
    /// two-person/30-minute backfill the ALTER gave it.
    /// <para>
    /// Gated on a marker row rather than on the column being absent: <see cref="EnsureSchema"/> runs
    /// on every open, and a store whose column was dropped and restored -- which is exactly what the
    /// schema-drift sweep in the tests does -- would otherwise be mistaken for a first migration and
    /// have the brigade's own edits overwritten. Writing the marker on a fresh store too means a new
    /// install can never be seeded later either.
    /// </para>
    /// </summary>
    private static void MigrateTruppTypeDefaults(SqliteConnection cn)
    {
        if (ReadSetting(cn, TruppTypeDefaultsMigratedKey) is not null)
        {
            return;
        }

        // The only comparison of a Trupp-Typ name against a literal left in the app, and it runs
        // once. TRIM + NOCASE matches what the old AtemschutzTrupp.IsChemicalTrupp accepted, so a
        // store migrates with the rule it actually had -- no more and no less.
        foreach (var (name, defaults) in LegacyTruppTypeDefaults.ByName)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText =
                """
                UPDATE md_trupp_types
                   SET member_count = $m, max_duration_minutes = $d
                 WHERE TRIM(value) = $n COLLATE NOCASE;
                """;
            cmd.Parameters.AddWithValue("$m", defaults.MemberCount);
            cmd.Parameters.AddWithValue("$d", defaults.MaxDurationMinutes);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        WriteSetting(cn, TruppTypeDefaultsMigratedKey, 1);
    }

    private static MasterDataSet Read(SqliteConnection cn)
    {
        var (checklistAufbau, checklistAbbau) = ReadChecklistTemplate(cn);
        return new(
            ReadColumn(cn, "SELECT value FROM md_roles;"),
            ReadColumn(cn, "SELECT value FROM md_unit_status;"),
            ReadLinks(cn),
            checklistAufbau,
            checklistAbbau,
            ReadTruppTypes(cn),
            ReadPersonnel(cn),
            ReadVehicles(cn),
            ReadSettings(cn));
    }

    // Rows are ordered globally by ordinal (Aufbau's block precedes Abbau's — see
    // InsertChecklistTemplate), so filtering by kind here preserves each list's own order.
    private static (IReadOnlyList<ChecklistTemplateItem> Aufbau, IReadOnlyList<ChecklistTemplateItem> Abbau)
        ReadChecklistTemplate(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT text, is_mandatory, kind FROM md_checklist_template ORDER BY ordinal;";
        using var r = cmd.ExecuteReader();
        var aufbau = new List<ChecklistTemplateItem>();
        var abbau = new List<ChecklistTemplateItem>();
        while (r.Read())
        {
            var item = new ChecklistTemplateItem(r.GetString(0), r.GetInt32(1) != 0);
            (r.GetInt32(2) == 0 ? aufbau : abbau).Add(item);
        }

        return (aufbau, abbau);
    }

    /// <summary>One raw md_settings value, or null when the key has no row. For the internal
    /// markers that are not part of <see cref="IncidentSettings"/>.</summary>
    private static int? ReadSetting(SqliteConnection cn, string key)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT value FROM md_settings WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() is long v ? (int)v : null;
    }

    private static void WriteSetting(SqliteConnection cn, string key, int value)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO md_settings (key, value) VALUES ($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static IncidentSettings ReadSettings(SqliteConnection cn)
    {
        var stored = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM md_settings;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                stored[r.GetString(0)] = r.GetInt32(1);
            }
        }

        // Per-key fallback to the defaults: a store written before a given setting existed simply
        // has no row for it, and reads the compiled-in default rather than a zero.
        var d = IncidentSettings.Defaults;
        int Get(string key, int fallback) => stored.TryGetValue(key, out var v) ? v : fallback;
        return new IncidentSettings(
            Get("ils_reminder_interval_minutes", d.IlsReminderIntervalMinutes),
            Get("ils_reminder_follow_up_interval_minutes", d.IlsReminderFollowUpIntervalMinutes),
            Get("return_pressure_bar", d.ReturnPressureBar));
    }

    private static void InsertList(SqliteConnection cn, SqliteTransaction tx, string table, IReadOnlyList<string> values)
    {
        foreach (var v in values)
        {
            Run(cn, tx, $"INSERT INTO {table} (value) VALUES ($v);", p => p("$v", v));
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Audited: SQL built from compile-time schema constants; values use bound parameters.")]
    private static List<string> ReadColumn(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read())
        {
            list.Add(r.GetString(0));
        }

        return list;
    }

    private static List<Link> ReadLinks(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name, url FROM md_links;";
        using var r = cmd.ExecuteReader();
        var list = new List<Link>();
        while (r.Read())
        {
            list.Add(new Link(r.GetString(0), r.GetString(1)));
        }

        return list;
    }

    private static List<TruppType> ReadTruppTypes(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT value, member_count, max_duration_minutes FROM md_trupp_types;";
        using var r = cmd.ExecuteReader();
        var list = new List<TruppType>();
        while (r.Read())
        {
            // Clamped on the way out as well as on the way in: the columns are only defaulted, not
            // constrained, so a file edited by hand outside the app cannot put an unreachable crew
            // position on the Atemschutz form, nor an Einsatzzeit no countdown can run.
            list.Add(new TruppType(
                r.GetString(0),
                TruppType.ClampMemberCount(r.GetInt32(1)),
                TruppType.ClampMaxDurationMinutes(r.GetInt32(2))));
        }

        return list;
    }

    private static List<Vehicle> ReadVehicles(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT wache, call_sign, seats, has_zugfuehrer FROM md_vehicles;";
        using var r = cmd.ExecuteReader();
        var list = new List<Vehicle>();
        while (r.Read())
        {
            list.Add(new Vehicle(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3) != 0));
        }

        return list;
    }

    private static List<Person> ReadPersonnel(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT last_name, first_name, role, call_sign, phone FROM md_personnel ORDER BY last_name, first_name;";
        using var r = cmd.ExecuteReader();
        var list = new List<Person>();
        while (r.Read())
        {
            list.Add(new Person(r.GetString(0), r.GetString(1), Str(r, 2), Str(r, 3), Str(r, 4)));
        }

        return list;

        static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    }

    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Audited: SQL built from compile-time schema constants; values use bound parameters.")]
    private static void Run(SqliteConnection cn, SqliteTransaction tx, string sql, Action<Action<string, object>> bind)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind((name, value) => cmd.Parameters.AddWithValue(name, value));
        cmd.ExecuteNonQuery();
    }

    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Audited: SQL built from compile-time schema constants; values use bound parameters.")]
    private static void Exec(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
