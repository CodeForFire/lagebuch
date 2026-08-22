using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class ClosedIncidentReadOnlyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ro-{Guid.NewGuid():N}.fwincident");
    private sealed class Clock : IClock { public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2)); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void SaveClosedIncident()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);
        new IncidentRepository().Save(_path, incident);
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
    public void Load_of_closed_incident_returns_readonly_domain_state()
    {
        SaveClosedIncident();
        var loaded = new IncidentRepository().Load(_path);
        Assert.Equal(IncidentState.Closed, loaded.State);
        Assert.Throws<IncidentClosedException>(
            () => loaded.AddForceUnit(new Clock(), new Domain.SessionOperator("Müller"), "FFB", 1));
    }
}
