using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

public class IncidentRepositorySaveTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"save-{Guid.NewGuid():N}.fwincident");
    private sealed class Clock : IClock { public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2)); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Save_writes_meta_and_journal_rows()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, "Brand");
        incident.SetIncidentNumber(new IncidentNumber("B 1.2 260715 4242"));
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Meldung", from: "ILS");

        new IncidentRepository().Save(_path, incident);

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

        new IncidentRepository().Save(_path, incident);

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
        var repo = new IncidentRepository();
        repo.Save(_path, Incident.Start(clock, new SessionOperator("Müller")));

        // Simulate a file written before the unification: the number lived in ils_number and
        // incident_number was empty.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "UPDATE incident_meta SET incident_number = NULL, ils_number = '4711';";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal("4711", repo.Load(_path).IncidentNumber!.Value);
    }

    [Fact]
    public void Save_is_idempotent_overwrite_not_append()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var repo = new IncidentRepository();
        repo.Save(_path, incident);
        repo.Save(_path, incident);

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM incident_meta;";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }
}
