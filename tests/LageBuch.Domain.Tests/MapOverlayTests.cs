using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Map;

namespace LageBuch.Domain.Tests;

public class MapOverlayTests
{
    // 16:51 in Germany in November: an hour ahead of the UTC the DWD writes.
    private static readonly DateTimeOffset T0 = new(2025, 11, 7, 16, 51, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset First = new(2025, 11, 7, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Last = new(2025, 11, 7, 16, 45, 0, TimeSpan.Zero);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static MapOverlay Import(Incident incident, FixedClock clock, SessionOperator op, MapOverlaySummary? summary = null, string fileName = "prognose.zip") =>
        incident.AddMapOverlay(
            clock,
            op,
            Guid.NewGuid(),
            Guid.NewGuid(),
            fileName,
            IncidentFile.MapOverlayMimeTypesByExtension[Path.GetExtension(fileName)],
            2048,
            summary ?? new MapOverlaySummary("prognose", 8, First, Last));

    [Fact]
    public void An_imported_forecast_is_recorded_with_its_times_in_local_time_and_stays_visible_with_system_lines_hidden()
    {
        var (incident, clock, op) = NewIncident();

        var overlay = Import(incident, clock, op);

        Assert.Same(overlay, Assert.Single(incident.MapOverlays));
        Assert.True(overlay.IsTimeSeries);
        var entry = incident.Journal[^1];
        Assert.Equal("Kartenebene importiert: prognose (8 Zeitpunkte, 16:00–17:45 Uhr)", entry.Text);
        Assert.Equal(EtbDirection.Measurement, entry.Direction);
    }

    [Fact]
    public void An_Ebene_without_times_is_recorded_by_its_name_alone()
    {
        var (incident, clock, op) = NewIncident();

        Import(incident, clock, op, new MapOverlaySummary("Sperrgebiet", 1, null, null), "sperrgebiet.geojson");

        Assert.Equal("Kartenebene importiert: Sperrgebiet", incident.Journal[^1].Text);
    }

    [Fact]
    public void The_file_of_an_Ebene_joins_the_Einsatz_s_files_under_its_own_id()
    {
        var (incident, clock, op) = NewIncident();

        var overlay = Import(incident, clock, op);

        var file = Assert.Single(incident.Files);
        Assert.Equal(overlay.FileId, file.Id);
        Assert.Equal("application/zip", file.ContentType);
    }

    [Fact]
    public void Removing_an_Ebene_takes_its_file_with_it_and_says_so()
    {
        var (incident, clock, op) = NewIncident();
        var overlay = Import(incident, clock, op);

        var removed = incident.RemoveMapOverlay(clock, op, overlay.Id);

        Assert.Equal(overlay.FileId, removed?.Id);
        Assert.Empty(incident.MapOverlays);
        Assert.Empty(incident.Files);
        Assert.Equal("Kartenebene entfernt: prognose", incident.Journal[^1].Text);
    }

    [Fact]
    public void Removing_the_file_of_an_Ebene_under_Dateien_removes_the_Ebene_too()
    {
        var (incident, clock, op) = NewIncident();
        var overlay = Import(incident, clock, op);

        incident.RemoveFile(clock, op, overlay.FileId);

        Assert.Empty(incident.MapOverlays);
        Assert.Equal("Kartenebene entfernt: prognose", incident.Journal[^1].Text);
    }

    [Fact]
    public void An_unknown_Ebene_cannot_be_removed()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.RemoveMapOverlay(clock, op, Guid.NewGuid()));
    }

    [Fact]
    public void The_same_ids_cannot_be_imported_twice()
    {
        var (incident, clock, op) = NewIncident();
        var overlay = Import(incident, clock, op);

        Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, overlay.Id, Guid.NewGuid(), "b.zip", "application/zip", 10, new MapOverlaySummary("b", 1, null, null)));
        Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), overlay.FileId, "b.zip", "application/zip", 10, new MapOverlaySummary("b", 1, null, null)));
    }

    [Fact]
    public void A_closed_Einsatz_takes_no_Ebene()
    {
        var (incident, clock, op) = NewIncident();
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => Import(incident, clock, op));
    }

    [Fact]
    public void An_Einsatz_holds_no_more_than_the_cap_of_Ebenen()
    {
        var (incident, clock, op) = NewIncident();
        for (var i = 0; i < Incident.MaxMapOverlays; i++)
        {
            Import(incident, clock, op);
        }

        Assert.Throws<InvalidOperationException>(() => Import(incident, clock, op));
    }

    [Theory]
    [InlineData("prognose.exe", "application/zip")]
    [InlineData("prognose.zip", "application/pdf")]
    [InlineData("prognose.png", "image/png")]
    public void An_Ebene_s_file_must_be_GeoJSON_or_zip_and_named_for_its_type(string fileName, string contentType)
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), fileName, contentType, 10, new MapOverlaySummary("x", 1, null, null)));
        Assert.Empty(incident.Files);
    }

    [Fact]
    public void A_plain_attachment_still_cannot_be_a_zip()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => incident.AddFile(clock, op, "beweise.zip", "application/zip", 10));
        Assert.False(IncidentFile.MimeTypesByExtension.ContainsKey(".zip"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MapOverlay.MaxFrames + 1)]
    public void A_summary_out_of_range_is_refused(int frames)
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => Import(incident, clock, op, new MapOverlaySummary("x", frames, null, null)));
    }

    [Fact]
    public void Times_that_run_backwards_or_come_half_are_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => Import(incident, clock, op, new MapOverlaySummary("x", 2, Last, First)));
        Assert.Throws<ArgumentException>(() => Import(incident, clock, op, new MapOverlaySummary("x", 2, First, null)));
        Assert.Throws<ArgumentException>(() => Import(incident, clock, op, new MapOverlaySummary("  ", 1, null, null)));
    }

    [Fact]
    public void A_long_name_is_cut_not_refused()
    {
        var (incident, clock, op) = NewIncident();

        var overlay = Import(incident, clock, op, new MapOverlaySummary(new string('a', 300), 1, null, null));

        Assert.Equal(MapOverlay.MaxNameLength, overlay.Name.Length);
    }

    [Fact]
    public void A_stored_Ebene_that_does_not_add_up_reads_back_as_the_nearest_valid_one()
    {
        var overlay = MapOverlay.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), null, T0, null, 9999, Last, First);

        Assert.Equal("Kartenebene", overlay.Name);
        Assert.Equal(MapOverlay.MaxFrames, overlay.FrameCount);
        Assert.Null(overlay.FirstTime);
        Assert.Null(overlay.LastTime);
    }
}
