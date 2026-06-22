using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

public class IncidentRoundTripTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rt-{Guid.NewGuid():N}.fwincident");
    private sealed class Clock : IClock { public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2)); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Open_incident_round_trips_all_fields()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, "Brand");
        incident.SetIncidentNumber(new IncidentNumber("B 4242"));
        incident.SetIlsNumber(IlsNumber.Parse("4242"));
        incident.SetAddress("Hauptstr. 12", "FFB");
        incident.SetStatus("aufgenommen");
        incident.SeedChecklist(new[] { "Blaulicht aus?", "Bei ILS gemeldet?" });
        incident.ToggleChecklistItem(incident.Checklist[0].Id);
        clock.Now = clock.Now.AddMinutes(5);
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Meldung", from: "ILS");
        incident.AssignRole("EL", "Müller", callSign: "FFB 12/1");
        incident.AddForceUnit("FFB", 12, callSign: "FFB 1/40/1");

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        Assert.Equal(incident.Id, loaded.Id);
        Assert.Equal(IncidentState.Open, loaded.State);
        Assert.Equal("B 4242", loaded.IncidentNumber!.Value);
        Assert.Equal("4242", loaded.IlsNumber!.Value);
        Assert.Equal("Hauptstr. 12", loaded.Street);
        Assert.Equal("FFB", loaded.District);
        Assert.Equal("aufgenommen", loaded.Status);
        Assert.Equal(2, loaded.Checklist.Count);
        Assert.True(loaded.Checklist[0].IsDone);
        Assert.Single(loaded.Journal);
        Assert.Equal(clock.Now, loaded.Journal[0].Timestamp);
        Assert.Equal("Müller (FFB 12/1)", loaded.Journal[0].EnteredBy);
        Assert.Equal("EL", loaded.Roles[0].Role);
        Assert.Equal(12, loaded.TotalPersonnel);
        Assert.Equal(incident.Audit.Count, loaded.Audit.Count);
    }

    [Fact]
    public void Closed_incident_round_trips_and_stays_closed()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.AddJournalEntry(clock, op, EtbDirection.Internal, "x");
        clock.Now = clock.Now.AddHours(2);
        incident.Close(clock, op);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        Assert.Equal(IncidentState.Closed, loaded.State);
        Assert.Equal(incident.ClosedAt, loaded.ClosedAt);
        Assert.Equal("Müller", loaded.ClosedBy);
        Assert.Throws<IncidentClosedException>(() => loaded.SetStatus("x"));
    }
}
