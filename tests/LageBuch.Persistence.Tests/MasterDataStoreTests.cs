using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class MasterDataStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"md-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void GetOrCreate_returns_an_empty_set_for_a_fresh_database()
    {
        // The app ships with no seed, so a brand-new masterdata.db comes back empty -- populated
        // only by Save (the editor's Import), never by a compiled-in default.
        var set = new MasterDataStore().GetOrCreate(_path);
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void GetOrCreate_is_idempotent_and_never_seeds()
    {
        var store = new MasterDataStore();
        Assert.True(store.GetOrCreate(_path).IsEmpty);
        Assert.True(store.GetOrCreate(_path).IsEmpty); // still empty on a second open
    }

    [Fact]
    public void Save_then_GetOrCreate_round_trips_every_category_including_personnel()
    {
        var store = new MasterDataStore();
        var set = MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "ZF" },
            Status = new[] { "aufgenommen" },
            UnitStatus = new[] { "Alarmiert" },
            Equipment = new[] { "Mobilteil 1" },
            Districts = new[] { "FFB" },
            Brigades = new[] { "FFB Wache 1" },
            RadioCallSigns = new[] { "Land 1" },
            TruppTypes = new[] { "Angriffstrupp" },
            Einsatzarten = new[] { "B", "THL" },
            ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("Schritt 1", true), new ChecklistTemplateItem("Schritt 2", false) },
            ChecklistTemplateAbbau = new[] { new ChecklistTemplateItem("Abbauschritt", true) },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") },
            Links = new[] { new Link("Wetterdienst", "https://dwd.de") },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
        };
        store.Save(_path, set);

        var reopened = store.GetOrCreate(_path);
        Assert.Equal(new[] { "EL", "ZF" }, reopened.Roles);
        Assert.Equal(new[] { "B", "THL" }, reopened.Einsatzarten);
        Assert.Equal(
            new[] { new ChecklistTemplateItem("Schritt 1", true), new ChecklistTemplateItem("Schritt 2", false) },
            reopened.ChecklistTemplateAufbau);
        Assert.Equal(new[] { new ChecklistTemplateItem("Abbauschritt", true) }, reopened.ChecklistTemplateAbbau);
        Assert.Contains(reopened.Streets, s => s.Name == "Bahnhofstr." && s.District == "FFB");
        Assert.Equal(new Link("Wetterdienst", "https://dwd.de"), Assert.Single(reopened.Links));
        var max = reopened.Personnel.Single(p => p.LastName == "Mustermann");
        Assert.Equal("Land 1", max.CallSign);
        Assert.Equal("01 71 / 1 23 45 67", max.Phone);
    }

    [Fact]
    public void Vehicles_round_trip_with_wache_callsign_and_seats()
    {
        var store = new MasterDataStore();
        var set = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
            },
        };
        store.Save(_path, set);

        var reopened = store.GetOrCreate(_path);

        Assert.Equal(set.Vehicles, reopened.Vehicles);
    }

    [Fact]
    public void Personnel_optional_fields_round_trip_as_null()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with
        {
            Personnel = new[] { new Person("Musterfrau", "Erika", null, null, null) },
        });

        var erika = store.GetOrCreate(_path).Personnel.Single();
        Assert.Null(erika.Role);
        Assert.Null(erika.CallSign);
        Assert.Null(erika.Phone);
    }

    [Fact]
    public void Personnel_come_back_name_sorted()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with
        {
            Personnel = new[]
            {
                new Person("Zieger", "Anna", null, null, null),
                new Person("Amsel", "Berta", null, null, null),
            },
        });

        var names = store.GetOrCreate(_path).Personnel.Select(p => p.LastName).ToList();
        Assert.Equal(new[] { "Amsel", "Zieger" }, names);
    }

    [Fact]
    public void Save_round_trips_an_added_and_a_removed_street()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with
        {
            Streets = new[] { new Street("Alt Str.", "FFB"), new Street("Bahnhofstr.", "FFB") },
        });

        var current = store.GetOrCreate(_path);
        var edited = current with { Streets = current.Streets.Skip(1).Append(new Street("Neu Str.", "Aich")).ToList() };
        store.Save(_path, edited);

        var reopened = store.GetOrCreate(_path);
        Assert.Contains(reopened.Streets, s => s.Name == "Neu Str." && s.District == "Aich");
        Assert.DoesNotContain(reopened.Streets, s => s.Name == "Alt Str.");
        Assert.Equal(2, reopened.Streets.Count);
    }

    [Fact]
    public void Save_round_trips_an_added_and_a_removed_link()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with
        {
            Links = new[] { new Link("Alt Link", "https://old.example"), new Link("Wetterdienst", "https://dwd.de") },
        });

        var current = store.GetOrCreate(_path);
        var edited = current with { Links = current.Links.Skip(1).Append(new Link("Neu Link", "https://new.example")).ToList() };
        store.Save(_path, edited);

        var reopened = store.GetOrCreate(_path);
        Assert.Contains(reopened.Links, l => l.Name == "Neu Link" && l.Url == "https://new.example");
        Assert.DoesNotContain(reopened.Links, l => l.Name == "Alt Link");
        Assert.Equal(2, reopened.Links.Count);
    }

    [Fact]
    public void Save_round_trips_a_checklist_reorder_and_delete()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with
        {
            ChecklistTemplateAufbau = Items("A", "B", "C"),
        });

        store.Save(_path, MasterDataSet.Empty with { ChecklistTemplateAufbau = Items("C", "A") });

        Assert.Equal(Items("C", "A"), store.GetOrCreate(_path).ChecklistTemplateAufbau);

        static IReadOnlyList<ChecklistTemplateItem> Items(params string[] texts) =>
            texts.Select(t => new ChecklistTemplateItem(t, false)).ToList();
    }

    [Fact]
    public void Save_round_trips_aufbau_and_abbau_independently_with_mandatory_flags()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with
        {
            ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("Fahrzeug prüfen", true) },
            ChecklistTemplateAbbau = new[] { new ChecklistTemplateItem("Material zählen", false) },
        });

        var reopened = store.GetOrCreate(_path);
        Assert.Equal(new ChecklistTemplateItem("Fahrzeug prüfen", true), Assert.Single(reopened.ChecklistTemplateAufbau));
        Assert.Equal(new ChecklistTemplateItem("Material zählen", false), Assert.Single(reopened.ChecklistTemplateAbbau));
    }

    [Fact]
    public void A_pre_split_database_widens_in_place_and_reads_everything_as_optional_aufbau()
    {
        // Simulate a database written before the Aufbau/Abbau split: the table exists but without
        // is_mandatory/kind. EnsureSchema must widen it in place rather than erroring on open.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE md_checklist_template (ordinal INTEGER PRIMARY KEY, text TEXT NOT NULL);
                INSERT INTO md_checklist_template (ordinal, text) VALUES (0, 'Altes Item');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var set = new MasterDataStore().GetOrCreate(_path);

        Assert.Equal(new ChecklistTemplateItem("Altes Item", false), Assert.Single(set.ChecklistTemplateAufbau));
        Assert.Empty(set.ChecklistTemplateAbbau);
    }

    [Fact]
    public void A_value_deleted_through_Save_stays_deleted()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with { Roles = new[] { "EL", "ZF" } });

        store.Save(_path, MasterDataSet.Empty with { Roles = new[] { "ZF" } });

        Assert.DoesNotContain("EL", store.GetOrCreate(_path).Roles);
    }

    [Fact]
    public void A_person_without_a_first_name_displays_as_the_last_name_alone()
        => Assert.Equal("Mustermann", new Person("Mustermann", "", null, null, null).DisplayName);

    [Fact]
    public void A_fresh_database_reads_the_default_settings()
    {
        var set = new MasterDataStore().GetOrCreate(_path);
        Assert.Equal(IncidentSettings.Defaults, set.Settings);
    }

    [Fact]
    public void Save_then_GetOrCreate_round_trips_settings()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with { Settings = new IncidentSettings(12, 33, 25, 18, 40, 4, 55) });

        Assert.Equal(new IncidentSettings(12, 33, 25, 18, 40, 4, 55), store.GetOrCreate(_path).Settings);
    }

    [Fact]
    public void A_missing_setting_key_falls_back_to_its_default()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with { Settings = new IncidentSettings(12, 33, 25, 18, 40, 4, 55) });

        // Simulate a store written before a setting existed: drop one row, which read must backfill.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM md_settings WHERE key = 'return_pressure_bar';";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var settings = store.GetOrCreate(_path).Settings;
        Assert.Equal(IncidentSettings.Defaults.ReturnPressureBar, settings.ReturnPressureBar);
        Assert.Equal(12, settings.IlsReminderIntervalMinutes); // the other keys are untouched
    }
}
