using System.Globalization;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// The file now stores its own Checklisten rather than two kind-tagged item runs, so a save/load
// has to preserve each list's id, title and order -- and, because Save is a full rewrite, must not
// accumulate rows. V21 exists only because force_unit_edits was missing from Save's wipe list and
// every save re-inserted the whole history, which no test caught.
public class ChecklistListRoundTripTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"cl-rt-{Guid.NewGuid():N}.fwincident");

    private static readonly Guid Nachbereitung = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Fahrzeug = new("55555555-5555-5555-5555-555555555555");

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

        GC.SuppressFinalize(this);
    }

    private static Incident ThreeListIncident()
    {
        var incident = Incident.Start(new Clock(), new SessionOperator("Müller", "FFB 12/1"), "Brand");
        incident.SeedChecklist(new[]
        {
            new ChecklistSeed(
                ChecklistDefaults.AufbauListId,
                ChecklistDefaults.AufbauTitle,
                new[] { ("Blaulicht aus?", true), ("Bei ILS gemeldet?", false) }),
            new ChecklistSeed(
                Nachbereitung,
                "Nachbereitung",
                new[] { ("Bericht schreiben", false) }),
            new ChecklistSeed(
                Fahrzeug,
                "Fahrzeug",
                new[] { ("Tank gefüllt", true), ("Schläuche getauscht", false), ("Gewaschen", false) }),
        });
        return incident;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Test-only counting query; the two possible strings are compile-time constants.")]
    private long CountRows(string table)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = table == "checklist_lists"
            ? "SELECT count(*) FROM checklist_lists;"
            : "SELECT count(*) FROM checklist_items;";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Three_lists_round_trip_with_their_ids_titles_and_order()
    {
        IncidentRepository.Save(_path, ThreeListIncident());

        var loaded = IncidentRepository.Load(_path);

        Assert.Equal(
            new[] { ChecklistDefaults.AufbauListId, Nachbereitung, Fahrzeug },
            loaded.Checklists.Select(l => l.Id));
        Assert.Equal(
            new[] { "Aufbau", "Nachbereitung", "Fahrzeug" },
            loaded.Checklists.Select(l => l.Title));
        Assert.Equal(new[] { 2, 1, 3 }, loaded.Checklists.Select(l => l.Items.Count));
    }

    [Fact]
    public void Item_order_and_flags_survive_within_each_list()
    {
        IncidentRepository.Save(_path, ThreeListIncident());

        var fahrzeug = IncidentRepository.Load(_path).Checklists.Single(l => l.Id == Fahrzeug);

        Assert.Equal(
            new[] { "Tank gefüllt", "Schläuche getauscht", "Gewaschen" },
            fahrzeug.Items.Select(i => i.Text));
        Assert.Equal(new[] { true, false, false }, fahrzeug.Items.Select(i => i.IsMandatory));
    }

    [Fact]
    public void A_ticked_item_stays_ticked()
    {
        var incident = ThreeListIncident();
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        incident.ToggleChecklistItem(clock, op, incident.Checklists[1].Items[0].Id);
        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);

        Assert.True(loaded.Checklists[1].Items[0].IsDone);
        Assert.False(loaded.Checklists[0].Items[0].IsDone);
    }

    [Fact]
    public void An_incident_with_no_checklists_round_trips_as_none()
    {
        var incident = Incident.Start(new Clock(), new SessionOperator("Müller", "FFB 12/1"), "Brand");
        incident.SeedChecklist(Array.Empty<ChecklistSeed>());
        IncidentRepository.Save(_path, incident);

        Assert.Empty(IncidentRepository.Load(_path).Checklists);
    }

    // Save wipes and rewrites. If checklist_lists is missing from that wipe list, every save
    // silently doubles the rows -- and a load that groups by list_id would still look right.
    [Fact]
    public void Saving_repeatedly_does_not_accumulate_rows()
    {
        var incident = ThreeListIncident();
        IncidentRepository.Save(_path, incident);
        var listsAfterFirst = CountRows("checklist_lists");
        var itemsAfterFirst = CountRows("checklist_items");

        IncidentRepository.Save(_path, incident);
        IncidentRepository.Save(_path, incident);

        Assert.Equal(3, listsAfterFirst);
        Assert.Equal(6, itemsAfterFirst);
        Assert.Equal(listsAfterFirst, CountRows("checklist_lists"));
        Assert.Equal(itemsAfterFirst, CountRows("checklist_items"));
        Assert.Equal(3, IncidentRepository.Load(_path).Checklists.Count);
    }
}
