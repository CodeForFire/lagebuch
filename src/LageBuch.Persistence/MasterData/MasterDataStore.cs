using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.Atemschutz;
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

        WriteChecklists(cn, tx, set.ChecklistTemplates);
        WriteNavigation(cn, tx, set.Navigation);

        Run(cn, tx, "DELETE FROM md_personnel;", _ => { });
        foreach (var person in set.Personnel)
        {
            Run(
                cn,
                tx,
                "INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone, email, note) VALUES ($l,$f,$r,$c,$p,$e,$n);",
                p =>
                {
                    p("$l", person.LastName);
                    p("$f", person.FirstName);
                    p("$r", (object?)person.Role ?? DBNull.Value);
                    p("$c", (object?)person.CallSign ?? DBNull.Value);
                    p("$p", (object?)person.Phone ?? DBNull.Value);
                    p("$e", (object?)person.Email ?? DBNull.Value);
                    p("$n", (object?)person.Note ?? DBNull.Value);
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

    // Full replace, like every other category here. Per-list ordinals restart at 0, which is what
    // the composite (list_id, ordinal) key exists for.
    private static void WriteChecklists(
        SqliteConnection cn, SqliteTransaction tx, IReadOnlyList<ChecklistTemplate> templates)
    {
        Run(cn, tx, "DELETE FROM md_checklist_items;", _ => { });
        Run(cn, tx, "DELETE FROM md_checklist_lists;", _ => { });

        for (var t = 0; t < templates.Count; t++)
        {
            var template = templates[t];
            var ordinal = t;
            Run(
                cn,
                tx,
                "INSERT INTO md_checklist_lists (id, ordinal, title) VALUES ($id,$o,$t);",
                p =>
                {
                    p("$id", template.Id.ToString());
                    p("$o", ordinal);
                    p("$t", template.Title);
                });

            for (var i = 0; i < template.Items.Count; i++)
            {
                var item = template.Items[i];
                var itemOrdinal = i;
                Run(
                    cn,
                    tx,
                    "INSERT INTO md_checklist_items (list_id, ordinal, text, is_mandatory) VALUES ($l,$o,$t,$m);",
                    p =>
                    {
                        p("$l", template.Id.ToString());
                        p("$o", itemOrdinal);
                        p("$t", item.Text);
                        p("$m", item.IsMandatory ? 1 : 0);
                    });
            }
        }
    }

    private static void WriteNavigation(
        SqliteConnection cn, SqliteTransaction tx, IReadOnlyList<NavEntry> navigation)
    {
        Run(cn, tx, "DELETE FROM md_navigation;", _ => { });
        for (var i = 0; i < navigation.Count; i++)
        {
            var entry = navigation[i];
            var ordinal = i;
            Run(
                cn,
                tx,
                "INSERT INTO md_navigation (ordinal, module, checklist_id, is_visible) VALUES ($o,$m,$c,$v);",
                p =>
                {
                    p("$o", ordinal);
                    p("$m", entry.ModuleKey);
                    p("$c", (object?)entry.ChecklistId?.ToString() ?? DBNull.Value);
                    p("$v", entry.IsVisible ? 1 : 0);
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
            CREATE TABLE IF NOT EXISTS md_checklist_lists (
                id TEXT PRIMARY KEY,
                ordinal INTEGER NOT NULL,
                title TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS md_checklist_items (
                list_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                text TEXT NOT NULL,
                is_mandatory INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (list_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS md_navigation (
                ordinal INTEGER PRIMARY KEY,
                module TEXT NOT NULL,
                checklist_id TEXT,
                is_visible INTEGER NOT NULL DEFAULT 1
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
                phone TEXT,
                email TEXT,
                note TEXT
            );
            CREATE TABLE IF NOT EXISTS md_settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
            """;
        Exec(cn, schema);

        MoveChecklistTemplateToLists(cn);

        // The optional columns of the Checklisten and Navigation tables. Like md_personnel's below,
        // no shipped version ever lacked them -- both tables arrived complete -- but #397's sweep
        // holds every repairable column to the same rule, and a store that lost one would otherwise
        // fail to open with "no such column" instead of repairing itself.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_items", "is_mandatory", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_navigation", "checklist_id", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_navigation", "is_visible", "INTEGER NOT NULL DEFAULT 1");

        // Widen a pre-existing md_vehicles that predates the ZF flag -- existing vehicles read as
        // "no Zugführer" rather than failing to load.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_vehicles", "seats", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_vehicles", "has_zugfuehrer", "INTEGER NOT NULL DEFAULT 0");

        // md_personnel's optional columns. role/call_sign/phone repair nothing today -- no shipped
        // version ever lacked them -- and are listed because #397's sweep below holds every
        // repairable column to the same rule, and a line that is missing is only ever noticed by a
        // user whose store already broke. email/note are the opposite case: every store written by
        // a released build really is missing them, so without these two lines opening one fails
        // with "no such column: email" instead of reading the roster it already holds.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "role", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "call_sign", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "phone", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "email", "TEXT");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_personnel", "note", "TEXT");

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
        // for live configuration. Order matters: MigrateTruppTypeDefaults above reads these three to
        // carry the brigade's own Einsatzzeiten onto the rows, so deleting them first would lose
        // exactly the values the migration exists to preserve.
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
    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Audited: both CommandText branches are compile-time literals; the Trupp-Typ name and the two numbers are bound parameters.")]
    private static void MigrateTruppTypeDefaults(SqliteConnection cn)
    {
        if (ReadSetting(cn, TruppTypeDefaultsMigratedKey) is not null)
        {
            return;
        }

        // Read before the retired rows are deleted further up the call: these are the brigade's own
        // Einsatzzeiten, edited in Stammdaten -> Einstellungen, not the shipped defaults. Assuming
        // the defaults here would hand a Wehr that had set its CSA-Trupp to 15 minutes a 20-minute
        // countdown instead -- longer under air than it decided on, silently, on first launch.
        var legacy = ReadLegacyEinsatzzeiten(cn);

        // Every row first, because the ALTER's constant DEFAULT cannot know about this store's AGT
        // setting; then the two named ones, which the old rule treated differently. This is the only
        // comparison of a Trupp-Typ name against a literal left in the app, and it runs once.
        // TRIM + NOCASE matches what AtemschutzTrupp.IsChemicalTrupp accepted, so a store migrates
        // with the rule it actually ran -- no more and no less.
        Update(null, AtemschutzTrupp.StandardMemberCount, legacy.Agt);
        Update(LegacyTruppTypeDefaults.ChemicalName, AtemschutzTrupp.MaxMemberCount, legacy.Chemical);
        Update(LegacyTruppTypeDefaults.LpaName, AtemschutzTrupp.StandardMemberCount, legacy.Lpa);

        WriteSetting(cn, TruppTypeDefaultsMigratedKey, 1);

        void Update(string? name, int memberCount, int minutes)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = name is null
                ? "UPDATE md_trupp_types SET member_count = $m, max_duration_minutes = $d;"
                : """
                  UPDATE md_trupp_types
                     SET member_count = $m, max_duration_minutes = $d
                   WHERE TRIM(value) = $n COLLATE NOCASE;
                  """;
            cmd.Parameters.AddWithValue("$m", memberCount);
            cmd.Parameters.AddWithValue("$d", minutes);
            if (name is not null)
            {
                cmd.Parameters.AddWithValue("$n", name);
            }

            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The three retired per-name Einsatzzeit settings as this store still holds them, falling back
    /// per key to what <c>IncidentSettings</c> used to default to. Only the one-time Trupp-Typ
    /// migration reads these; <see cref="ReadSettings"/> no longer knows about them.
    /// </summary>
    private static LegacyEinsatzzeiten ReadLegacyEinsatzzeiten(SqliteConnection cn)
    {
        var d = LegacyEinsatzzeiten.Defaults;
        return new LegacyEinsatzzeiten(
            Minutes("agt_max_duration_minutes", d.Agt),
            Minutes("csa_max_duration_minutes", d.Chemical),
            Minutes("lpa_max_duration_minutes", d.Lpa));

        int Minutes(string key, int fallback) =>
            TruppType.ClampMaxDurationMinutes(ReadSetting(cn, key) ?? fallback);
    }

    /// <summary>
    /// Moves a pre-existing <c>md_checklist_template</c> into the n-list tables, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old table could not be widened in place: <c>ordinal</c> was its PRIMARY KEY, and the
    /// two lists shared one running sequence (Abbau continuing where Aufbau left off) to fit
    /// inside it. Per-list ordinals restart at 0, which that key cannot express.
    /// </para>
    /// <para>
    /// This store has no version marker, so the old table's own existence is the marker: it is
    /// dropped on success and the whole method then costs one <c>sqlite_master</c> lookup. That is
    /// the same idiom already used for <c>md_call_signs</c>/<c>md_brigades</c>.
    /// </para>
    /// <para>
    /// Done row by row in C# rather than as one INSERT..SELECT: undoing the offset scheme needs a
    /// per-list counter, which would mean a window function, and this matches how the rest of the
    /// file reads and writes. It is the only destructive step here, so it gets its own transaction
    /// — <c>EnsureSchema</c> otherwise runs untransacted.
    /// </para>
    /// </remarks>
    private static void MoveChecklistTemplateToLists(SqliteConnection cn)
    {
        if (!SchemaHelpers.TableExists(cn, null, "md_checklist_template"))
        {
            return;
        }

        // A store old enough to predate the Aufbau/Abbau split has neither column; widen first so
        // the read below is uniform.
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_template", "is_mandatory", "INTEGER NOT NULL DEFAULT 0");
        SchemaHelpers.AddColumnIfMissing(cn, null, "md_checklist_template", "kind", "INTEGER NOT NULL DEFAULT 0");

        var aufbau = new List<ChecklistTemplateItem>();
        var abbau = new List<ChecklistTemplateItem>();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT text, is_mandatory, kind FROM md_checklist_template ORDER BY ordinal;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var item = new ChecklistTemplateItem(r.GetString(0), r.GetInt32(1) != 0);
                (r.GetInt32(2) == 0 ? aufbau : abbau).Add(item);
            }
        }

        using var tx = cn.BeginTransaction();
        WriteChecklists(cn, tx, ChecklistTemplate.AufbauAbbau(aufbau, abbau));
        Run(cn, tx, "DROP TABLE md_checklist_template;", _ => { });
        tx.Commit();
    }

    private static MasterDataSet Read(SqliteConnection cn)
    {
        return new(
            ReadColumn(cn, "SELECT value FROM md_roles;"),
            ReadColumn(cn, "SELECT value FROM md_unit_status;"),
            ReadLinks(cn),
            ReadChecklists(cn),
            ReadNavigation(cn),
            ReadTruppTypes(cn),
            ReadPersonnel(cn),
            ReadVehicles(cn),
            ReadSettings(cn));
    }

    // One query per table, grouped here. An item row whose list_id has no list row is dropped
    // rather than filed under a guess.
    private static List<ChecklistTemplate> ReadChecklists(SqliteConnection cn)
    {
        var itemsByList = new Dictionary<string, List<ChecklistTemplateItem>>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT list_id, text, is_mandatory FROM md_checklist_items ORDER BY list_id, ordinal;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var listId = r.GetString(0);
                if (!itemsByList.TryGetValue(listId, out var items))
                {
                    items = new List<ChecklistTemplateItem>();
                    itemsByList[listId] = items;
                }

                items.Add(new ChecklistTemplateItem(r.GetString(1), r.GetInt32(2) != 0));
            }
        }

        var templates = new List<ChecklistTemplate>();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, title FROM md_checklist_lists ORDER BY ordinal;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                IReadOnlyList<ChecklistTemplateItem> items = itemsByList.TryGetValue(id, out var found)
                    ? found
                    : Array.Empty<ChecklistTemplateItem>();
                templates.Add(new ChecklistTemplate(Guid.Parse(id), r.GetString(1), items));
            }
        }

        return templates;
    }

    private static List<NavEntry> ReadNavigation(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT module, checklist_id, is_visible FROM md_navigation ORDER BY ordinal;";
        using var r = cmd.ExecuteReader();
        var entries = new List<NavEntry>();
        while (r.Read())
        {
            entries.Add(new NavEntry(
                r.GetString(0),
                r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
                r.GetInt32(2) != 0));
        }

        return entries;
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
        cmd.CommandText = "SELECT last_name, first_name, role, call_sign, phone, email, note FROM md_personnel ORDER BY last_name, first_name;";
        using var r = cmd.ExecuteReader();
        var list = new List<Person>();
        while (r.Read())
        {
            list.Add(new Person(
                r.GetString(0), r.GetString(1), Str(r, 2), Str(r, 3), Str(r, 4), Str(r, 5), Str(r, 6)));
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
