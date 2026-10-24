using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class ClosedIncidentReadOnlyTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"ro-{Guid.NewGuid():N}.lagebuch");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        // The write-protected test leaves its -wal/-shm behind: SQLite cannot checkpoint a file
        // it may not write. File.Delete is a no-op for a missing file.
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }

    private void SaveClosedIncident()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);
        IncidentRepository.Save(_path, incident);
    }

    private void AssertNoSidecars()
    {
        Assert.False(File.Exists(_path + "-wal"), "-wal left beside the file");
        Assert.False(File.Exists(_path + "-shm"), "-shm left beside the file");
    }

    private void SetWriteProtected(bool on)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(_path, on ? FileAttributes.ReadOnly : FileAttributes.Normal);
        }
        else
        {
            File.SetUnixFileMode(_path, on ? UnixFileMode.UserRead : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void ReadOnly_connection_rejects_writes()
    {
        SaveClosedIncident();
        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE incident_meta SET status='hacked';";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    [Fact]
    public void Closing_a_read_only_connection_leaves_no_wal_or_shm_beside_the_file()
    {
        // A connection opened with SQLITE_OPEN_READONLY may never checkpoint, so when it is the
        // last one to close it leaves an empty -wal and a 32 KB -shm behind for good.
        SaveClosedIncident();
        using (var cn = SqliteConnectionFactory.OpenReadOnly(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT state FROM incident_meta;";
            cmd.ExecuteScalar();
        }

        AssertNoSidecars();
    }

    [Fact]
    public void Reading_the_state_for_the_overview_leaves_no_wal_or_shm_beside_the_file()
    {
        // Home asks this of every recent file, so a leak here litters the user's Einsatz folder.
        SaveClosedIncident();

        Assert.Equal(IncidentState.Closed, IncidentRepository.TryReadState(_path));
        AssertNoSidecars();
    }

    [Fact]
    public void Loading_a_closed_incident_leaves_no_wal_or_shm_beside_the_file()
    {
        SaveClosedIncident();

        IncidentRepository.Load(_path);
        AssertNoSidecars();
    }

    [Fact]
    public void A_write_protected_file_still_shows_its_state_in_the_overview()
    {
        SaveClosedIncident();
        SetWriteProtected(true);
        try
        {
            Assert.Equal(IncidentState.Closed, IncidentRepository.TryReadState(_path));
        }
        finally
        {
            SetWriteProtected(false);
        }
    }

    [Fact]
    public void Load_of_closed_incident_returns_readonly_domain_state()
    {
        SaveClosedIncident();
        var loaded = IncidentRepository.Load(_path);
        Assert.Equal(IncidentState.Closed, loaded.State);
        Assert.Throws<IncidentClosedException>(
            () => loaded.AddForceUnit(new Clock(), new Domain.SessionOperator("Müller"), "FFB", 1));
    }

    [Fact]
    public void A_closed_incident_keeps_its_involved_parties_and_rejects_new_ones()
    {
        var clock = new Clock();
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op);
        incident.AddInvolvedParty(clock, op, "Erika Beispiel", "0171 0000001", null);
        incident.Close(clock, op);
        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);

        Assert.Equal("Erika Beispiel", Assert.Single(loaded.InvolvedParties).Name);
        Assert.Throws<IncidentClosedException>(
            () => loaded.AddInvolvedParty(clock, op, "Max Beispiel", null, null));
    }
}
