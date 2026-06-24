using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

public class MigrationForwardCompatTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fwd-{Guid.NewGuid():N}.fwincident");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Older_v0_database_upgrades_to_current_version()
    {
        // Simulate an older file: a bare DB with an explicit version 0 and no incident tables.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (0);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Assert.Equal(0, Migrations.GetVersion(cn));
            Migrations.Migrate(cn);
            Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn));

            using var check = cn.CreateCommand();
            check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='etb_entries';";
            Assert.Equal(1L, (long)check.ExecuteScalar()!);
        }
    }

    [Fact]
    public void Loading_a_pre_v2_incident_file_upgrades_and_does_not_crash()
    {
        // Arrange: write a normal incident, then degrade the file to a pre-SCBA (V1) state —
        // exactly what a file last saved by the old code looks like on disk.
        var clock = new Clock();
        var op = new Domain.SessionOperator("Müller", "FFB 12/1");
        var incident = Domain.Incident.Start(clock, op, "Brand");
        incident.AddForceUnit("FFB", 12);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "DROP TABLE scba_pressure_readings; DROP TABLE scba_trupps; " +
                "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (1);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // Act + Assert: opening the old file must upgrade it in place, not throw.
        var loaded = repo.Load(_path);

        Assert.Equal("Brand", loaded.Keyword);
        Assert.Equal(12, loaded.TotalPersonnel);
        Assert.Empty(loaded.ScbaTrupps);
    }

    [Fact]
    public void V2_scba_trupp_migrates_to_v3_as_a_started_trupp()
    {
        // Build a file with the V1+V2 schema, stamped at version 2, holding one V2-shaped trupp
        // (entry_time + entry_pressure, the pre-split columns).
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            using (var v1 = cn.CreateCommand())
            {
                v1.CommandText =
                    "CREATE TABLE schema_version (version INTEGER NOT NULL); INSERT INTO schema_version (version) VALUES (2);";
                v1.ExecuteNonQuery();
            }
            using (var v2 = cn.CreateCommand())
            {
                v2.CommandText = """
                    CREATE TABLE scba_trupps (
                        id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL, designation TEXT NOT NULL,
                        members TEXT NOT NULL, call_sign TEXT, task TEXT, entry_time TEXT NOT NULL,
                        entry_pressure INTEGER NOT NULL, max_duration_minutes INTEGER NOT NULL,
                        return_pressure_bar INTEGER NOT NULL, exit_time TEXT);
                    CREATE TABLE scba_pressure_readings (
                        id TEXT PRIMARY KEY, trupp_id TEXT NOT NULL, ordinal INTEGER NOT NULL,
                        reading_time TEXT NOT NULL, bar INTEGER NOT NULL);
                    INSERT INTO scba_trupps
                        (id, ordinal, designation, members, call_sign, task, entry_time, entry_pressure, max_duration_minutes, return_pressure_bar, exit_time)
                    VALUES ('11111111-1111-1111-1111-111111111111', 0, 'Angriffstrupp', 'Müller / Schmidt',
                            'FFB 1/40/1', NULL, '2026-06-22T09:03:00.0000000+02:00', 300, 30, 60, NULL);
                    """;
                v2.ExecuteNonQuery();
            }
        }
        SqliteConnection.ClearAllPools();

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(cn);
            Assert.Equal(Migrations.CurrentVersion, Migrations.GetVersion(cn));

            using var read = cn.CreateCommand();
            read.CommandText =
                "SELECT registered_at, start_time, start_pressure, pressure_control_interval_minutes FROM scba_trupps;";
            using var r = read.ExecuteReader();
            Assert.True(r.Read());
            // An existing V2 trupp was already under air: start == entry, pressure preserved.
            Assert.Equal(r.GetString(0), r.GetString(1));
            Assert.Equal(300, r.GetInt32(2));
            Assert.Equal(AtemschutzTrupp.DefaultPressureControlIntervalMinutes, r.GetInt32(3));
        }
    }

    private sealed class Clock : Domain.Time.IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }
}
