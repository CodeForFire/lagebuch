using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class IncidentRepositorySaveTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"save-{Guid.NewGuid():N}.fwincident");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Save_writes_meta_and_journal_rows()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, "Brand");
        incident.SetIncidentNumber(new IncidentNumber("B 1.2 260715 4242"));
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Meldung", from: "ILS");

        IncidentRepository.Save(_path, incident);

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT incident_number FROM incident_meta;";
        Assert.Equal("B 1.2 260715 4242", (string)cmd.ExecuteScalar()!);

        // Two rows: the automatic "Einsatz begonnen" entry from Incident.Start plus the
        // manual one above.
        cmd.CommandText = "SELECT count(*) FROM etb_entries;";
        Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Save_writes_one_etb_entry_edits_row_per_edit()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, "Brand");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung");
        incident.EditJournalEntry(clock, op, entry.Id, "Erste Korrektur");
        incident.EditJournalEntry(clock, op, entry.Id, "Zweite Korrektur");

        IncidentRepository.Save(_path, incident);

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM etb_entry_edits WHERE entry_id = $id;";
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Legacy_ils_number_loads_as_the_incident_number()
    {
        var clock = new Clock();

        IncidentRepository.Save(_path, Incident.Start(clock, new SessionOperator("Müller")));

        // Simulate a file written before the unification: the number lived in ils_number and
        // incident_number was empty.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "UPDATE incident_meta SET incident_number = NULL, ils_number = '4711';";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        Assert.Equal("4711", IncidentRepository.Load(_path).IncidentNumber!.Value);
    }

    [Fact]
    public void Save_is_idempotent_overwrite_not_append()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);

        IncidentRepository.Save(_path, incident);
        IncidentRepository.Save(_path, incident);

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM incident_meta;";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    // The incremental Save (issue #167 P2 follow-up) upserts by id instead of delete-all/insert-all,
    // so a second save must only touch the row that actually changed — an unrelated row, and the
    // append-only audit log, must survive untouched rather than being rewritten with fresh content.
    [Fact]
    public void Save_twice_after_editing_one_force_unit_leaves_the_other_and_the_audit_log_untouched()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var edited = incident.AddForceUnit(clock, op, "FFB 1", 6);
        var untouched = incident.AddForceUnit(clock, op, "FFB 2", 6);
        IncidentRepository.Save(_path, incident);
        var auditBefore = incident.Audit.Select(a => a.Action).ToList();

        incident.UpdateForceUnit(clock, op, edited.Id, status: "Im Einsatz", notes: null);
        IncidentRepository.Save(_path, incident);

        var reloaded = IncidentRepository.Load(_path);
        Assert.Equal(2, reloaded.Forces.Count);
        Assert.Equal("Im Einsatz", reloaded.Forces.Single(f => f.Id == edited.Id).Status);
        Assert.Equal("FFB 2", reloaded.Forces.Single(f => f.Id == untouched.Id).Brigade);
        Assert.Null(reloaded.Forces.Single(f => f.Id == untouched.Id).Status);

        // Every audit line from before the second save is still there, in the same order, plus
        // whatever the edit itself logged — none of the earlier ones were rewritten or reordered.
        var auditAfter = reloaded.Audit.Select(a => a.Action).ToList();
        Assert.Equal(auditBefore, auditAfter.Take(auditBefore.Count));
        Assert.Equal(incident.Audit.Count, auditAfter.Count);
    }

    [Fact]
    public void Removing_a_force_unit_then_saving_deletes_its_row()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var kept = incident.AddForceUnit(clock, op, "FFB 1", 6);
        var removed = incident.AddForceUnit(clock, op, "FFB 2", 6);
        IncidentRepository.Save(_path, incident);

        incident.RemoveForceUnit(clock, op, removed.Id);
        IncidentRepository.Save(_path, incident);

        var reloaded = IncidentRepository.Load(_path);
        Assert.Equal(new[] { kept.Id }, reloaded.Forces.Select(f => f.Id));

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM force_units;";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    // A single larger, incrementally-saved incident, to exercise the upsert/tail-append paths at a
    // scale closer to a real multi-hour Einsatz than the handful-of-rows fixtures above.
    [Fact]
    public void Round_trips_a_larger_incrementally_saved_incident()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);

        for (var i = 0; i < 40; i++)
        {
            incident.AddJournalEntry(clock, op, EtbDirection.Internal, $"Meldung {i}");
            if (i % 5 == 0)
            {
                // Saved partway through, mirroring how a real session calls Save() after every
                // mutation rather than once at the end.
                IncidentRepository.Save(_path, incident);
            }
        }

        var units = Enumerable.Range(0, 15)
            .Select(i => incident.AddForceUnit(clock, op, $"FFB {i}", personnelCount: 6))
            .ToList();
        IncidentRepository.Save(_path, incident);

        foreach (var unit in units.Take(5))
        {
            incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 1, personnelCount: 7, scbaCount: 2);
        }

        IncidentRepository.Save(_path, incident);

        var reloaded = IncidentRepository.Load(_path);

        // 40 manual + the automatic "Einsatz begonnen" + one per AddForceUnit ("Einheit
        // aufgenommen: …") + one per UpdateForceStrength ("… Stärke … → …") — both log their own
        // ETB line, same as the manual entries above.
        Assert.Equal(61, reloaded.Journal.Count);
        Assert.Equal(incident.Journal.Select(e => e.Text), reloaded.Journal.Select(e => e.Text));
        Assert.Equal(15, reloaded.Forces.Count);
        Assert.All(reloaded.Forces.Take(5), f => Assert.Equal(7, f.PersonnelCount));
        Assert.All(reloaded.Forces.Skip(5), f => Assert.Equal(6, f.PersonnelCount));
        foreach (var unit in units.Take(5))
        {
            Assert.Single(reloaded.Forces.Single(f => f.Id == unit.Id).Edits);
        }
    }
}
