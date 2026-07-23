using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

// TryReadState backs the Home overview's closed marker: it must report an incident's lifecycle state
// cheaply, tolerate unreadable files, and -- crucially -- never write to the file it inspects.
public class IncidentStateProbeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"probe-{Guid.NewGuid():N}.fwincident");
    private sealed class Clock : IClock { public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2)); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Save(bool closed)
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        if (closed) incident.Close(clock, op);
        new IncidentRepository().Save(_path, incident);
    }

    [Fact]
    public void Open_incident_reads_as_Open()
    {
        Save(closed: false);
        Assert.Equal(IncidentState.Open, new IncidentRepository().TryReadState(_path));
    }

    [Fact]
    public void Closed_incident_reads_as_Closed()
    {
        Save(closed: true);
        Assert.Equal(IncidentState.Closed, new IncidentRepository().TryReadState(_path));
    }

    [Fact]
    public void Missing_file_reads_as_null()
    {
        Assert.Null(new IncidentRepository().TryReadState(_path));
    }

    [Fact]
    public void Garbage_file_reads_as_null_instead_of_throwing()
    {
        File.WriteAllText(_path, "this is not a sqlite database");
        Assert.Null(new IncidentRepository().TryReadState(_path));
    }

    [Fact]
    public void Probing_does_not_modify_the_file()
    {
        Save(closed: false);
        var before = File.ReadAllBytes(_path);

        new IncidentRepository().TryReadState(_path);

        Assert.Equal(before, File.ReadAllBytes(_path));
    }
}
