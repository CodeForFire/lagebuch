using Feuerwehr.Persistence.MasterData;
using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

public class MasterDataStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"md-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Embedded_defaults_contain_the_known_ffb_lists()
    {
        var set = MasterDataDefaults.LoadEmbedded();
        Assert.Contains("EL", set.Roles);
        Assert.Contains("abbestellt", set.Status);
        Assert.Contains("FFB", set.Districts);
        Assert.Equal(313, set.Streets.Count);
        Assert.Equal(12, set.ChecklistTemplate.Count);
        Assert.Contains(set.Streets, s => s.Name == "Bahnhofstr.");
    }

    [Fact]
    public void GetOrSeed_creates_and_persists_master_data()
    {
        var store = new MasterDataStore();
        var first = store.GetOrSeed(_path);
        Assert.Equal(9, first.Roles.Count);

        // second call reads existing store without reseeding duplicates
        var second = store.GetOrSeed(_path);
        Assert.Equal(first.Streets.Count, second.Streets.Count);
        Assert.Equal(first.Roles.Count, second.Roles.Count);
    }

    [Fact]
    public void GetOrSeed_seeds_trupp_types()
    {
        var set = new MasterDataStore().GetOrSeed(_path);
        Assert.Contains("Angriffstrupp", set.TruppTypes);
    }

    [Fact]
    public void GetOrSeed_backfills_a_new_category_into_an_already_seeded_db()
    {
        // Simulate a masterdata.db created before truppTypes existed: roles are present
        // (so the legacy "is the DB empty?" check is false) but the trupp-types table is bare.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE md_roles (value TEXT NOT NULL); INSERT INTO md_roles (value) VALUES ('EL');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var set = new MasterDataStore().GetOrSeed(_path);

        Assert.Contains("Angriffstrupp", set.TruppTypes);
    }

    // --- Personnel roster (issue #17) ---

    [Fact]
    public void Personnel_is_empty_when_no_roster_has_been_installed()
    {
        // personnel.json is gitignored and only embedded when a local CLS export exists, so on CI
        // and on a fresh clone this is the normal state -- it must not throw or seed placeholders.
        // A developer with a local export sees their own roster instead, hence the range check.
        var set = MasterDataDefaults.LoadEmbedded();
        Assert.NotNull(set.Personnel);
        Assert.DoesNotContain(set.Personnel, p => string.IsNullOrWhiteSpace(p.LastName));
    }

    [Fact]
    public void Personnel_round_trips_through_the_store()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE md_personnel (
                    last_name TEXT NOT NULL, first_name TEXT NOT NULL,
                    role TEXT, call_sign TEXT, phone TEXT);
                INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone)
                VALUES ('Mustermann', 'Max', 'ZF', 'Land 1', '01 71 / 1 23 45 67'),
                       ('Musterfrau', 'Erika', NULL, NULL, NULL);
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var set = new MasterDataStore().GetOrSeed(_path);

        // Asserted by membership, not by whole-collection equality: seeding now merges, so a
        // developer with a local personnel.json installed would otherwise see this fail purely
        // because their roster is also present. It passed before only because the old
        // seed-when-empty rule skipped a non-empty table.
        Assert.Contains("Musterfrau, Erika", set.Personnel.Select(p => p.DisplayName));
        Assert.Contains("Mustermann, Max", set.Personnel.Select(p => p.DisplayName));
        var max = set.Personnel.Single(p => p.LastName == "Mustermann");
        Assert.Equal("01 71 / 1 23 45 67", max.Phone);
        Assert.Equal("Land 1", max.CallSign);
        Assert.Null(set.Personnel.Single(p => p.LastName == "Musterfrau").Phone);
    }

    // --- Kräfte master data (issue #18) ---

    [Fact]
    public void Seed_contains_the_feuerwehren_from_the_hilfsdaten_sheet()
    {
        var set = MasterDataDefaults.LoadEmbedded();
        Assert.Equal(
            new[]
            {
                "FFB Wache 1", "FFB Wache 2", "Aich", "Puch", "Emmering", "Schöngeising",
                "Biburg", "Maisach", "Germering", "Mammendorf", "Landkreis",
            },
            set.Brigades);
    }

    [Fact]
    public void Seed_contains_the_per_unit_status_vocabulary()
    {
        var set = MasterDataDefaults.LoadEmbedded();
        Assert.Equal(new[] { "Alarmiert", "Auf Anfahrt", "Bereitstellungsraum", "Im Einsatz" }, set.UnitStatus);
        // Must not be confused with the incident-level list, which is a different vocabulary.
        Assert.Contains("aufgenommen", set.Status);
        Assert.DoesNotContain("aufgenommen", set.UnitStatus);
    }

    [Theory]
    // The units listed on the Kräfteübersicht sheet that the original import missed.
    [InlineData("FFB 1/39/1")]
    [InlineData("FFB 1/59/1")]
    [InlineData("Kater 13/1")]
    [InlineData("Land 13/1")]
    [InlineData("Land 1")]
    [InlineData("Land 4")]
    public void Seed_contains_the_previously_missing_call_signs(string callSign)
        => Assert.Contains(callSign, MasterDataDefaults.LoadEmbedded().RadioCallSigns);

    [Fact]
    public void Adding_call_signs_did_not_drop_any_existing_one()
    {
        var set = MasterDataDefaults.LoadEmbedded();
        Assert.Equal(33, set.RadioCallSigns.Count);
        Assert.Equal(set.RadioCallSigns.Count, set.RadioCallSigns.Distinct().Count());
        foreach (var kept in new[] { "FFB 1/10/1", "FFB 1/94/1", "FFB 1/99/2", "Aich 11/1", "Puch 43/1" })
            Assert.Contains(kept, set.RadioCallSigns);
    }

    [Fact]
    public void Brigades_and_unit_status_backfill_into_an_already_seeded_db()
    {
        // The categories added by issue #18 must reach a masterdata.db created before they existed.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE md_roles (value TEXT NOT NULL); INSERT INTO md_roles (value) VALUES ('EL');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var set = new MasterDataStore().GetOrSeed(_path);

        Assert.Contains("FFB Wache 1", set.Brigades);
        Assert.Contains("Bereitstellungsraum", set.UnitStatus);
    }

    [Fact]
    public void A_person_without_a_first_name_displays_as_the_last_name_alone()
        => Assert.Equal("Mustermann", new Person("Mustermann", "", null, null, null).DisplayName);

    // --- Seed merging ---

    // The original behaviour only filled a table while it was still entirely empty, so every
    // later seed addition was invisible on an existing installation. That bit three times:
    // eight missing radio call signs, the CSA-Trupp type, and the personnel roster.
    [Fact]
    public void New_seed_values_reach_a_db_that_predates_them()
    {
        SeedLegacy("md_trupp_types",
            "Angriffstrupp", "Wassertrupp", "Schlauchtrupp", "Sicherheitstrupp", "Sonstiger Trupp");

        var set = new MasterDataStore().GetOrSeed(_path);

        Assert.Contains("CSA-Trupp", set.TruppTypes);
    }

    [Fact]
    public void Merged_values_land_in_seed_order_not_appended_at_the_end()
    {
        SeedLegacy("md_trupp_types",
            "Angriffstrupp", "Wassertrupp", "Schlauchtrupp", "Sicherheitstrupp", "Sonstiger Trupp");

        var set = new MasterDataStore().GetOrSeed(_path);

        // CSA-Trupp belongs with the other real Trupp types, ahead of the catch-all.
        Assert.Equal(MasterDataDefaults.LoadEmbedded().TruppTypes, set.TruppTypes);
    }

    [Fact]
    public void Values_the_seed_does_not_know_about_are_kept()
    {
        SeedLegacy("md_trupp_types", "Angriffstrupp", "Eigener Sondertrupp");

        var set = new MasterDataStore().GetOrSeed(_path);

        // Merging is additive: a hand-added entry must survive, even though nothing in the app
        // creates one yet. Silently dropping local data would be worse than a stale list.
        Assert.Contains("Eigener Sondertrupp", set.TruppTypes);
        Assert.Contains("CSA-Trupp", set.TruppTypes);
    }

    [Fact]
    public void Merging_repeatedly_does_not_duplicate_anything()
    {
        var store = new MasterDataStore();
        var first = store.GetOrSeed(_path);
        var second = store.GetOrSeed(_path);
        var third = store.GetOrSeed(_path);

        Assert.Equal(first.TruppTypes, third.TruppTypes);
        Assert.Equal(first.RadioCallSigns, second.RadioCallSigns);
        Assert.Equal(third.Streets.Count, third.Streets.Distinct().Count());
        Assert.Equal(third.RadioCallSigns.Count, third.RadioCallSigns.Distinct().Count());
        Assert.Equal(313, third.Streets.Count);
    }

    [Fact]
    public void Streets_merge_on_name_and_district_together()
    {
        ExecRaw("CREATE TABLE md_streets (name TEXT NOT NULL, district TEXT NOT NULL);"
              + "INSERT INTO md_streets (name, district) VALUES ('Bahnhofstr.', 'FFB'), ('Eigene Str.', 'Aich');");

        var set = new MasterDataStore().GetOrSeed(_path);

        Assert.Single(set.Streets, s => s.Name == "Bahnhofstr." && s.District == "FFB");
        Assert.Contains(set.Streets, s => s.Name == "Eigene Str.");
        Assert.Equal(314, set.Streets.Count); // 313 seeded + the local one
    }

    [Fact]
    public void Personnel_merge_on_the_whole_name()
    {
        ExecRaw("CREATE TABLE md_personnel (last_name TEXT NOT NULL, first_name TEXT NOT NULL,"
              + " role TEXT, call_sign TEXT, phone TEXT);"
              + "INSERT INTO md_personnel (last_name, first_name, role, call_sign, phone)"
              + " VALUES ('Eigen', 'Person', NULL, NULL, '01 71 / 0 00 00 00');");

        var set = new MasterDataStore().GetOrSeed(_path);

        Assert.Contains(set.Personnel, p => p.LastName == "Eigen" && p.FirstName == "Person");
    }

    [Fact]
    public void Checklist_template_gains_new_steps_without_losing_local_ones()
    {
        ExecRaw("CREATE TABLE md_checklist_template (ordinal INTEGER PRIMARY KEY, text TEXT NOT NULL);"
              + "INSERT INTO md_checklist_template (ordinal, text) VALUES (0, 'Eigener Schritt');");

        var set = new MasterDataStore().GetOrSeed(_path);

        Assert.Equal(13, set.ChecklistTemplate.Count); // 12 seeded + the local one
        Assert.Contains("Eigener Schritt", set.ChecklistTemplate);
        Assert.Equal(MasterDataDefaults.LoadEmbedded().ChecklistTemplate[0], set.ChecklistTemplate[0]);
    }

    // --- Snapshot-aware seeding (issue #27) ---

    [Fact]
    public void Deleting_a_seed_value_after_the_first_seed_stays_deleted()
    {
        var store = new MasterDataStore();
        store.GetOrSeed(_path);                       // first run: seeds + writes snapshot
        ExecRaw("DELETE FROM md_roles WHERE value = 'EL';");

        var set = store.GetOrSeed(_path);             // snapshot path: must not resurrect 'EL'

        Assert.DoesNotContain("EL", set.Roles);
    }

    [Fact]
    public void A_value_absent_from_the_snapshot_is_added_as_new_seed()
    {
        var store = new MasterDataStore();
        store.GetOrSeed(_path);
        // Simulate a value the previously-applied seed did not contain: forget it in the snapshot
        // AND remove it from the table, so the next start sees it as genuinely new.
        ExecRaw("DELETE FROM md_roles WHERE value = 'EL'; DELETE FROM md_seed_snapshot WHERE item_key = 'EL';");

        var set = store.GetOrSeed(_path);

        Assert.Contains("EL", set.Roles);             // reappears: new since snapshot
    }

    [Fact]
    public void Once_a_snapshot_exists_the_stored_order_is_preserved()
    {
        var store = new MasterDataStore();
        store.GetOrSeed(_path);
        // Replace roles with two entries in a custom order. The snapshot already remembers the full
        // seed, so nothing is re-added and nothing is reordered to seed-first.
        ExecRaw("DELETE FROM md_roles;"
              + "INSERT INTO md_roles (value) VALUES ('ZF'), ('EL');");

        var set = store.GetOrSeed(_path);

        Assert.Equal(new[] { "ZF", "EL" }, set.Roles);
    }

    // --- Save write path (issue #28) ---

    [Fact]
    public void Save_round_trips_every_category_including_personnel()
    {
        var store = new MasterDataStore();
        var seeded = store.GetOrSeed(_path);

        var edited = seeded with
        {
            Roles = new[] { "EL", "Eigene Rolle" },
            TruppTypes = new[] { "Angriffstrupp" },
            Personnel = new[] { new Person("Neu", "Person", "GF", "Land 1", "01 71 / 0 00 00 00") },
        };
        store.Save(_path, edited);

        var reopened = store.GetOrSeed(_path);
        Assert.Equal(new[] { "EL", "Eigene Rolle" }, reopened.Roles);
        Assert.Contains(reopened.Personnel, p => p.LastName == "Neu" && p.CallSign == "Land 1");
        Assert.Equal(seeded.Streets.Count, reopened.Streets.Count); // streets carried through untouched
    }

    [Fact]
    public void A_value_deleted_through_Save_does_not_come_back_on_the_next_start()
    {
        var store = new MasterDataStore();
        var seeded = store.GetOrSeed(_path);

        store.Save(_path, seeded with { Roles = seeded.Roles.Where(r => r != "EL").ToList() });

        var reopened = store.GetOrSeed(_path);
        Assert.DoesNotContain("EL", reopened.Roles);
    }

    private void SeedLegacy(string table, params string[] values)
    {
        var sql = $"CREATE TABLE {table} (value TEXT NOT NULL);"
                + string.Join("", values.Select(v => $"INSERT INTO {table} (value) VALUES ('{v}');"));
        ExecRaw(sql);
    }

    private void ExecRaw(string sql)
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }
}
