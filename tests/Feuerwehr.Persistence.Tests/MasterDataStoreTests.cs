using Feuerwehr.Persistence.MasterData;
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
            ChecklistTemplate = new[] { "Schritt 1", "Schritt 2" },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
        };
        store.Save(_path, set);

        var reopened = store.GetOrCreate(_path);
        Assert.Equal(new[] { "EL", "ZF" }, reopened.Roles);
        Assert.Equal(new[] { "B", "THL" }, reopened.Einsatzarten);
        Assert.Equal(new[] { "Schritt 1", "Schritt 2" }, reopened.ChecklistTemplate);
        Assert.Contains(reopened.Streets, s => s.Name == "Bahnhofstr." && s.District == "FFB");
        var max = reopened.Personnel.Single(p => p.LastName == "Mustermann");
        Assert.Equal("Land 1", max.CallSign);
        Assert.Equal("01 71 / 1 23 45 67", max.Phone);
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
    public void Save_round_trips_a_checklist_reorder_and_delete()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with { ChecklistTemplate = new[] { "A", "B", "C" } });

        store.Save(_path, MasterDataSet.Empty with { ChecklistTemplate = new[] { "C", "A" } });

        Assert.Equal(new[] { "C", "A" }, store.GetOrCreate(_path).ChecklistTemplate);
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
        store.Save(_path, MasterDataSet.Empty with { Settings = new IncidentSettings(12, 25, 18, 40, 4, 55) });

        Assert.Equal(new IncidentSettings(12, 25, 18, 40, 4, 55), store.GetOrCreate(_path).Settings);
    }

    [Fact]
    public void A_missing_setting_key_falls_back_to_its_default()
    {
        var store = new MasterDataStore();
        store.Save(_path, MasterDataSet.Empty with { Settings = new IncidentSettings(12, 25, 18, 40, 4, 55) });

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
