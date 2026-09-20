using LageBuch.Domain;
using LageBuch.Persistence.MasterData;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// The Stammdaten store has no version marker, so md_checklist_template could not simply be widened
// -- ordinal was its PRIMARY KEY and the two lists shared one running sequence to fit inside it.
// The move to md_checklist_lists/md_checklist_items happens on open, once, and the old table's own
// existence is what marks it as still to do.
public class MasterDataChecklistMoveTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"md-move-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    // The old table exactly as a released build left it: one ordinal sequence, Abbau's block
    // continuing where Aufbau's ended.
    private void SeedOldStore(params (string Text, int Mandatory, int Kind)[] rows)
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            using (var create = cn.CreateCommand())
            {
                create.CommandText =
                    "CREATE TABLE md_checklist_template (ordinal INTEGER PRIMARY KEY, text TEXT NOT NULL, " +
                    "is_mandatory INTEGER NOT NULL DEFAULT 0, kind INTEGER NOT NULL DEFAULT 0);";
                create.ExecuteNonQuery();
            }

            var ordinal = 0;
            foreach (var (text, mandatory, kind) in rows)
            {
                using var insert = cn.CreateCommand();
                insert.CommandText =
                    "INSERT INTO md_checklist_template (ordinal, text, is_mandatory, kind) VALUES ($o,$t,$m,$k);";
                insert.Parameters.AddWithValue("$o", ordinal++);
                insert.Parameters.AddWithValue("$t", text);
                insert.Parameters.AddWithValue("$m", mandatory);
                insert.Parameters.AddWithValue("$k", kind);
                insert.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();
    }

    private bool HasTable(string name)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        return SchemaHelpers.TableExists(cn, null, name);
    }

    [Fact]
    public void An_old_store_becomes_two_named_templates_on_the_frozen_ids()
    {
        SeedOldStore(
            ("Schritt 1", 1, 0),
            ("Schritt 2", 0, 0),
            ("Abbauschritt", 1, 1));

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(
            new[] { ChecklistDefaults.AufbauListId, ChecklistDefaults.AbbauListId },
            set.ChecklistTemplates.Select(t => t.Id));
        Assert.Equal(new[] { "Aufbau", "Abbau" }, set.ChecklistTemplates.Select(t => t.Title));
    }

    // Undoing the offset scheme is the substance of the move: per-list ordinals restart at 0, so
    // each list has to come back in its own original order rather than interleaved.
    [Fact]
    public void Each_lists_own_item_order_survives_the_offset_scheme()
    {
        SeedOldStore(
            ("Aufbau A", 0, 0),
            ("Aufbau B", 0, 0),
            ("Aufbau C", 0, 0),
            ("Abbau A", 0, 1),
            ("Abbau B", 0, 1));

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(
            new[] { "Aufbau A", "Aufbau B", "Aufbau C" },
            set.ChecklistTemplates[0].Items.Select(i => i.Text));
        Assert.Equal(
            new[] { "Abbau A", "Abbau B" },
            set.ChecklistTemplates[1].Items.Select(i => i.Text));
    }

    [Fact]
    public void Mandatory_flags_survive_the_move()
    {
        SeedOldStore(("Pflicht", 1, 0), ("Optional", 0, 0));

        var items = MasterDataStore.GetOrCreate(_path).ChecklistTemplates[0].Items;

        Assert.Equal(new[] { true, false }, items.Select(i => i.IsMandatory));
    }

    // An old store that only ever had Aufbau items gets one list, matching the V23 file migration
    // and the legacy-JSON import: no items, no list.
    [Fact]
    public void A_kind_with_no_rows_produces_no_template()
    {
        SeedOldStore(("Schritt 1", 0, 0));

        Assert.Equal(
            ChecklistDefaults.AufbauListId,
            Assert.Single(MasterDataStore.GetOrCreate(_path).ChecklistTemplates).Id);
    }

    [Fact]
    public void The_old_table_is_dropped_so_the_move_marks_itself_done()
    {
        SeedOldStore(("Schritt 1", 0, 0));
        Assert.True(HasTable("md_checklist_template"));

        MasterDataStore.GetOrCreate(_path);

        Assert.False(HasTable("md_checklist_template"));
    }

    [Fact]
    public void Reopening_does_not_duplicate_or_lose_anything()
    {
        SeedOldStore(("Schritt 1", 1, 0), ("Abbauschritt", 0, 1));

        var first = MasterDataStore.GetOrCreate(_path);
        var second = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(
            first.ChecklistTemplates.Select(t => (t.Id, t.Title, t.Items.Count)),
            second.ChecklistTemplates.Select(t => (t.Id, t.Title, t.Items.Count)));
        Assert.Equal(2, second.ChecklistTemplates.Count);
    }

    // A store old enough to predate the Aufbau/Abbau split has neither column; everything it holds
    // is an optional Aufbau item.
    [Fact]
    public void A_store_predating_the_split_moves_as_one_optional_aufbau_list()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE md_checklist_template (ordinal INTEGER PRIMARY KEY, text TEXT NOT NULL);" +
                "INSERT INTO md_checklist_template (ordinal, text) VALUES (0, 'Schritt 1'), (1, 'Schritt 2');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var template = Assert.Single(MasterDataStore.GetOrCreate(_path).ChecklistTemplates);
        Assert.Equal(ChecklistDefaults.AufbauListId, template.Id);
        Assert.Equal(new[] { "Schritt 1", "Schritt 2" }, template.Items.Select(i => i.Text));
        Assert.All(template.Items, i => Assert.False(i.IsMandatory));
    }

    [Fact]
    public void A_fresh_store_has_no_old_table_and_no_templates()
    {
        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Empty(set.ChecklistTemplates);
        Assert.Empty(set.Navigation);
        Assert.False(HasTable("md_checklist_template"));
    }

    [Fact]
    public void A_navigation_layout_round_trips_through_the_store()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            Navigation = new[]
            {
                new NavEntry(NavModules.Checklist, ChecklistDefaults.AbbauListId, true),
                new NavEntry(NavModules.Etb, null, true),
                new NavEntry(NavModules.Scba, null, false),
            },
        });

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(3, set.Navigation.Count);
        Assert.Equal(ChecklistDefaults.AbbauListId, set.Navigation[0].ChecklistId);
        Assert.Null(set.Navigation[1].ChecklistId);
        Assert.False(set.Navigation[2].IsVisible);
    }

    [Fact]
    public void Saving_a_third_list_keeps_all_three_in_order()
    {
        var extra = new Guid("33333333-3333-3333-3333-333333333333");
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            ChecklistTemplates = new[]
            {
                new ChecklistTemplate(ChecklistDefaults.AufbauListId, "Aufbau", new[] { new ChecklistTemplateItem("A", true) }),
                new ChecklistTemplate(extra, "Nachbereitung", new[] { new ChecklistTemplateItem("B", false) }),
                new ChecklistTemplate(ChecklistDefaults.AbbauListId, "Abbau", new[] { new ChecklistTemplateItem("C", false) }),
            },
        });

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(
            new[] { ChecklistDefaults.AufbauListId, extra, ChecklistDefaults.AbbauListId },
            set.ChecklistTemplates.Select(t => t.Id));
        Assert.Equal(new[] { "A", "B", "C" }, set.ChecklistTemplates.Select(t => t.Items[0].Text));
    }
}
