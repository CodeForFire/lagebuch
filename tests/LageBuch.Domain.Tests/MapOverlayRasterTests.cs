using LageBuch.Domain.Files;
using LageBuch.Domain.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

/// <summary>A Luftbild or Flurstücksgrenzen Ebene: an image laid on the Lagekarte at its own box.</summary>
public class MapOverlayRasterTests
{
    private const string Attribution = "Luftbild, Flurstücke: © Bayerische Vermessungsverwaltung (CC BY 4.0)";

    private static readonly DateTimeOffset T0 = new(2026, 3, 14, 10, 20, 0, TimeSpan.FromHours(1));

    // Around the Fürstenfeldbrucker Klosterkirche, about a kilometre each way.
    private static readonly GeoBox Box = new(11.24, 48.17, 11.26, 48.18);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static MapOverlaySummary Raster(GeoBox? bounds = null) =>
        new("Luftbild (LDBV)", 1, null, null, MapOverlayKind.Raster, bounds ?? Box, Attribution);

    [Fact]
    public void A_Luftbild_is_imported_as_a_raster_Ebene_with_its_box_and_attribution()
    {
        var (incident, clock, op) = NewIncident();

        var overlay = incident.AddMapOverlay(clock, op, Guid.NewGuid(), Guid.NewGuid(), "luftbild.jpg", "image/jpeg", 2048, Raster());

        Assert.Equal(MapOverlayKind.Raster, overlay.Kind);
        Assert.Equal(Box, overlay.Bounds);
        Assert.Equal(Attribution, overlay.Attribution);
        Assert.Equal("image/jpeg", Assert.Single(incident.Files).ContentType);
        Assert.Equal("Kartenebene importiert: Luftbild (LDBV)", incident.Journal[^1].Text);
    }

    [Fact]
    public void A_GeoJSON_Ebene_stays_a_vector_one_without_a_box()
    {
        var (incident, clock, op) = NewIncident();

        var overlay = incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "lage.geojson", "application/geo+json", 10, new MapOverlaySummary("lage", 1, null, null));

        Assert.Equal(MapOverlayKind.Vector, overlay.Kind);
        Assert.Null(overlay.Bounds);
        Assert.Null(overlay.Attribution);
    }

    [Fact]
    public void A_raster_Ebene_without_a_box_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        var refused = Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "luftbild.jpg", "image/jpeg", 10, Raster() with { Bounds = null }));

        Assert.StartsWith("Ein Luftbild braucht seine Lage auf der Karte.", refused.Message, StringComparison.Ordinal);
        Assert.Empty(incident.MapOverlays);
        Assert.Empty(incident.Files);
    }

    [Fact]
    public void A_vector_Ebene_with_a_box_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "lage.geojson", "application/geo+json", 10, new MapOverlaySummary("lage", 1, null, null, Bounds: Box)));
    }

    [Fact]
    public void A_kind_no_build_knows_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "luftbild.jpg", "image/jpeg", 10, Raster() with { Kind = (MapOverlayKind)7 }));
    }

    [Theory]
    [InlineData("luftbild.png", "image/png", true)]
    [InlineData("luftbild.jpg", "image/jpeg", true)]
    [InlineData("lage.geojson", "application/geo+json", false)]
    [InlineData("lage.zip", "application/zip", false)]
    public void A_raster_Ebene_takes_only_a_JPEG_or_PNG(string fileName, string contentType, bool accepted)
    {
        var (incident, clock, op) = NewIncident();

        var add = () => incident.AddMapOverlay(clock, op, Guid.NewGuid(), Guid.NewGuid(), fileName, contentType, 10, Raster());

        if (accepted)
        {
            add();
            Assert.Single(incident.MapOverlays);
        }
        else
        {
            Assert.Throws<ArgumentException>(add);
        }
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void A_vector_Ebene_refuses_an_image(string contentType)
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() => incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "lage.png", contentType, 10, new MapOverlaySummary("lage", 1, null, null)));
    }

    [Fact]
    public void The_raster_file_types_are_exactly_JPEG_and_PNG()
    {
        Assert.Equal(
            new Dictionary<string, string>
            {
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
            },
            IncidentFile.MapOverlayRasterMimeTypesByExtension);
    }

    [Fact]
    public void An_attribution_is_trimmed_capped_and_blank_means_none()
    {
        var (incident, clock, op) = NewIncident();

        var long_ = incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "a.png", "image/png", 10, Raster() with { Attribution = "  " + new string('x', 500) });
        var blank = incident.AddMapOverlay(
            clock, op, Guid.NewGuid(), Guid.NewGuid(), "b.png", "image/png", 10, Raster() with { Attribution = "   " });

        Assert.Equal(MapOverlay.MaxAttributionLength, long_.Attribution?.Length);
        Assert.Null(blank.Attribution);
    }

    [Fact]
    public void An_Einsatz_takes_only_so_many_raster_Ebenen_but_still_GeoJSON_ones()
    {
        var (incident, clock, op) = NewIncident();
        for (var i = 0; i < Incident.MaxRasterMapOverlays; i++)
        {
            incident.AddMapOverlay(clock, op, Guid.NewGuid(), Guid.NewGuid(), "a.png", "image/png", 10, Raster());
        }

        var refused = Assert.Throws<InvalidOperationException>(() =>
            incident.AddMapOverlay(clock, op, Guid.NewGuid(), Guid.NewGuid(), "b.png", "image/png", 10, Raster()));
        Assert.Equal($"Ein Einsatz hat höchstens {Incident.MaxRasterMapOverlays} Luftbild-Ebenen. Ältere entfernen.", refused.Message);
        Assert.Equal(Incident.MaxRasterMapOverlays, incident.MapOverlays.Count);
        Assert.Equal(Incident.MaxRasterMapOverlays, incident.Files.Count);

        incident.AddMapOverlay(clock, op, Guid.NewGuid(), Guid.NewGuid(), "lage.geojson", "application/geo+json", 10, new MapOverlaySummary("lage", 1, null, null));
        Assert.Equal(Incident.MaxRasterMapOverlays + 1, incident.MapOverlays.Count);
    }

    [Fact]
    public void A_loaded_raster_row_without_a_box_reads_back_as_a_vector_Ebene()
    {
        // The nearest valid Ebene: one the map cannot place is shown as unreadable, never placed anywhere.
        var overlay = MapOverlay.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Luftbild", T0, "Muster", 1, null, null, MapOverlayKind.Raster, null, Attribution);

        Assert.Equal(MapOverlayKind.Vector, overlay.Kind);
        Assert.Null(overlay.Bounds);
    }

    [Fact]
    public void A_loaded_vector_row_drops_a_box_and_an_unknown_kind_reads_as_vector()
    {
        var vector = MapOverlay.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), "lage", T0, "Muster", 1, null, null, MapOverlayKind.Vector, Box, null);
        var unknown = MapOverlay.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), "lage", T0, "Muster", 1, null, null, (MapOverlayKind)9, Box, null);

        Assert.Null(vector.Bounds);
        Assert.Equal(MapOverlayKind.Vector, unknown.Kind);
        Assert.Null(unknown.Bounds);
    }

    [Fact]
    public void A_loaded_raster_row_keeps_its_box_and_attribution()
    {
        var overlay = MapOverlay.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Luftbild", T0, "Muster", 1, null, null, MapOverlayKind.Raster, Box, "  " + Attribution + " ");

        Assert.Equal(MapOverlayKind.Raster, overlay.Kind);
        Assert.Equal(Box, overlay.Bounds);
        Assert.Equal(Attribution, overlay.Attribution);
    }
}
