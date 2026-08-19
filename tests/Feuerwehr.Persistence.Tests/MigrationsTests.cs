using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

public class MigrationsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mig-{Guid.NewGuid():N}.fwincident");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
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
}
