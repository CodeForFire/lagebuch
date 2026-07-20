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
        incident.SetIlsNumber(IlsNumber.Parse("4242"));
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Meldung", from: "ILS");

        new IncidentRepository().Save(_path, incident);

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT ils_number FROM incident_meta;";
        Assert.Equal("4242", (string)cmd.ExecuteScalar()!);
        // Two rows: the automatic "Einsatz begonnen" entry from Incident.Start plus the
        // manual one above.
        cmd.CommandText = "SELECT count(*) FROM etb_entries;";
        Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
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
