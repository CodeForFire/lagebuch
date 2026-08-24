using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Domain;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

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
        incident.SetIncidentNumber(new IncidentNumber("B 1.2 260715 4242"));
        incident.SetAddress("Hauptstr. 12", "FFB");
        incident.SetStatus("aufgenommen");
        // Item 0 stays mandatory-and-unchecked deliberately: toggling it would complete Aufbau and
        // fire an extra ETB system entry, which this test isn't about — that's covered in
        // IncidentOperationsTests. Toggling the optional item still proves IsDone round-trips.
        incident.SeedChecklist(
            new[] { ("Blaulicht aus?", true), ("Bei ILS gemeldet?", false) },
            new[] { ("Fahrzeug abgerüstet?", true) });
        incident.ToggleChecklistItem(clock, op, incident.ChecklistAufbau[1].Id);
        clock.Now = clock.Now.AddMinutes(5);
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Meldung", from: "ILS");
        var journalTime = clock.Now;
        incident.AssignRole(clock, op, "EL", "Müller", callSign: "FFB 12/1", from: clock.Now,
            section: "Abschnitt Nord", phone: "01 71 / 1 23 45 67");
        var assignedAt = clock.Now;
        incident.AddForceUnit(clock, op, "FFB", 12, callSign: "FFB 1/40/1", status: "Im Einsatz",
            notes: "über DLK angefordert", scbaCount: 6, officerCount: 1);
        clock.Now = clock.Now.AddMinutes(3);
        incident.UpdateForceStrength(clock, op, incident.Forces[0].Id,
            officerCount: 1, personnelCount: 14, scbaCount: 7);
        clock.Now = clock.Now.AddMinutes(2);
        incident.AddTask(clock, op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        var taskDueAt = clock.Now.AddMinutes(5);
        incident.AddTask(clock, op, "Nachfordern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        incident.SetTaskCompleted(incident.Tasks[1].Id, true, clock, op);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        Assert.Equal(incident.Id, loaded.Id);
        Assert.Equal(IncidentState.Open, loaded.State);
        Assert.Equal("B 1.2 260715 4242", loaded.IncidentNumber!.Value);
        Assert.Equal("Hauptstr. 12", loaded.Street);
        Assert.Equal("FFB", loaded.District);
        Assert.Equal("aufgenommen", loaded.Status);
        Assert.Equal(2, loaded.ChecklistAufbau.Count);
        Assert.False(loaded.ChecklistAufbau[0].IsDone);
        Assert.True(loaded.ChecklistAufbau[0].IsMandatory);
        Assert.True(loaded.ChecklistAufbau[1].IsDone);
        Assert.False(loaded.ChecklistAufbau[1].IsMandatory);
        Assert.True(Assert.Single(loaded.ChecklistAbbau).IsMandatory);
        // Journal[0] is the automatic "Einsatz begonnen" entry from Incident.Start; the manual one
        // follows it in chronological order, then the automatic entries for the role assignment,
        // the recorded unit and the strength correction -- which is also the proof that generated
        // entries survive the round trip.
        Assert.Equal(
            new[]
            {
                "Einsatz begonnen",
                "Meldung",
                "Funktion EL zugewiesen: Müller",
                "Einheit aufgenommen: FFB (FFB 1/40/1), Stärke 1/11/12, davon 6 AGT — Status: Im Einsatz",
                "FFB (FFB 1/40/1): Stärke 1/11/12 → 1/13/14, davon AGT 6 → 7",
            },
            loaded.Journal.Select(e => e.Text));
        // The direction rides along too: generated lines are System, the manual one keeps Incoming.
        Assert.Equal(
            new[] { EtbDirection.System, EtbDirection.Incoming, EtbDirection.System, EtbDirection.System, EtbDirection.System },
            loaded.Journal.Select(e => e.Direction));
        Assert.Equal("FFB 1/40/1", loaded.Journal[3].To);
        Assert.Equal("FFB 1/40/1", loaded.Journal[4].From);
        Assert.Equal(journalTime, loaded.Journal[1].Timestamp);
        Assert.Equal("Müller (FFB 12/1)", loaded.Journal[1].EnteredBy);
        Assert.Equal("EL", loaded.Roles[0].Role);
        Assert.Equal("Abschnitt Nord", loaded.Roles[0].Section);
        Assert.Equal("01 71 / 1 23 45 67", loaded.Roles[0].Phone);
        Assert.Equal(assignedAt, loaded.Roles[0].From);
        Assert.Null(loaded.Roles[0].To);
        // The strength was corrected after entry: the loaded unit carries the corrected numbers
        // plus the retained prior state (#76).
        Assert.Equal(14, loaded.TotalPersonnel);
        Assert.Equal(7, loaded.TotalScba);
        var force = loaded.Forces[0];
        Assert.Equal("Im Einsatz", force.Status);
        Assert.Equal("über DLK angefordert", force.Notes);
        Assert.Equal((1, 14, 7), (force.OfficerCount, force.PersonnelCount, force.ScbaCount));
        Assert.Equal("1/13/14", force.StrengthText);
        var strengthEdit = Assert.Single(force.Edits);
        Assert.Equal((1, 12, 6), (strengthEdit.PreviousOfficerCount, strengthEdit.PreviousPersonnelCount, strengthEdit.PreviousScbaCount));
        Assert.Equal(op.Display, strengthEdit.EditedBy);
        Assert.Equal(incident.Audit.Count, loaded.Audit.Count);
        Assert.Equal(2, loaded.Tasks.Count);
        Assert.Equal("Tür sichern", loaded.Tasks[0].Text);
        Assert.Equal("FFB 1/44/1", loaded.Tasks[0].Assignee);
        Assert.Equal(taskDueAt, loaded.Tasks[0].DueAt);
        Assert.False(loaded.Tasks[0].IsCompleted);
        Assert.True(loaded.Tasks[1].IsCompleted);
        Assert.NotNull(loaded.Tasks[1].CompletedAt);
    }

    [Fact]
    public void An_edited_status_and_bemerkung_survive_a_round_trip()
    {
        // The point of making them editable is that the corrected value is what ends up in the
        // Einsatz record -- so the edit has to reach disk, not just the in-memory unit.
        var clock = new Clock();
        var op = new Domain.SessionOperator("Müller", "FFB 12/1");
        var incident = Domain.Incident.Start(clock, op, "Brand");
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 9, "FFB 1/40/1", "Alarmiert", null, 4);

        incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", "Innenangriff");

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        var reloaded = Assert.Single(loaded.Forces);
        Assert.Equal("Im Einsatz", reloaded.Status);
        Assert.Equal("Innenangriff", reloaded.Notes);
        // Identity and the descriptive fields ride along unchanged.
        Assert.Equal(unit.Id, reloaded.Id);
        Assert.Equal(9, reloaded.PersonnelCount);
        Assert.Equal(4, reloaded.ScbaCount);
    }

    [Fact]
    public void An_edited_etb_entry_and_its_history_survive_a_round_trip()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, "Brand");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung", from: "ILS");
        clock.Now = clock.Now.AddMinutes(5);
        var editor = new SessionOperator("Schmidt");
        incident.EditJournalEntry(clock, editor, entry.Id, "Lagemeldung korrigiert");

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        var reloaded = loaded.Journal.Single(e => e.Id == entry.Id);
        Assert.Equal("Lagemeldung korrigiert", reloaded.Text);
        var historyEntry = Assert.Single(reloaded.Edits);
        Assert.Equal("Lagemeldung", historyEntry.PreviousText);
        Assert.Equal("Schmidt", historyEntry.EditedBy);
        Assert.Equal(clock.Now, historyEntry.EditedAt);
        // An entry with no edits still round-trips with an empty (not null) history.
        Assert.Empty(loaded.Journal.Single(e => e.Text == "Einsatz begonnen").Edits);
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

    [Fact]
    public void Attached_files_metadata_round_trips()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        var brand = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 2048);
        incident.RenameFile(brand.Id, "Küchenbrand");
        clock.Now = clock.Now.AddMinutes(1);
        incident.AddFile(clock, op, "bericht.pdf", "application/pdf", 4096);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        Assert.Equal(2, loaded.Files.Count);
        Assert.Equal("brand.jpg", loaded.Files[0].FileName);
        Assert.Equal("Küchenbrand", loaded.Files[0].DisplayName);
        Assert.Equal("image/jpeg", loaded.Files[0].ContentType);
        Assert.Equal(2048, loaded.Files[0].SizeBytes);
        Assert.Equal("bericht.pdf", loaded.Files[1].FileName);
        // Never renamed -- display name still defaults to the original file name.
        Assert.Equal("bericht.pdf", loaded.Files[1].DisplayName);
        // The auto ETB entries land alongside the manual ones, same as every other module.
        Assert.Contains("Datei hinzugefügt: brand.jpg", loaded.Journal.Select(e => e.Text));
        Assert.Contains("Datei hinzugefügt: bericht.pdf", loaded.Journal.Select(e => e.Text));
    }

    [Fact]
    public void A_file_row_written_before_display_name_existed_falls_back_to_the_file_name()
    {
        // Simulates a .fwincident saved by a build before this column existed: no code path in this
        // build ever writes a NULL display_name, so the only way to reproduce that state is a raw
        // UPDATE against the column directly, mirroring how a genuinely older file would look.
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 2048);
        var repo = new IncidentRepository();
        repo.Save(_path, incident);

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "UPDATE incident_files SET display_name = NULL;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var loaded = repo.Load(_path);

        Assert.Equal("brand.jpg", Assert.Single(loaded.Files).DisplayName);
    }

    [Fact]
    public void Incident_timers_round_trip()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.UpsertTimer("ils-reminder", clock.Now, intervalMinutes: 15, recurringIntervalMinutes: 30, isRunning: true);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        var loaded = repo.Load(_path);

        var timer = loaded.FindTimer("ils-reminder");
        Assert.NotNull(timer);
        Assert.Equal(clock.Now, timer!.CycleAnchor);
        Assert.Equal(15, timer.IntervalMinutes);
        Assert.Equal(30, timer.RecurringIntervalMinutes);
        Assert.True(timer.IsRunning);
    }

    [Fact]
    public void A_removed_unit_and_its_edit_trail_do_not_survive_a_round_trip()
    {
        // The repository rewrites the incident wholesale on every save, so a unit removed from the
        // domain simply is not re-inserted — this pins that its row and Wert-Historie really go.
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddForceUnit(clock, op, "FFB Wache 1", 6, callSign: "FFB 1/40/1", scbaCount: 2, officerCount: 1);
        incident.UpdateForceStrength(clock, op, incident.Forces[0].Id, officerCount: 1, personnelCount: 9, scbaCount: 4);

        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        incident.RemoveForceUnit(clock, op, incident.Forces[0].Id);
        repo.Save(_path, incident);

        var loaded = repo.Load(_path);
        Assert.Empty(loaded.Forces);
        Assert.Equal((0, 0), (loaded.TotalPersonnel, loaded.TotalScba));
    }

    [Fact]
    public void V13_file_gains_an_empty_tasks_table_and_still_loads()
    {
        // Build a current (V14) file, then roll the marker back and drop the new table to simulate
        // a file written by the previous app version.
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddTask(clock, op, "Alt", null, TaskImportance.Medium, TaskUrgency.Medium, 15);
        new IncidentRepository().Save(_path, incident);

        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE schema_version SET version = 13; DROP TABLE IF EXISTS incident_tasks;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var loaded = new IncidentRepository().Load(_path);

        Assert.Empty(loaded.Tasks);           // old file has no tasks — loads cleanly, migrates to V14
        Assert.Equal("Brand", loaded.Keyword);
    }
}
