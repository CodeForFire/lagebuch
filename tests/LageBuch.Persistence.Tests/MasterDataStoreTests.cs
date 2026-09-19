using System.Diagnostics.CodeAnalysis;
using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class MasterDataStoreTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"md-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void GetOrCreate_returns_an_empty_set_for_a_fresh_database()
    {
        // The app ships with no seed, so a brand-new masterdata.db comes back empty -- populated
        // only by Save (the editor's Import), never by a compiled-in default.
        var set = MasterDataStore.GetOrCreate(_path);
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void GetOrCreate_is_idempotent_and_never_seeds()
    {
        Assert.True(MasterDataStore.GetOrCreate(_path).IsEmpty);
        Assert.True(MasterDataStore.GetOrCreate(_path).IsEmpty); // still empty on a second open
    }

    [Fact]
    public void Save_then_GetOrCreate_round_trips_every_category_including_personnel()
    {
        var set = MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "ZF" },
            UnitStatus = new[] { "Alarmiert" },
            ChecklistTemplates = ChecklistTemplate.AufbauAbbau(
                new[] { new ChecklistTemplateItem("Schritt 1", true), new ChecklistTemplateItem("Schritt 2", false) },
                new[] { new ChecklistTemplateItem("Abbauschritt", true) }),
            TruppTypes = new[] { new TruppType("Angriffstrupp") },
            Links = new[] { new Link("Wetterdienst", "https://dwd.de") },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
        };
        MasterDataStore.Save(_path, set);

        var reopened = MasterDataStore.GetOrCreate(_path);
        Assert.Equal(new[] { "EL", "ZF" }, reopened.Roles);
        Assert.Equal(
            new[] { new ChecklistTemplateItem("Schritt 1", true), new ChecklistTemplateItem("Schritt 2", false) },
            reopened.ChecklistTemplates[0].Items);
        Assert.Equal(new[] { new ChecklistTemplateItem("Abbauschritt", true) }, reopened.ChecklistTemplates[1].Items);
        Assert.Equal(new Link("Wetterdienst", "https://dwd.de"), Assert.Single(reopened.Links));
        var max = reopened.Personnel.Single(p => p.LastName == "Mustermann");
        Assert.Equal("Land 1", max.CallSign);
        Assert.Equal("01 71 / 1 23 45 67", max.Phone);
    }

    [Fact]
    public void Vehicles_round_trip_with_wache_callsign_and_seats()
    {
        var set = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
            },
        };
        MasterDataStore.Save(_path, set);

        var reopened = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(set.Vehicles, reopened.Vehicles);
    }

    [Fact]
    public void Vehicles_round_trip_the_zugfuehrer_flag()
    {
        var set = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB ELW 1", 4, HasZugfuehrer: true),
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            },
        };
        MasterDataStore.Save(_path, set);

        var reopened = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(set.Vehicles, reopened.Vehicles);
    }

    [Fact]
    public void A_pre_zugfuehrer_flag_database_widens_in_place_and_reads_existing_vehicles_as_false()
    {
        // Simulate a database written before the ZF flag existed: the table exists but without
        // has_zugfuehrer. EnsureSchema must widen it in place rather than erroring on open.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE md_vehicles (wache TEXT NOT NULL, call_sign TEXT NOT NULL, seats INTEGER NOT NULL DEFAULT 0);
                INSERT INTO md_vehicles (wache, call_sign, seats) VALUES ('FFB Wache 1', 'FFB 1/40/1', 9);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(new Vehicle("FFB Wache 1", "FFB 1/40/1", 9), Assert.Single(set.Vehicles));
    }

    [Fact]
    public void A_database_with_the_old_wachen_and_funkrufnamen_tables_opens_and_derives_from_vehicles()
    {
        // Simulate a database written while Wachen/Funkrufnamen were still their own lists. The
        // tables are dropped on open (no version marker, so every open re-checks) and the derived
        // lists come from md_vehicles alone -- an orphan entry no vehicle covers is gone.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE md_brigades (value TEXT NOT NULL);
                CREATE TABLE md_call_signs (value TEXT NOT NULL);
                INSERT INTO md_brigades (value) VALUES ('FFB Wache 1'), ('Alt-Wache');
                INSERT INTO md_call_signs (value) VALUES ('FFB 1/40/1'), ('Leitstelle');
                CREATE TABLE md_vehicles (wache TEXT NOT NULL, call_sign TEXT NOT NULL, seats INTEGER NOT NULL DEFAULT 0, has_zugfuehrer INTEGER NOT NULL DEFAULT 0);
                INSERT INTO md_vehicles (wache, call_sign, seats) VALUES ('FFB Wache 1', 'FFB 1/40/1', 9);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(new[] { "FFB Wache 1" }, set.Brigades);
        Assert.Equal(new[] { "FFB 1/40/1" }, set.RadioCallSigns);

        using var check = new SqliteConnection($"Data Source={_path}");
        check.Open();
        using var count = check.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('md_brigades', 'md_call_signs');";
        Assert.Equal(0L, (long)count.ExecuteScalar()!);
    }

    [Fact]
    public void Personnel_optional_fields_round_trip_as_null()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            Personnel = new[] { new Person("Musterfrau", "Erika", null, null, null) },
        });

        var erika = MasterDataStore.GetOrCreate(_path).Personnel.Single();
        Assert.Null(erika.Role);
        Assert.Null(erika.CallSign);
        Assert.Null(erika.Phone);
    }

    [Fact]
    public void Personnel_come_back_name_sorted()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            Personnel = new[]
            {
                new Person("Zieger", "Anna", null, null, null),
                new Person("Amsel", "Berta", null, null, null),
            },
        });

        var names = MasterDataStore.GetOrCreate(_path).Personnel.Select(p => p.LastName).ToList();
        Assert.Equal(new[] { "Amsel", "Zieger" }, names);
    }

    [Fact]
    public void Save_round_trips_an_added_and_a_removed_link()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            Links = new[] { new Link("Alt Link", "https://old.example"), new Link("Wetterdienst", "https://dwd.de") },
        });

        var current = MasterDataStore.GetOrCreate(_path);
        var edited = current with { Links = current.Links.Skip(1).Append(new Link("Neu Link", "https://new.example")).ToList() };
        MasterDataStore.Save(_path, edited);

        var reopened = MasterDataStore.GetOrCreate(_path);
        Assert.Contains(reopened.Links, l => l.Name == "Neu Link" && l.Url == "https://new.example");
        Assert.DoesNotContain(reopened.Links, l => l.Name == "Alt Link");
        Assert.Equal(2, reopened.Links.Count);
    }

    [Fact]
    public void Save_round_trips_a_checklist_reorder_and_delete()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            ChecklistTemplates = ChecklistTemplate.AufbauAbbau(Items("A", "B", "C"), null),
        });

        MasterDataStore.Save(_path, MasterDataSet.Empty with { ChecklistTemplates = ChecklistTemplate.AufbauAbbau(Items("C", "A"), null) });

        Assert.Equal(Items("C", "A"), MasterDataStore.GetOrCreate(_path).ChecklistTemplates[0].Items);

        static IReadOnlyList<ChecklistTemplateItem> Items(params string[] texts) =>
            texts.Select(t => new ChecklistTemplateItem(t, false)).ToList();
    }

    [Fact]
    public void Save_round_trips_aufbau_and_abbau_independently_with_mandatory_flags()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with
        {
            ChecklistTemplates = ChecklistTemplate.AufbauAbbau(
                new[] { new ChecklistTemplateItem("Fahrzeug prüfen", true) },
                new[] { new ChecklistTemplateItem("Material zählen", false) }),
        });

        var reopened = MasterDataStore.GetOrCreate(_path);
        Assert.Equal(new ChecklistTemplateItem("Fahrzeug prüfen", true), Assert.Single(reopened.ChecklistTemplates[0].Items));
        Assert.Equal(new ChecklistTemplateItem("Material zählen", false), Assert.Single(reopened.ChecklistTemplates[1].Items));
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

        var set = MasterDataStore.GetOrCreate(_path);

        // One list, not two: a kind with no rows produces no template, the same rule the V23
        // file migration and the legacy-JSON import follow.
        var template = Assert.Single(set.ChecklistTemplates);
        Assert.Equal("Aufbau", template.Title);
        Assert.Equal(new ChecklistTemplateItem("Altes Item", false), Assert.Single(template.Items));
    }

    [Fact]
    public void A_value_deleted_through_Save_stays_deleted()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with { Roles = new[] { "EL", "ZF" } });

        MasterDataStore.Save(_path, MasterDataSet.Empty with { Roles = new[] { "ZF" } });

        Assert.DoesNotContain("EL", MasterDataStore.GetOrCreate(_path).Roles);
    }

    [Fact]
    public void A_person_without_a_first_name_displays_as_the_last_name_alone()
        => Assert.Equal("Mustermann", new Person("Mustermann", string.Empty, null, null, null).DisplayName);

    [Fact]
    public void A_fresh_database_reads_the_default_settings()
    {
        var set = MasterDataStore.GetOrCreate(_path);
        Assert.Equal(IncidentSettings.Defaults, set.Settings);
    }

    [Fact]
    public void Save_then_GetOrCreate_round_trips_settings()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with { Settings = new IncidentSettings(12, 33, 55) });

        Assert.Equal(new IncidentSettings(12, 33, 55), MasterDataStore.GetOrCreate(_path).Settings);
    }

    [Fact]
    public void A_missing_setting_key_falls_back_to_its_default()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with { Settings = new IncidentSettings(12, 33, 55) });

        // Simulate a store written before a setting existed: drop one row, which read must backfill.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM md_settings WHERE key = 'return_pressure_bar';";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var settings = MasterDataStore.GetOrCreate(_path).Settings;
        Assert.Equal(IncidentSettings.Defaults.ReturnPressureBar, settings.ReturnPressureBar);
        Assert.Equal(12, settings.IlsReminderIntervalMinutes); // the other keys are untouched
    }

    // --- Trupp-Typen: the #398 widening and its one-time translation ---
    [Fact]
    public void A_pre_trupp_type_rules_database_widens_and_restores_what_the_old_rule_gave()
    {
        // Simulate a store written before the Staerke and Einsatzzeit moved onto the row, when
        // "CSA-Trupp" and "LPA-Trupp" were matched as literals at registration time. The ALTER
        // backfills everything as an ordinary Trupp; the one-time migration then restores the two
        // names' old values, trimmed and ignoring case exactly as the runtime rule did.
        WriteLegacyTruppTypes("Angriffstrupp", "CSA-Trupp", "LPA-Trupp", " csa-trupp ");

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(
            new[]
            {
                new TruppType("Angriffstrupp", 2, 30),
                new TruppType("CSA-Trupp", 3, 20),
                new TruppType("LPA-Trupp", 2, 60),
                new TruppType(" csa-trupp ", 3, 20),
            },
            set.TruppTypes);
    }

    [Fact]
    public void The_trupp_type_migration_runs_once_and_never_overwrites_a_later_edit()
    {
        // The whole reason the migration is gated on a marker rather than on the column being
        // absent. A brigade that decides its CSA-Trupp is two people on 45 minutes must keep that
        // across every subsequent launch.
        WriteLegacyTruppTypes("CSA-Trupp");
        var migrated = MasterDataStore.GetOrCreate(_path);
        Assert.Equal(new TruppType("CSA-Trupp", 3, 20), Assert.Single(migrated.TruppTypes));

        MasterDataStore.Save(_path, migrated with { TruppTypes = new[] { new TruppType("CSA-Trupp", 2, 45) } });

        Assert.Equal(
            new TruppType("CSA-Trupp", 2, 45),
            Assert.Single(MasterDataStore.GetOrCreate(_path).TruppTypes));
    }

    [Fact]
    public void A_fresh_database_is_marked_migrated_so_it_can_never_be_seeded_later()
    {
        // A new install has no pre-#398 data to translate. If the marker were not written here,
        // adding a CSA-Trupp by hand and reopening would silently rewrite its numbers.
        MasterDataStore.GetOrCreate(_path);
        MasterDataStore.Save(
            _path, MasterDataSet.Empty with { TruppTypes = new[] { new TruppType("CSA-Trupp", 2, 45) } });

        Assert.Equal(
            new TruppType("CSA-Trupp", 2, 45),
            Assert.Single(MasterDataStore.GetOrCreate(_path).TruppTypes));
    }

    [Fact]
    public void A_stored_einsatzzeit_no_countdown_can_run_is_clamped_on_read()
    {
        // Save can no longer write such a row, so this has to be planted directly -- the point is
        // a masterdata.db edited outside the app. Unclamped it would reach
        // AtemschutzTrupp.Register's ThrowIfNegativeOrZero and crash the Atemschutz form.
        //
        // The migration marker goes in too: this is a store that already carries the new columns
        // and was hand-edited afterwards, not a legacy one. Without it the one-time migration would
        // run and overwrite both rows, which is correct for legacy data but not what is under test.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE md_trupp_types (
                    value TEXT NOT NULL,
                    member_count INTEGER NOT NULL DEFAULT 2,
                    max_duration_minutes INTEGER NOT NULL DEFAULT 30);
                INSERT INTO md_trupp_types (value, member_count, max_duration_minutes) VALUES
                    ('Kaputt', 2, 0),
                    ('Lang', 2, 240);
                CREATE TABLE md_settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                INSERT INTO md_settings (key, value) VALUES ('trupp_type_defaults_migrated', 1);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var set = MasterDataStore.GetOrCreate(_path);

        // Floor only: the four-hour entry is left exactly as the brigade wrote it.
        Assert.Equal(new[] { 1, 240 }, set.TruppTypes.Select(t => t.MaxDurationMinutes));
    }

    [Fact]
    public void The_retired_einsatzzeit_settings_are_removed_from_an_existing_store()
    {
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE md_settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                INSERT INTO md_settings (key, value) VALUES
                    ('agt_max_duration_minutes', 35),
                    ('csa_max_duration_minutes', 22),
                    ('lpa_max_duration_minutes', 48),
                    ('return_pressure_bar', 55);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        Assert.Equal(55, MasterDataStore.GetOrCreate(_path).Settings.ReturnPressureBar);
        Assert.DoesNotContain(SettingKeys(), k => k.EndsWith("_max_duration_minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void The_migration_carries_over_the_einsatzzeiten_the_brigade_had_configured()
    {
        // The three settings were edited in Stammdaten -> Einstellungen; they were never constants.
        // Migrating with the shipped defaults instead would hand a Wehr that had shortened its
        // CSA-Trupp to 15 minutes a 20-minute countdown -- longer under air than it decided on.
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE md_trupp_types (value TEXT NOT NULL);
                INSERT INTO md_trupp_types (value) VALUES ('Angriffstrupp'), ('CSA-Trupp'), ('LPA-Trupp');
                CREATE TABLE md_settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                INSERT INTO md_settings (key, value) VALUES
                    ('agt_max_duration_minutes', 25),
                    ('csa_max_duration_minutes', 15),
                    ('lpa_max_duration_minutes', 45);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(
            new[]
            {
                new TruppType("Angriffstrupp", 2, 25),
                new TruppType("CSA-Trupp", 3, 15),
                new TruppType("LPA-Trupp", 2, 45),
            },
            set.TruppTypes);

        // ... and only then are the retired keys dropped.
        Assert.DoesNotContain(SettingKeys(), k => k.EndsWith("_max_duration_minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void The_migration_falls_back_per_key_for_a_store_that_never_overrode_them()
    {
        WriteLegacyTruppTypes("Angriffstrupp", "CSA-Trupp", "LPA-Trupp");

        var set = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(new[] { 30, 20, 60 }, set.TruppTypes.Select(t => t.MaxDurationMinutes));
    }

    /// <summary>
    /// The drift guard from #397, pointed at this store. A column added to the CREATE TABLE block
    /// without a matching AddColumnIfMissing line breaks every pre-existing database, silently --
    /// and no existing test catches it, because a fresh store is built straight from those CREATE
    /// statements and so always matches itself. Dropping every repairable column and reopening is
    /// what exercises the widening path a real user's file takes.
    /// </summary>
    [Fact]
    public void Every_repairable_column_is_restored_after_being_dropped()
    {
        MasterDataStore.GetOrCreate(_path);

        var repairable = new List<(string Table, string Column)>();
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            foreach (var table in Query(cn, "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'md\\_%' ESCAPE '\\';"))
            {
                // Only a nullable column or one with a default can be re-added by ALTER TABLE;
                // a primary key or a bare NOT NULL could not be, so it is not in scope here.
                foreach (var column in Query(
                    cn,
                    $"SELECT name FROM pragma_table_info('{table}') WHERE pk = 0 AND (\"notnull\" = 0 OR dflt_value IS NOT NULL);"))
                {
                    repairable.Add((table, column));
                }
            }

            DropColumns(cn, repairable);
        }

        SqliteConnection.ClearAllPools();
        Assert.NotEmpty(repairable);

        MasterDataStore.GetOrCreate(_path);

        using var reopened = new SqliteConnection($"Data Source={_path}");
        reopened.Open();
        foreach (var (table, column) in repairable)
        {
            Assert.Contains(column, Query(reopened, $"SELECT name FROM pragma_table_info('{table}');"));
        }
    }

    /// <summary>Writes a md_trupp_types in its pre-#398 shape: names only, no rules.</summary>
    private void WriteLegacyTruppTypes(params string[] names)
    {
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var create = cn.CreateCommand();
            create.CommandText = "CREATE TABLE md_trupp_types (value TEXT NOT NULL);";
            create.ExecuteNonQuery();

            foreach (var name in names)
            {
                using var insert = cn.CreateCommand();
                insert.CommandText = "INSERT INTO md_trupp_types (value) VALUES ($v);";
                insert.Parameters.AddWithValue("$v", name);
                insert.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();
    }

    private List<string> SettingKeys()
    {
        using var cn = new SqliteConnection($"Data Source={_path}");
        cn.Open();
        return Query(cn, "SELECT key FROM md_settings;");
    }

    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Test-only: the table and column names come from this database's own sqlite_master/pragma output, never from user input.")]
    private static void DropColumns(SqliteConnection cn, IEnumerable<(string Table, string Column)> columns)
    {
        foreach (var (table, column) in columns)
        {
            using var drop = cn.CreateCommand();
            drop.CommandText = $"ALTER TABLE {table} DROP COLUMN {column};";
            drop.ExecuteNonQuery();
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Test-only: the SQL is built from schema identifiers this test just read back out of the same database, never from user input.")]
    private static List<string> Query(SqliteConnection cn, string sql)
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
}
