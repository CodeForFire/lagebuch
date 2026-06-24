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

    [Fact]
    public void Scba_trupps_round_trip_with_readings_and_exit()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op);

        var active = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300,
            callSign: "FFB 1/40/1", maxDurationMinutes: 30, returnPressureBar: 60);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordScbaPressure(clock, active.Id, 240);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordScbaPressure(clock, active.Id, 180);

        var returned = incident.AddScbaTrupp(clock, "Sicherheitstrupp", "Huber / Mayr", 280);
        clock.Now = clock.Now.AddMinutes(8);
        incident.MarkScbaReturned(clock, returned.Id);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        Assert.Equal(2, loaded.ScbaTrupps.Count);
        var loadedActive = loaded.ScbaTrupps[0];
        Assert.Equal("Angriffstrupp", loadedActive.Designation);
        Assert.Equal("Müller / Schmidt", loadedActive.Members);
        Assert.Equal("FFB 1/40/1", loadedActive.CallSign);
        Assert.Equal(300, loadedActive.EntryPressure);
        Assert.Equal(30, loadedActive.MaxDurationMinutes);
        Assert.Equal(60, loadedActive.ReturnPressureBar);
        Assert.Equal(2, loadedActive.PressureReadings.Count);
        Assert.Equal(240, loadedActive.PressureReadings[0].Bar);
        Assert.Equal(180, loadedActive.LatestPressure);
        Assert.True(loadedActive.IsActive);

        var loadedReturned = loaded.ScbaTrupps[1];
        Assert.False(loadedReturned.IsActive);
        Assert.Equal(returned.ExitTime, loadedReturned.ExitTime);
    }
}
