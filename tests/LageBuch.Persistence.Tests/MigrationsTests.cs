using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class MigrationsTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"mig-{Guid.NewGuid():N}.fwincident");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Fresh_database_starts_at_version_zero()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Assert.Equal(0, Migrations.GetVersion(cn));
    }

    [Fact]
    public void Migrate_brings_database_to_current_version_and_is_idempotent()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(cn);
            Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn));
        }

        // re-open and migrate again: no error, same version
        using (var cn2 = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(cn2);
            Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));
        }
    }

    [Fact]
    public void Migrate_creates_the_incident_tables()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN " +
            "('incident_meta','checklist_items','etb_entries','role_assignments','force_units','audit_events');";
        Assert.Equal(6L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migrate_creates_the_incident_files_table()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='incident_files';";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migrate_adds_the_display_name_column_to_incident_files()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pragma_table_info('incident_files') WHERE name='display_name';";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migrate_adds_the_officer_count_column_and_the_force_unit_edits_table()
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "SELECT (SELECT count(*) FROM pragma_table_info('force_units') WHERE name='officer_count') + " +
            "       (SELECT count(*) FROM sqlite_master WHERE type='table' AND name='force_unit_edits');";
        Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void V12_force_units_upgrade_to_v13_with_officer_count_defaulting_to_zero()
    {
        // A database stamped at 12 carries force_units without officer_count and rows whose
        // Gesamtstärke predates the GF split — after migrating they must read as 0/x/x.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (12);" +
                """
                CREATE TABLE force_units (
                    id TEXT PRIMARY KEY,
                    ordinal INTEGER NOT NULL,
                    brigade TEXT NOT NULL,
                    call_sign TEXT,
                    personnel_count INTEGER NOT NULL,
                    scba_count INTEGER NOT NULL DEFAULT 0,
                    status TEXT,
                    notes TEXT
                );
                INSERT INTO force_units (id, ordinal, brigade, call_sign, personnel_count, scba_count) VALUES ('u1', 0, 'Aich', NULL, 6, 2);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText = "SELECT officer_count FROM force_units WHERE id='u1';";
        Assert.Equal(0L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public void V1_database_upgrades_to_v2_and_gains_scba_tables()
    {
        // Build a database stamped at version 1, before the SCBA tables existed.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (1);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Assert.Equal(1, Migrations.GetVersion(cn));
            Migrations.Migrate(cn);
            Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn));

            using var check = cn.CreateCommand();
            check.CommandText =
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN " +
                "('scba_trupps','scba_pressure_readings');";
            Assert.Equal(2L, (long)check.ExecuteScalar()!);
        }
    }

    [Fact]
    public void V8_database_upgrades_to_v9_and_gains_the_checklist_mandatory_and_kind_columns()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (8);" +
                "CREATE TABLE checklist_items (id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL, text TEXT NOT NULL, is_done INTEGER NOT NULL, note TEXT);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText = "SELECT count(*) FROM pragma_table_info('checklist_items') WHERE name IN ('is_mandatory','kind');";
        Assert.Equal(2L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public void V24_database_upgrades_to_v25_and_gains_the_involved_parties_table()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (24);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText =
            "SELECT count(*) FROM pragma_table_info('involved_parties') " +
            "WHERE name IN ('id','ordinal','name','phone','notes','created_by','created_at');";
        Assert.Equal(7L, (long)check.ExecuteScalar()!);
    }

    [Fact]
    public void V25_database_upgrades_to_v26_keeping_the_address_and_adding_blank_hausnummer_plz_and_ort()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (25);" +
                "CREATE TABLE incident_meta (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, state INTEGER NOT NULL, incident_number TEXT, " +
                "ils_number TEXT, keyword TEXT, street TEXT, district TEXT, status TEXT, closed_at TEXT, closed_by TEXT);" +
                "INSERT INTO incident_meta (id, started_at, state, street, district) VALUES ('m', '2026-06-22T09:00:00+02:00', 0, 'Hauptstr. 12', 'Puch');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText = "SELECT street, district, house_number, postal_code, place FROM incident_meta;";
        using var r = check.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("Hauptstr. 12", r.GetString(0));
        Assert.Equal("Puch", r.GetString(1));
        Assert.True(r.IsDBNull(2));
        Assert.True(r.IsDBNull(3));
        Assert.True(r.IsDBNull(4));
    }

    [Fact]
    public void V26_database_upgrades_to_v27_with_every_beteiligte_in_no_wohnung()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (26);" +
                "CREATE TABLE involved_parties (id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL, name TEXT NOT NULL, phone TEXT, " +
                "notes TEXT, created_by TEXT NOT NULL, created_at TEXT NOT NULL);" +
                "INSERT INTO involved_parties (id, ordinal, name, created_by, created_at) VALUES ('p', 0, 'Erika Beispiel', 'Muster', '2026-09-27T14:00:00+02:00');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText = "SELECT name, dwelling_id, dwelling_label FROM involved_parties;";
        using var r = check.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("Erika Beispiel", r.GetString(0));
        Assert.True(r.IsDBNull(1));
        Assert.True(r.IsDBNull(2));
    }

    [Fact]
    public void V27_database_upgrades_to_v28_with_no_beteiligte_aufenthaltsort_known()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (27);" +
                "CREATE TABLE involved_parties (id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL, name TEXT NOT NULL, phone TEXT, " +
                "notes TEXT, created_by TEXT NOT NULL, created_at TEXT NOT NULL, dwelling_id TEXT, dwelling_label TEXT);" +
                "INSERT INTO involved_parties (id, ordinal, name, created_by, created_at) VALUES ('p', 0, 'Erika Beispiel', 'Muster', '2026-09-27T14:00:00+02:00');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText = "SELECT name, whereabouts, whereabouts_at FROM involved_parties;";
        using var r = check.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("Erika Beispiel", r.GetString(0));
        Assert.True(r.IsDBNull(1));
        Assert.True(r.IsDBNull(2));
    }

    [Fact]
    public void V28_database_upgrades_to_v29_with_every_funktion_reporting_to_no_one()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (28);" +
                "CREATE TABLE role_assignments (id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL, role TEXT NOT NULL, person_name TEXT NOT NULL, " +
                "call_sign TEXT, from_time TEXT, to_time TEXT, section TEXT, phone TEXT);" +
                "INSERT INTO role_assignments (id, ordinal, role, person_name) VALUES ('r', 0, 'EL', 'Huber');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using var cn2 = SqliteConnectionFactory.OpenReadWrite(_path);
        Migrations.Migrate(cn2);
        Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn2));

        using var check = cn2.CreateCommand();
        check.CommandText = "SELECT role, person_name, reports_to_id FROM role_assignments;";
        using var r = check.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("EL", r.GetString(0));
        Assert.Equal("Huber", r.GetString(1));
        Assert.True(r.IsDBNull(2));
    }
}
