using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.MasterData;

public sealed class MasterDataStore
{
    public MasterDataSet GetOrSeed(string path)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        EnsureSchema(cn);
        // Backfill per category, not all-or-nothing: a DB seeded before a new category existed
        // has the populated old tables but an empty new one. Seeding only when the whole store
        // looks empty would skip the new category forever, leaving its dropdown blank.
        SeedMissing(cn, MasterDataDefaults.LoadEmbedded());
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
            CREATE TABLE IF NOT EXISTS md_streets (name TEXT NOT NULL, district TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_checklist_template (ordinal INTEGER PRIMARY KEY, text TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS md_trupp_types (value TEXT NOT NULL);
            """);
    }

    private static bool IsTableEmpty(SqliteConnection cn, string table)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {table};";
        return (long)cmd.ExecuteScalar()! == 0;
    }

    private static void SeedMissing(SqliteConnection cn, MasterDataSet set)
    {
        using var tx = cn.BeginTransaction();
        SeedListIfEmpty(cn, tx, "md_roles", set.Roles);
        SeedListIfEmpty(cn, tx, "md_status", set.Status);
        SeedListIfEmpty(cn, tx, "md_equipment", set.Equipment);
        SeedListIfEmpty(cn, tx, "md_districts", set.Districts);
        SeedListIfEmpty(cn, tx, "md_call_signs", set.RadioCallSigns);
        SeedListIfEmpty(cn, tx, "md_trupp_types", set.TruppTypes);
        if (IsTableEmpty(cn, "md_streets"))
            foreach (var s in set.Streets)
                Run(cn, tx, "INSERT INTO md_streets (name, district) VALUES ($n,$d);",
                    p => { p("$n", s.Name); p("$d", s.District); });
        if (IsTableEmpty(cn, "md_checklist_template"))
            for (var i = 0; i < set.ChecklistTemplate.Count; i++)
                Run(cn, tx, "INSERT INTO md_checklist_template (ordinal, text) VALUES ($o,$t);",
                    p => { p("$o", i); p("$t", set.ChecklistTemplate[i]); });
        tx.Commit();
    }

    private static void SeedListIfEmpty(SqliteConnection cn, SqliteTransaction tx, string table, IReadOnlyList<string> values)
    {
        if (IsTableEmpty(cn, table))
            InsertList(cn, tx, table, values);
    }

    private static MasterDataSet Read(SqliteConnection cn) => new(
        ReadColumn(cn, "SELECT value FROM md_roles;"),
        ReadColumn(cn, "SELECT value FROM md_status;"),
        ReadColumn(cn, "SELECT value FROM md_equipment;"),
        ReadColumn(cn, "SELECT value FROM md_districts;"),
        ReadColumn(cn, "SELECT value FROM md_call_signs;"),
        ReadStreets(cn),
        ReadColumn(cn, "SELECT text FROM md_checklist_template ORDER BY ordinal;"),
        ReadColumn(cn, "SELECT value FROM md_trupp_types;"));

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
