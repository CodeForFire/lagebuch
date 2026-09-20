using System.Globalization;
using LageBuch.Domain;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// V23 gives an incident file its own checklist_lists table, so it knows what its Checklisten are
// called without asking Stammdaten, and backfills checklist_items.list_id from the old kind
// column. Getting the backfill wrong is silent -- items keep loading, just under the wrong list
// or under none -- so each rule gets its own test.
public class ChecklistListMigrationTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"mig22-{Guid.NewGuid():N}.fwincident");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    // A V21 checklist_items table with the rows a pre-V23 build would have written.
    private void SeedV21(params (string Text, int Kind)[] items)
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (21);" +
                "CREATE TABLE checklist_items (id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL, text TEXT NOT NULL, " +
                "is_done INTEGER NOT NULL, note TEXT, is_mandatory INTEGER NOT NULL DEFAULT 0, kind INTEGER NOT NULL DEFAULT 0);";
            cmd.ExecuteNonQuery();

            var ordinal = 0;
            foreach (var (text, kind) in items)
            {
                using var insert = cn.CreateCommand();
                insert.CommandText =
                    "INSERT INTO checklist_items (id, ordinal, text, is_done, note, is_mandatory, kind) " +
                    "VALUES ($id,$o,$t,0,NULL,0,$k);";
                insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                insert.Parameters.AddWithValue("$o", ordinal++);
                insert.Parameters.AddWithValue("$t", text);
                insert.Parameters.AddWithValue("$k", kind);
                insert.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();
    }

    private static List<(string Id, long Ordinal, string Title)> ReadLists(SqliteConnection cn)
    {
        var rows = new List<(string, long, string)>();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id, ordinal, title FROM checklist_lists ORDER BY ordinal;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
        }

        return rows;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Test-only counting query; every string interpolated is a compile-time constant guid from ChecklistDefaults.")]
    private static long Scalar(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void A_v21_file_gains_a_list_row_per_kind_that_actually_has_items()
    {
        SeedV21(("Fahrzeug prüfen", 0), ("Blaulicht aus", 0), ("Standort räumen", 1));

        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        var lists = ReadLists(cn);
        Assert.Equal(2, lists.Count);
        Assert.Equal((ChecklistDefaults.AufbauListId.ToString(), 0L, "Aufbau"), lists[0]);
        Assert.Equal((ChecklistDefaults.AbbauListId.ToString(), 1L, "Abbau"), lists[1]);
    }

    // The guid literals in the migration SQL have to match Guid.ToString()'s default "D" form,
    // because that is what the repository writes and what Guid.Parse reads back. A mismatched
    // format would leave every item orphaned from its list with nothing failing loudly.
    [Fact]
    public void List_ids_are_written_in_the_same_form_the_repository_writes()
    {
        SeedV21(("Fahrzeug prüfen", 0));

        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        var id = Assert.Single(ReadLists(cn)).Id;
        Assert.Equal(ChecklistDefaults.AufbauListId.ToString(), id);
        Assert.Equal(ChecklistDefaults.AufbauListId, Guid.Parse(id));
    }

    // Decision: a file whose Abbau list was never filled loses its permanently-empty ABBAU tab.
    [Fact]
    public void A_kind_with_no_items_gets_no_list_row()
    {
        SeedV21(("Fahrzeug prüfen", 0), ("Blaulicht aus", 0));

        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        var list = Assert.Single(ReadLists(cn));
        Assert.Equal("Aufbau", list.Title);
    }

    [Fact]
    public void A_file_with_no_checklist_items_at_all_gains_no_lists()
    {
        SeedV21();

        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        Assert.Empty(ReadLists(cn));
    }

    [Fact]
    public void Every_item_is_filed_under_the_list_its_kind_named()
    {
        SeedV21(("Fahrzeug prüfen", 0), ("Standort räumen", 1), ("Material zählen", 1));

        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        Assert.Equal(0, Scalar(cn, "SELECT count(*) FROM checklist_items WHERE list_id IS NULL;"));
        Assert.Equal(
            1,
            Scalar(cn, $"SELECT count(*) FROM checklist_items WHERE list_id = '{ChecklistDefaults.AufbauListId}';"));
        Assert.Equal(
            2,
            Scalar(cn, $"SELECT count(*) FROM checklist_items WHERE list_id = '{ChecklistDefaults.AbbauListId}';"));
    }

    // kind stays on the table, dormant, rather than costing a full table rebuild to drop -- the
    // same call incident_meta.ils_number already makes.
    [Fact]
    public void The_old_kind_column_is_kept()
    {
        SeedV21(("Fahrzeug prüfen", 0));

        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        Assert.Equal(1, Scalar(cn, "SELECT count(*) FROM pragma_table_info('checklist_items') WHERE name = 'kind';"));
    }

    [Fact]
    public void Migrating_twice_does_not_duplicate_the_lists()
    {
        SeedV21(("Fahrzeug prüfen", 0), ("Standort räumen", 1));

        using (var first = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(first);
        }

        SqliteConnection.ClearAllPools();

        using var second = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(second);

        Assert.Equal(2, ReadLists(second).Count);
    }

    [Fact]
    public void A_fresh_file_has_the_checklist_lists_table()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);

        Assert.Equal(
            1,
            Scalar(cn, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='checklist_lists';"));
    }
}
