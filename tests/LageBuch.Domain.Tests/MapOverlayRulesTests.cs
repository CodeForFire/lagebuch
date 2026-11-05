using LageBuch.Domain.Files;
using LageBuch.Domain.Map;

namespace LageBuch.Domain.Tests;

/// <summary>The exact edges of the Ebene's rules: messages, boundaries, and what a load keeps.</summary>
public class MapOverlayRulesTests
{
    private static readonly DateTimeOffset T0 = new(2025, 11, 7, 16, 51, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset First = new(2025, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static MapOverlay Import(Incident incident, FixedClock clock, SessionOperator op, MapOverlaySummary summary) =>
        incident.AddMapOverlay(clock, op, Guid.NewGuid(), Guid.NewGuid(), "x.zip", "application/zip", 10, summary);

    [Fact]
    public void The_overlay_file_types_are_exactly_GeoJSON_and_zip()
    {
        Assert.Equal(
            new Dictionary<string, string>
            {
                [".geojson"] = "application/geo+json",
                [".json"] = "application/geo+json",
                [".zip"] = "application/zip",
            },
            IncidentFile.MapOverlayMimeTypesByExtension);
        Assert.True(IncidentFile.MapOverlayMimeTypesByExtension.ContainsKey(".GEOJSON"));
    }

    [Fact]
    public void An_Ebene_with_one_forecast_time_is_recorded_as_a_Stand_not_a_series()
    {
        var (incident, clock, op) = NewIncident();

        var overlay = Import(incident, clock, op, new MapOverlaySummary("lage", 1, First, First));

        Assert.False(overlay.IsTimeSeries);
        Assert.Equal("Kartenebene importiert: lage (Stand 16:00 Uhr)", incident.Journal[^1].Text);
    }

    [Fact]
    public void The_cap_and_a_duplicate_are_refused_in_German()
    {
        var (incident, clock, op) = NewIncident();
        var first = Import(incident, clock, op, new MapOverlaySummary("a", 1, null, null));

        var duplicate = Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, first.Id, Guid.NewGuid(), "b.zip", "application/zip", 10, new MapOverlaySummary("b", 1, null, null)));
        Assert.StartsWith("Diese Kartenebene ist schon im Einsatz.", duplicate.Message, StringComparison.Ordinal);

        for (var i = 1; i < Incident.MaxMapOverlays; i++)
        {
            Import(incident, clock, op, new MapOverlaySummary("a", 1, null, null));
        }

        var full = Assert.Throws<InvalidOperationException>(() => Import(incident, clock, op, new MapOverlaySummary("a", 1, null, null)));
        Assert.Equal("Ein Einsatz hat höchstens 100 Kartenebenen.", full.Message);
    }

    [Fact]
    public void Removing_needs_a_clock_and_an_operator_and_says_which_Ebene_is_unknown()
    {
        var (incident, clock, op) = NewIncident();
        var overlay = Import(incident, clock, op, new MapOverlaySummary("a", 1, null, null));
        var unknown = Guid.NewGuid();

        Assert.Throws<ArgumentNullException>(() => incident.RemoveMapOverlay(null!, op, overlay.Id));
        Assert.Throws<ArgumentNullException>(() => incident.RemoveMapOverlay(clock, null!, overlay.Id));
        Assert.Equal($"Kartenebene {unknown} nicht gefunden.", Assert.Throws<KeyNotFoundException>(() => incident.RemoveMapOverlay(clock, op, unknown)).Message);
        Assert.Single(incident.MapOverlays);
    }

    [Fact]
    public void A_closed_Einsatz_keeps_its_Ebenen()
    {
        var (incident, clock, op) = NewIncident();
        var overlay = Import(incident, clock, op, new MapOverlaySummary("a", 1, null, null));
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.RemoveMapOverlay(clock, op, overlay.Id));
        Assert.Single(incident.MapOverlays);
    }

    [Fact]
    public void Removing_an_Ebene_whose_file_is_already_gone_returns_no_file()
    {
        var overlay = MapOverlay.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), "a", T0, "Muster", 1, null, null);
        var incident = Rehydrated([overlay]);

        Assert.Null(incident.RemoveMapOverlay(new FixedClock(T0), new SessionOperator("Muster", "FFB 12/1"), overlay.Id));
        Assert.Empty(incident.MapOverlays);
    }

    [Fact]
    public void A_load_keeps_the_Ebenen_in_order_drops_a_repeated_id_and_stops_at_the_cap()
    {
        var overlays = Enumerable.Range(0, Incident.MaxMapOverlays + 5)
            .Select(i => MapOverlay.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), $"e{i}", T0, "Muster", 1, null, null))
            .ToList();
        overlays.Insert(1, overlays[0]);

        var incident = Rehydrated(overlays);

        Assert.Equal(Incident.MaxMapOverlays, incident.MapOverlays.Count);
        Assert.Equal("e0", incident.MapOverlays[0].Name);
        Assert.Equal("e1", incident.MapOverlays[1].Name);
        Assert.Empty(Rehydrated(null).MapOverlays);
    }

    [Fact]
    public void An_Ebene_needs_both_ids()
    {
        var summary = new MapOverlaySummary("a", 1, null, null);

        Assert.Throws<ArgumentException>(() => MapOverlay.Create(Guid.Empty, Guid.NewGuid(), summary, T0, "Muster"));
        var noFile = Assert.Throws<ArgumentException>(() => MapOverlay.Create(Guid.NewGuid(), Guid.Empty, summary, T0, "Muster"));
        Assert.StartsWith("Kartenebene ohne Kennung.", noFile.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusals_name_what_is_wrong()
    {
        static string Refused(MapOverlaySummary summary) =>
            Assert.Throws<ArgumentException>(() => MapOverlay.Create(Guid.NewGuid(), Guid.NewGuid(), summary, T0, "Muster")).Message;

        Assert.StartsWith("Kartenebene braucht einen Namen.", Refused(new MapOverlaySummary(" ", 1, null, null)), StringComparison.Ordinal);
        Assert.StartsWith("Eine Kartenebene hat 1 bis 500 Zeitpunkte.", Refused(new MapOverlaySummary("a", 501, null, null)), StringComparison.Ordinal);
        Assert.StartsWith("Die Zeitpunkte der Kartenebene passen nicht zusammen.", Refused(new MapOverlaySummary("a", 2, First.AddHours(1), First)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_limits_themselves_are_allowed()
    {
        var overlay = MapOverlay.Create(Guid.NewGuid(), Guid.NewGuid(), new MapOverlaySummary(new string('a', MapOverlay.MaxNameLength), MapOverlay.MaxFrames, First, First), T0, "Muster");

        Assert.Equal(MapOverlay.MaxFrames, overlay.FrameCount);
        Assert.Equal(MapOverlay.MaxNameLength, overlay.Name.Length);
        Assert.Equal(First, overlay.LastTime);
    }

    [Fact]
    public void A_sound_stored_Ebene_reads_back_as_it_was()
    {
        var id = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var overlay = MapOverlay.Rehydrate(id, fileId, " Lage ", T0, "Muster", 8, First, First);

        Assert.Equal(id, overlay.Id);
        Assert.Equal(fileId, overlay.FileId);
        Assert.Equal("Lage", overlay.Name);
        Assert.Equal(T0, overlay.ImportedAt);
        Assert.Equal("Muster", overlay.ImportedBy);
        Assert.Equal(8, overlay.FrameCount);
        Assert.Equal(First, overlay.FirstTime);
        Assert.Equal(First, overlay.LastTime);
        Assert.Equal(string.Empty, MapOverlay.Rehydrate(id, fileId, "x", T0, null, 0, null, null).ImportedBy);
        Assert.Equal(1, MapOverlay.Rehydrate(id, fileId, "x", T0, null, 0, null, null).FrameCount);
    }

    private static Incident Rehydrated(IEnumerable<MapOverlay>? overlays) =>
        Incident.Rehydrate(Guid.NewGuid(), T0, IncidentState.Open, null, null, null, null, null, null, null, [], [], [], [], [], [], [], [], [], [], [], [], mapOverlays: overlays);
}
