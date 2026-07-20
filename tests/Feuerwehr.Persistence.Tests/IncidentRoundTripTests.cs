using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Domain;
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
        incident.AssignRole("EL", "Müller", callSign: "FFB 12/1", from: clock.Now,
            section: "Abschnitt Nord", phone: "01 71 / 1 23 45 67");
        incident.AddForceUnit("FFB", 12, callSign: "FFB 1/40/1", status: "Im Einsatz",
            notes: "über DLK angefordert", scbaCount: 6);

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
        // Journal[0] is the automatic "Einsatz begonnen" entry from Incident.Start; the
        // manual one follows it in chronological order.
        Assert.Equal(new[] { "Einsatz begonnen", "Meldung" }, loaded.Journal.Select(e => e.Text));
        Assert.Equal(clock.Now, loaded.Journal[1].Timestamp);
        Assert.Equal("Müller (FFB 12/1)", loaded.Journal[1].EnteredBy);
        Assert.Equal("EL", loaded.Roles[0].Role);
        Assert.Equal("Abschnitt Nord", loaded.Roles[0].Section);
        Assert.Equal("01 71 / 1 23 45 67", loaded.Roles[0].Phone);
        Assert.Equal(clock.Now, loaded.Roles[0].From);
        Assert.Null(loaded.Roles[0].To);
        Assert.Equal(12, loaded.TotalPersonnel);
        Assert.Equal(6, loaded.TotalScba);
        Assert.Equal("Im Einsatz", loaded.Forces[0].Status);
        Assert.Equal("über DLK angefordert", loaded.Forces[0].Notes);
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
    public void Scba_trupps_round_trip_across_all_states()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op);

        // Active trupp: registered, started under air, two pressure readings.
        var active = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            callSign: "FFB 1/40/1", maxDurationMinutes: 30, returnPressureBar: 60,
            pressureControlIntervalMinutes: 5);
        clock.Now = clock.Now.AddMinutes(3);
        incident.StartScbaTrupp(clock, active.Id, 300);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordScbaPressure(clock, active.Id, 240);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordScbaPressure(clock, active.Id, 180);

        // Returned trupp: started then came back.
        var returned = incident.AddScbaTrupp(clock, "Sicherheitstrupp", TruppMember.Crew("Huber", "Mayr"));
        incident.StartScbaTrupp(clock, returned.Id, 280);
        clock.Now = clock.Now.AddMinutes(8);
        incident.MarkScbaReturned(clock, returned.Id);

        // CSA trupp: three people, to prove the crew size round-trips rather than being assumed.
        incident.AddScbaTrupp(clock, AtemschutzTrupp.ChemicalTruppDesignation,
            TruppMember.Crew("Berger", "Frank", "Lang"));

        // Waiting trupp: registered only, never started.
        var waiting = incident.AddScbaTrupp(clock, "Wassertrupp", TruppMember.Crew("Bauer", "Klein"));
        var waitingId = waiting.Id;

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        Assert.Equal(4, loaded.ScbaTrupps.Count);

        var loadedActive = loaded.ScbaTrupps[0];
        Assert.Equal("Angriffstrupp", loadedActive.Designation);
        // Crew survives as addressable members in position order, not as a re-parsed string.
        Assert.Equal(new[] { TruppRole.Truppfuehrer, TruppRole.Truppmann },
            loadedActive.Members.Select(m => m.Role));
        Assert.Equal(new[] { "Müller", "Schmidt" }, loadedActive.Members.Select(m => m.Name));

        var loadedCsa = loaded.ScbaTrupps[2];
        Assert.Equal(AtemschutzTrupp.ChemicalTruppDesignation, loadedCsa.Designation);
        Assert.Equal(3, loadedCsa.Members.Count);
        Assert.Equal("Berger / Frank / Lang", loadedCsa.MembersDisplay);
        Assert.Equal("FFB 1/40/1", loadedActive.CallSign);
        Assert.Equal(active.StartTime, loadedActive.StartTime);
        Assert.Equal(300, loadedActive.StartPressure);
        Assert.Equal(30, loadedActive.MaxDurationMinutes);
        Assert.Equal(60, loadedActive.ReturnPressureBar);
        Assert.Equal(5, loadedActive.PressureControlIntervalMinutes);
        Assert.Equal(2, loadedActive.PressureReadings.Count);
        Assert.Equal(180, loadedActive.LatestPressure);
        Assert.True(loadedActive.IsActive);

        var loadedReturned = loaded.ScbaTrupps[1];
        Assert.True(loadedReturned.IsReturned);
        Assert.Equal(returned.ExitTime, loadedReturned.ExitTime);

        var loadedWaiting = loaded.ScbaTrupps[3];
        Assert.Equal(waitingId, loadedWaiting.Id);
        Assert.True(loadedWaiting.IsWaiting);
        Assert.Null(loadedWaiting.StartTime);
        Assert.Null(loadedWaiting.StartPressure);
    }
}
