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

    private sealed class Clock : Domain.Time.IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }
}
