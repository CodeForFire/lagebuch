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
}
