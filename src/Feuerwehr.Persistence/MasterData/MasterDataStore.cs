using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.MasterData;

public sealed class MasterDataStore
{
    public MasterDataSet GetOrSeed(string path)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        EnsureSchema(cn);
        // Merge rather than seed-only-when-empty. Filling a table only while it was still empty
        // meant every later addition to the seed was invisible on an existing installation --
        // which silently cost eight radio call signs, the CSA-Trupp type and the whole personnel
        // roster before it was noticed. Merging is additive and order-preserving: seed values
        // first, in seed order, then anything already present that the seed does not know about,
        // so nothing local is dropped.
        Merge(cn, MasterDataDefaults.LoadEmbedded());
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
