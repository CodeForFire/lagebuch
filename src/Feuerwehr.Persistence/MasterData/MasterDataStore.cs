using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.MasterData;

public sealed class MasterDataStore
{
    public MasterDataSet GetOrSeed(string path)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        EnsureSchema(cn);
        var seed = MasterDataDefaults.LoadEmbedded();

        // Two paths. With no snapshot yet -- a fresh DB, or the first start after this feature
        // shipped -- run the original seed-first merge, which backfills in seed order and keeps
        // local additions. Once a snapshot exists, only entries the seed has gained *since* that
        // snapshot are added, and existing rows are never removed or reordered -- so an edit made
        // in the Stammdaten editor survives the next start.
        if (SnapshotIsEmpty(cn))
            Merge(cn, seed);
        else
            AppendNewSinceSnapshot(cn, seed);

        WriteSnapshotIfChanged(cn, seed);
        return Read(cn);
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
            CREATE TABLE IF NOT EXISTS md_checklist_template (ordinal INTEGER PRIMARY KEY, text TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_trupp_types (value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_personnel (
                last_name TEXT NOT NULL,
                first_name TEXT NOT NULL,
                role TEXT,
                call_sign TEXT,
                phone TEXT
            );
            CREATE TABLE IF NOT EXISTS md_seed_snapshot (category TEXT NOT NULL, item_key TEXT NOT NULL);
            """);
    }

    private static void Merge(SqliteConnection cn, MasterDataSet set)
    {
        // Every read happens before the transaction opens: Microsoft.Data.Sqlite rejects a command
        // that has no Transaction while one is pending, and computing the whole plan up front also
        // keeps the write phase short.
        var lists = new (string Table, IReadOnlyList<string> Seed)[]
        {
            ("md_roles", set.Roles),
            ("md_status", set.Status),
            ("md_unit_status", set.UnitStatus),
            ("md_equipment", set.Equipment),
            ("md_districts", set.Districts),
            ("md_call_signs", set.RadioCallSigns),
            ("md_brigades", set.Brigades),
            ("md_trupp_types", set.TruppTypes),
        };

        var listPlans = lists
            .Select(l => (l.Table, Merged: Combine(l.Seed, ReadColumn(cn, $"SELECT value FROM {l.Table};"), v => v)))
            .Where(p => !p.Merged.InSync)
            .ToList();

        var streets = Combine(set.Streets, ReadStreets(cn), s => Key(s.Name, s.District));
        var checklist = Combine(set.ChecklistTemplate,
            ReadColumn(cn, "SELECT text FROM md_checklist_template ORDER BY ordinal;"), t => t);
        // Personnel is read back sorted by name, so the order rows happen to sit in on disk is not
        // observable — comparing it would rewrite the table on every start for no reason.
        var personnel = Combine(set.Personnel, ReadPersonnel(cn),
            p => Key(p.LastName, p.FirstName), orderMatters: false);

        if (listPlans.Count == 0 && streets.InSync && checklist.InSync && personnel.InSync)
            return; // already in sync — do not rewrite on every start

        using var tx = cn.BeginTransaction();

        foreach (var (table, merged) in listPlans)
        {
            Run(cn, tx, $"DELETE FROM {table};", _ => { });
            InsertList(cn, tx, table, merged.Values);
        }

        if (!streets.InSync)
        {
            Run(cn, tx, "DELETE FROM md_streets;", _ => { });
            foreach (var s in streets.Values)
                Run(cn, tx, "INSERT INTO md_streets (name, district) VALUES ($n,$d);",
                    p => { p("$n", s.Name); p("$d", s.District); });
        }

        if (!checklist.InSync)
        {
            Run(cn, tx, "DELETE FROM md_checklist_template;", _ => { });
            for (var i = 0; i < checklist.Values.Count; i++)
            {
                var text = checklist.Values[i];
                Run(cn, tx, "INSERT INTO md_checklist_template (ordinal, text) VALUES ($o,$t);",
                    p => { p("$o", i); p("$t", text); });
            }
        }

        if (!personnel.InSync)
        {
            Run(cn, tx, "DELETE FROM md_personnel;", _ => { });
            foreach (var person in personnel.Values)
                Run(cn, tx, "INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone) VALUES ($l,$f,$r,$c,$p);",
                    p =>
                    {
                        p("$l", person.LastName); p("$f", person.FirstName);
                        p("$r", (object?)person.Role ?? DBNull.Value);
                        p("$c", (object?)person.CallSign ?? DBNull.Value);
                        p("$p", (object?)person.Phone ?? DBNull.Value);
                    });
        }

        tx.Commit();
    }

    private static bool SnapshotIsEmpty(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM md_seed_snapshot;";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
    }

    /// <summary>The seed flattened to (category, identity-key) rows — the shape stored in the snapshot.</summary>
    private static IReadOnlyList<(string Category, string Key)> SnapshotEntries(MasterDataSet seed)
    {
        var rows = new List<(string, string)>();
        void AddList(string category, IReadOnlyList<string> values)
        {
            foreach (var v in values) rows.Add((category, v));
        }

        AddList("roles", seed.Roles);
        AddList("status", seed.Status);
        AddList("unit_status", seed.UnitStatus);
        AddList("equipment", seed.Equipment);
        AddList("districts", seed.Districts);
        AddList("call_signs", seed.RadioCallSigns);
        AddList("brigades", seed.Brigades);
        AddList("trupp_types", seed.TruppTypes);
        AddList("checklist", seed.ChecklistTemplate);
        foreach (var s in seed.Streets) rows.Add(("streets", Key(s.Name, s.District)));
        foreach (var p in seed.Personnel) rows.Add(("personnel", Key(p.LastName, p.FirstName)));
        return rows;
    }

    /// <summary>
    /// Append seed entries that are new since the last snapshot, at the end of their table, only
    /// when not already present. Never deletes and never reorders — the editor and any local
    /// additions own the existing rows.
    /// </summary>
    private static void AppendNewSinceSnapshot(SqliteConnection cn, MasterDataSet seed)
    {
        var snapshot = ReadSnapshotKeys(cn);

        var lists = new (string Category, string Table, IReadOnlyList<string> Seed)[]
        {
            ("roles", "md_roles", seed.Roles),
            ("status", "md_status", seed.Status),
            ("unit_status", "md_unit_status", seed.UnitStatus),
            ("equipment", "md_equipment", seed.Equipment),
            ("districts", "md_districts", seed.Districts),
            ("call_signs", "md_call_signs", seed.RadioCallSigns),
            ("brigades", "md_brigades", seed.Brigades),
            ("trupp_types", "md_trupp_types", seed.TruppTypes),
        };

        var listAdditions = lists
            .Select(l => (l.Table, New: NewValues(l.Seed, snapshot[l.Category], ReadColumn(cn, $"SELECT value FROM {l.Table};"), v => v)))
            .Where(x => x.New.Count > 0)
            .ToList();

        var newStreets = NewValues(seed.Streets, snapshot["streets"], ReadStreets(cn), s => Key(s.Name, s.District));
        var newChecklist = NewValues(seed.ChecklistTemplate, snapshot["checklist"],
            ReadColumn(cn, "SELECT text FROM md_checklist_template ORDER BY ordinal;"), t => t);
        var newPersonnel = NewValues(seed.Personnel, snapshot["personnel"], ReadPersonnel(cn),
            p => Key(p.LastName, p.FirstName));

        if (listAdditions.Count == 0 && newStreets.Count == 0 && newChecklist.Count == 0 && newPersonnel.Count == 0)
            return;

        using var tx = cn.BeginTransaction();

        foreach (var (table, additions) in listAdditions)
            InsertList(cn, tx, table, additions);

        foreach (var s in newStreets)
            Run(cn, tx, "INSERT INTO md_streets (name, district) VALUES ($n,$d);",
                p => { p("$n", s.Name); p("$d", s.District); });

        if (newChecklist.Count > 0)
        {
            var next = NextChecklistOrdinal(cn, tx);
            foreach (var text in newChecklist)
                Run(cn, tx, "INSERT INTO md_checklist_template (ordinal, text) VALUES ($o,$t);",
                    p => { p("$o", next++); p("$t", text); });
        }

        foreach (var person in newPersonnel)
            Run(cn, tx, "INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone) VALUES ($l,$f,$r,$c,$p);",
                p =>
                {
                    p("$l", person.LastName); p("$f", person.FirstName);
                    p("$r", (object?)person.Role ?? DBNull.Value);
                    p("$c", (object?)person.CallSign ?? DBNull.Value);
                    p("$p", (object?)person.Phone ?? DBNull.Value);
                });

        tx.Commit();
    }

    /// <summary>Seed entries whose key is neither in the snapshot nor already in the table, in seed order.</summary>
    private static IReadOnlyList<T> NewValues<T>(
        IReadOnlyList<T> seed, ISet<string> snapshotKeys, IReadOnlyList<T> existing, Func<T, string> key)
    {
        var have = existing.Select(key).ToHashSet(StringComparer.Ordinal);
        return seed.Where(s => !snapshotKeys.Contains(key(s)) && have.Add(key(s))).ToList();
    }

    private static Dictionary<string, HashSet<string>> ReadSnapshotKeys(SqliteConnection cn)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var c in new[] { "roles", "status", "unit_status", "equipment", "districts",
                                  "call_signs", "brigades", "trupp_types", "streets", "checklist", "personnel" })
            map[c] = new HashSet<string>(StringComparer.Ordinal);

        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT category, item_key FROM md_seed_snapshot;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var category = r.GetString(0);
            if (!map.TryGetValue(category, out var set)) map[category] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(r.GetString(1));
        }
        return map;
    }

    private static int NextChecklistOrdinal(SqliteConnection cn, SqliteTransaction tx)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM md_checklist_template;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void WriteSnapshotIfChanged(SqliteConnection cn, MasterDataSet seed)
    {
        var desired = SnapshotEntries(seed);
        var stored = ReadSnapshotRows(cn);
        if (desired.Count == stored.Count
            && desired.Zip(stored).All(pair => pair.First.Category == pair.Second.Category && pair.First.Key == pair.Second.Key))
            return; // already current — the steady-state start writes nothing

        using var tx = cn.BeginTransaction();
        Run(cn, tx, "DELETE FROM md_seed_snapshot;", _ => { });
        foreach (var (category, key) in desired)
            Run(cn, tx, "INSERT INTO md_seed_snapshot (category, item_key) VALUES ($c,$k);",
                p => { p("$c", category); p("$k", key); });
        tx.Commit();
    }

    private static List<(string Category, string Key)> ReadSnapshotRows(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT category, item_key FROM md_seed_snapshot ORDER BY rowid;";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>
    /// Composite identity for a multi-column row. The separator is required, not cosmetic:
    /// plain concatenation would make ("Bahnhofstr.", "FFB") collide with ("Bahnhofstr.F", "FB").
    /// </summary>
    private static string Key(params string?[] parts) => string.Join('\u001F', parts);

    /// <summary>
    /// Seed values first, in seed order, then any existing value the seed does not contain.
    /// New seed entries therefore appear in their intended position rather than tacked onto the
    /// end, and local additions survive. <c>InSync</c> is true when the table already matches the
    /// merge result, so the common every-startup case writes nothing at all.
    /// </summary>
    private static (IReadOnlyList<T> Values, bool InSync) Combine<T>(
        IReadOnlyList<T> seed, IReadOnlyList<T> existing, Func<T, string> key, bool orderMatters = true)
    {
        var merged = new List<T>(seed);
        var seen = seed.Select(key).ToHashSet(StringComparer.Ordinal);
        foreach (var e in existing)
            if (seen.Add(key(e)))
                merged.Add(e);

        var inSync = merged.Count == existing.Count
            && (orderMatters
                ? merged.Zip(existing).All(pair => key(pair.First) == key(pair.Second))
                : existing.All(e => seen.Contains(key(e))));
        return (merged, inSync);
    }

    private static MasterDataSet Read(SqliteConnection cn) => new(
        ReadColumn(cn, "SELECT value FROM md_roles;"),
        ReadColumn(cn, "SELECT value FROM md_status;"),
        ReadColumn(cn, "SELECT value FROM md_equipment;"),
        ReadColumn(cn, "SELECT value FROM md_districts;"),
        ReadColumn(cn, "SELECT value FROM md_call_signs;"),
        ReadColumn(cn, "SELECT value FROM md_brigades;"),
        ReadColumn(cn, "SELECT value FROM md_unit_status;"),
        ReadStreets(cn),
        ReadColumn(cn, "SELECT text FROM md_checklist_template ORDER BY ordinal;"),
        ReadColumn(cn, "SELECT value FROM md_trupp_types;"),
        ReadPersonnel(cn));

    private static void InsertList(SqliteConnection cn, SqliteTransaction tx, string table, IReadOnlyList<string> values)
    {
        foreach (var v in values)
            Run(cn, tx, $"INSERT INTO {table} (value) VALUES ($v);", p => p("$v", v));
    }

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

    private static void Run(SqliteConnection cn, SqliteTransaction tx, string sql, Action<Action<string, object>> bind)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind((name, value) => cmd.Parameters.AddWithValue(name, value));
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
