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
        ReplaceList(cn, tx, "md_status", set.Status);
        ReplaceList(cn, tx, "md_unit_status", set.UnitStatus);
        ReplaceList(cn, tx, "md_equipment", set.Equipment);
        ReplaceList(cn, tx, "md_districts", set.Districts);
        ReplaceList(cn, tx, "md_call_signs", set.RadioCallSigns);
        ReplaceList(cn, tx, "md_brigades", set.Brigades);
        ReplaceList(cn, tx, "md_trupp_types", set.TruppTypes);
        ReplaceList(cn, tx, "md_einsatzarten", set.Einsatzarten);

        Run(cn, tx, "DELETE FROM md_streets;", _ => { });
        foreach (var s in set.Streets)
            Run(cn, tx, "INSERT INTO md_streets (name, district) VALUES ($n,$d);",
                p => { p("$n", s.Name); p("$d", s.District); });

        Run(cn, tx, "DELETE FROM md_links;", _ => { });
        foreach (var l in set.Links)
            Run(cn, tx, "INSERT INTO md_links (name, url) VALUES ($n,$u);",
                p => { p("$n", l.Name); p("$u", l.Url); });

        Run(cn, tx, "DELETE FROM md_vehicles;", _ => { });
        foreach (var v in set.Vehicles)
            Run(cn, tx, "INSERT INTO md_vehicles (wache, call_sign, seats) VALUES ($w,$c,$s);",
                p => { p("$w", v.Wache); p("$c", v.CallSign); p("$s", v.Seats); });

        Run(cn, tx, "DELETE FROM md_checklist_template;", _ => { });
        InsertChecklistTemplate(cn, tx, set.ChecklistTemplateAufbau, kind: 0, ordinalOffset: 0);
        InsertChecklistTemplate(cn, tx, set.ChecklistTemplateAbbau, kind: 1, ordinalOffset: set.ChecklistTemplateAufbau.Count);

        Run(cn, tx, "DELETE FROM md_personnel;", _ => { });
        foreach (var person in set.Personnel)
            Run(cn, tx, "INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone) VALUES ($l,$f,$r,$c,$p);",
                p =>
                {
                    p("$l", person.LastName); p("$f", person.FirstName);
                    p("$r", (object?)person.Role ?? DBNull.Value);
                    p("$c", (object?)person.CallSign ?? DBNull.Value);
                    p("$p", (object?)person.Phone ?? DBNull.Value);
                });

        // Settings are a single row per key; UPSERT rather than delete-and-reinsert so a key the
        // store already carries keeps its identity, and so writing a subset never drops the rest.
        foreach (var (key, value) in SettingsRows(set.Settings))
            Run(cn, tx,
                "INSERT INTO md_settings (key, value) VALUES ($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
                p => { p("$k", key); p("$v", value); });

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
            Run(cn, tx, "INSERT INTO md_checklist_template (ordinal, text, is_mandatory, kind) VALUES ($o,$t,$m,$k);",
                p => { p("$o", ordinal); p("$t", item.Text); p("$m", item.IsMandatory ? 1 : 0); p("$k", kind); });
        }
    }

    private static (string Key, int Value)[] SettingsRows(IncidentSettings s) => new[]
    {
        ("ils_reminder_interval_minutes", s.IlsReminderIntervalMinutes),
        ("ils_reminder_follow_up_interval_minutes", s.IlsReminderFollowUpIntervalMinutes),
        ("agt_max_duration_minutes", s.AgtMaxDurationMinutes),
        ("csa_max_duration_minutes", s.CsaMaxDurationMinutes),
        ("lpa_max_duration_minutes", s.LpaMaxDurationMinutes),
        ("pressure_control_interval_minutes", s.PressureControlIntervalMinutes),
        ("return_pressure_bar", s.ReturnPressureBar),
    };

    private static void ReplaceList(SqliteConnection cn, SqliteTransaction tx, string table, IReadOnlyList<string> values)
    {
        Run(cn, tx, $"DELETE FROM {table};", _ => { });
        InsertList(cn, tx, table, values);
    }

    private static void EnsureSchema(SqliteConnection cn)
    {
        Exec(cn, """
            CREATE TABLE IF NOT EXISTS md_roles (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_status (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_equipment (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_districts (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_call_signs (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_brigades (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_unit_status (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_streets (name TEXT NOT NULL, district TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_links (name TEXT NOT NULL, url TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_vehicles (wache TEXT NOT NULL, call_sign TEXT NOT NULL, seats INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS md_checklist_template (
                ordinal INTEGER PRIMARY KEY,
                text TEXT NOT NULL,
                is_mandatory INTEGER NOT NULL DEFAULT 0,
                kind INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS md_trupp_types (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_einsatzarten (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_personnel (
                last_name TEXT NOT NULL,
                first_name TEXT NOT NULL,
                role TEXT,
                call_sign TEXT,
                phone TEXT
            );
            CREATE TABLE IF NOT EXISTS md_settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
            """);

        // Widen a pre-existing md_checklist_template that predates the Aufbau/Abbau split — this
        // store has no version marker, so every open re-checks rather than gating on one.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_template", "is_mandatory", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_template", "kind", "INTEGER NOT NULL DEFAULT 0");
    }

    private static MasterDataSet Read(SqliteConnection cn)
    {
        var (checklistAufbau, checklistAbbau) = ReadChecklistTemplate(cn);
        return new(
            ReadColumn(cn, "SELECT value FROM md_roles;"),
            ReadColumn(cn, "SELECT value FROM md_status;"),
            ReadColumn(cn, "SELECT value FROM md_equipment;"),
            ReadColumn(cn, "SELECT value FROM md_districts;"),
            ReadColumn(cn, "SELECT value FROM md_call_signs;"),
            ReadColumn(cn, "SELECT value FROM md_brigades;"),
            ReadColumn(cn, "SELECT value FROM md_unit_status;"),
            ReadStreets(cn),
            ReadLinks(cn),
            checklistAufbau,
            checklistAbbau,
            ReadColumn(cn, "SELECT value FROM md_trupp_types;"),
            ReadPersonnel(cn),
            ReadColumn(cn, "SELECT value FROM md_einsatzarten;"),
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

    private static IncidentSettings ReadSettings(SqliteConnection cn)
    {
        var stored = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM md_settings;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) stored[r.GetString(0)] = r.GetInt32(1);
        }

        // Per-key fallback to the defaults: a store written before a given setting existed simply
        // has no row for it, and reads the compiled-in default rather than a zero.
        var d = IncidentSettings.Defaults;
        int Get(string key, int fallback) => stored.TryGetValue(key, out var v) ? v : fallback;
        return new IncidentSettings(
            Get("ils_reminder_interval_minutes", d.IlsReminderIntervalMinutes),
            Get("ils_reminder_follow_up_interval_minutes", d.IlsReminderFollowUpIntervalMinutes),
            Get("agt_max_duration_minutes", d.AgtMaxDurationMinutes),
            Get("csa_max_duration_minutes", d.CsaMaxDurationMinutes),
            Get("lpa_max_duration_minutes", d.LpaMaxDurationMinutes),
            Get("pressure_control_interval_minutes", d.PressureControlIntervalMinutes),
            Get("return_pressure_bar", d.ReturnPressureBar));
    }

    private static void InsertList(SqliteConnection cn, SqliteTransaction tx, string table, IReadOnlyList<string> values)
    {
        foreach (var v in values)
            Run(cn, tx, $"INSERT INTO {table} (value) VALUES ($v);", p => p("$v", v));
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Audited: SQL built from compile-time schema constants; values use bound parameters.")]
    private static List<string> ReadColumn(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<Street> ReadStreets(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name, district FROM md_streets;";
        using var r = cmd.ExecuteReader();
        var list = new List<Street>();
        while (r.Read()) list.Add(new Street(r.GetString(0), r.GetString(1)));
        return list;
    }

    private static List<Link> ReadLinks(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name, url FROM md_links;";
        using var r = cmd.ExecuteReader();
        var list = new List<Link>();
        while (r.Read()) list.Add(new Link(r.GetString(0), r.GetString(1)));
        return list;
    }

    private static List<Vehicle> ReadVehicles(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT wache, call_sign, seats FROM md_vehicles;";
        using var r = cmd.ExecuteReader();
        var list = new List<Vehicle>();
        while (r.Read()) list.Add(new Vehicle(r.GetString(0), r.GetString(1), r.GetInt32(2)));
        return list;
    }

    private static List<Person> ReadPersonnel(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT last_name, first_name, role, call_sign, phone FROM md_personnel ORDER BY last_name, first_name;";
        using var r = cmd.ExecuteReader();
        var list = new List<Person>();
        while (r.Read())
            list.Add(new Person(r.GetString(0), r.GetString(1), Str(r, 2), Str(r, 3), Str(r, 4)));
        return list;

        static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Audited: SQL built from compile-time schema constants; values use bound parameters.")]
    private static void Run(SqliteConnection cn, SqliteTransaction tx, string sql, Action<Action<string, object>> bind)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind((name, value) => cmd.Parameters.AddWithValue(name, value));
        cmd.ExecuteNonQuery();
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Audited: SQL built from compile-time schema constants; values use bound parameters.")]
    private static void Exec(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
