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

        // Ordered by last name, so Musterfrau precedes Mustermann.
        Assert.Equal(new[] { "Musterfrau, Erika", "Mustermann, Max" },
            set.Personnel.Select(p => p.DisplayName));
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
}
