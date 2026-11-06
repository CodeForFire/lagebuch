using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Domain.Map;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public sealed class MapOverlayRoundTripTests : IDisposable
{
    private static readonly DateTimeOffset First = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    private string Path_ => Path.Join(_dir, "einsatz.lagebuch");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void An_Ebene_and_its_file_survive_save_and_load_in_order()
    {
        var incident = IncidentWithEbenen();

        IncidentRepository.Save(Path_, incident);
        var loaded = IncidentRepository.Load(Path_);

        Assert.Equal(incident.MapOverlays, loaded.MapOverlays);
        Assert.Equal(["prognose 9 Uhr", "Sperrgebiet"], loaded.MapOverlays.Select(o => o.Name));
        Assert.Contains(loaded.Files, f => f.Id == loaded.MapOverlays[0].FileId);
    }

    [Fact]
    public void An_Einsatz_from_before_the_Ebenen_loads_with_none()
    {
        var clock = new FixedTestClock();
        IncidentRepository.Save(Path_, Incident.Start(clock, new SessionOperator("Muster", "FFB 12/1")));
        Tamper("DROP TABLE map_overlays;");

        // Migrate does not recreate a table a foreign build dropped (SchemaGuard leaves tables be),
        // but a file stamped before V42 gets it back.
        Tamper("UPDATE schema_version SET version = 41;");
        var loaded = IncidentRepository.Load(Path_);

        Assert.Empty(loaded.MapOverlays);
    }

    [Theory]
    [InlineData("id = 'kein-guid'")]
    [InlineData("file_id = 'kaputt'")]
    public void A_row_whose_ids_do_not_read_is_dropped_not_the_Einsatz(string tamper)
    {
        IncidentRepository.Save(Path_, IncidentWithEbenen());
        Tamper($"UPDATE map_overlays SET {tamper} WHERE name = 'Sperrgebiet';");

        var loaded = IncidentRepository.Load(Path_);

        Assert.Equal("prognose 9 Uhr", Assert.Single(loaded.MapOverlays).Name);
    }

    [Fact]
    public void A_frame_count_a_hostile_file_stores_out_of_range_is_clamped()
    {
        IncidentRepository.Save(Path_, IncidentWithEbenen());
        Tamper("UPDATE map_overlays SET frame_count = 100000;");

        var loaded = IncidentRepository.Load(Path_);

        Assert.All(loaded.MapOverlays, o => Assert.Equal(MapOverlay.MaxFrames, o.FrameCount));
    }

    private static Incident IncidentWithEbenen()
    {
        var clock = new FixedTestClock();
        var op = new SessionOperator("Muster", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "prognose.zip", "application/zip", 4096, new MapOverlaySummary("prognose 9 Uhr", 8, First, First.AddMinutes(105)));
        incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "sperrgebiet.geojson", "application/geo+json", 512, new MapOverlaySummary("Sperrgebiet", 1, null, null));
        return incident;
    }

    [SuppressMessage("Security", "CA2100", Justification = "Test-only: the statement is built from this test's own constants to play a hostile file.")]
    private void Tamper(string sql)
    {
        using var cn = SqliteConnectionFactory.OpenExisting(Path_);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset Now => new(2026, 3, 14, 10, 0, 0, TimeSpan.FromHours(1));
    }
}
