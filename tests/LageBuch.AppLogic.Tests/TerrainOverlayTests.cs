using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public sealed class TerrainOverlayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-terrain-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_picker_offers_every_layer_and_starts_on_none()
    {
        var terrain = new TerrainOverlay(new MapViewport());

        Assert.Equal(
            ["Aus", "Schummerung", "Höhenlinien", "Höhenschichten", "Hangneigung", "Höhe zum Einsatzort", "Fließweg vom Einsatzort"],
            terrain.Choices.Select(c => c.Label));
        Assert.Equal(TerrainLayer.None, terrain.Layer);
        Assert.True(terrain.Choices[0].IsSelected);
        Assert.Null(terrain.Style);
        Assert.False(terrain.HasElevation);
    }

    [Fact]
    public void A_pack_without_an_elevation_file_leaves_the_picker_hidden_without_an_error()
    {
        var terrain = new TerrainOverlay(new MapViewport());
        var folder = Directory.CreateDirectory(Path.Join(_dir, "old")).FullName;

        terrain.Show([new InstalledRegionPack(FakeRegionPackLibrary.Pack("old", "Alt"), folder)]);

        Assert.False(terrain.HasElevation);
        Assert.Null(terrain.Error);
    }

    [Fact]
    public void An_unreadable_elevation_file_is_named_and_the_other_packs_still_show_terrain()
    {
        var terrain = new TerrainOverlay(new MapViewport());
        var broken = Directory.CreateDirectory(Path.Join(_dir, "sta")).FullName;
        File.WriteAllText(Path.Join(broken, InstalledRegionPack.ElevationFileName), "not a tiff");

        terrain.Show([TerrainFixture.Pack(_dir), new InstalledRegionPack(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"), broken)]);

        Assert.True(terrain.HasElevation);
        Assert.Single(Assert.IsType<LageBuch.Persistence.Map.Elevation.ElevationSet>(terrain.Elevation).Grids);
        Assert.Equal("Höhendaten nicht lesbar (Landkreis Starnberg) – dort kein Gelände.", terrain.Error);
    }

    [Fact]
    public void Picking_a_layer_checks_it_alone_and_hands_the_map_its_style()
    {
        var terrain = Shown();

        terrain.Choices[1].IsSelected = true;

        Assert.Equal(TerrainLayer.Schummerung, terrain.Layer);
        Assert.Equal([false, true, false, false, false, false, false], terrain.Choices.Select(c => c.IsSelected));
        Assert.Equal(new TerrainStyle(TerrainLayer.Schummerung), terrain.Style);

        terrain.Choices[0].IsSelected = true;

        Assert.Null(terrain.Style);
        Assert.False(terrain.ShowsLegend);
    }

    [Fact]
    public void Each_layer_s_key_explains_it()
    {
        var terrain = Shown();

        terrain.Layer = TerrainLayer.Hoehenlinien;
        Assert.Equal("Höhenlinien alle 5 m, Zähllinie alle 25 m", terrain.LegendText);

        terrain.Layer = TerrainLayer.Hangneigung;
        Assert.True(terrain.ShowsSlopeKey);
        Assert.False(terrain.ShowsRampKey);

        terrain.Layer = TerrainLayer.Hoehenschichten;
        Assert.True(terrain.ShowsRampKey);
        Assert.Matches("^\\d+ m … \\d+ m ü\\. NHN$", terrain.LegendText);
    }

    [Fact]
    public void Heights_relative_to_the_Einsatzort_wait_for_one_to_be_set()
    {
        var terrain = Shown();
        terrain.Layer = TerrainLayer.RelativZumEinsatzort;

        Assert.Null(terrain.Style);
        Assert.Equal("Für Höhen zum Einsatzort erst den Einsatzort setzen.", terrain.LegendText);
        Assert.False(terrain.ShowsRelativeKey);

        terrain.Einsatzort = TerrainFixture.Marktplatz;

        Assert.Equal(550, Assert.NotNull(Assert.IsType<TerrainStyle>(terrain.Style).ReferenceHeight), precision: 1);
        Assert.Equal("als der Einsatzort (550,0 m)", terrain.LegendText);
        Assert.True(terrain.ShowsRelativeKey);
    }

    [Fact]
    public void Too_far_out_the_key_asks_to_zoom_in()
    {
        var map = new MapViewport { Zoom = TerrainSampler.MinZoom - 1, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        terrain.Layer = TerrainLayer.Hangneigung;

        Assert.Equal("Gelände: näher heranzoomen.", terrain.LegendText);
        Assert.False(terrain.ShowsSlopeKey);

        map.ZoomInCommand.Execute(null);

        Assert.Equal("Hangneigung", terrain.LegendText);
        Assert.True(terrain.ShowsSlopeKey);
    }

    [Fact]
    public void The_colour_range_stays_over_a_small_pan_and_follows_a_long_one()
    {
        var map = new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        terrain.Layer = TerrainLayer.Hoehenschichten;
        var start = terrain.Style;

        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 200, 15), 15));
        Assert.Equal(start, terrain.Style);

        // 1 m every 20 m east: a kilometre east is 50 m higher.
        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 700, 15), 15));
        Assert.True(Assert.IsType<TerrainStyle>(terrain.Style).Low > Assert.IsType<TerrainStyle>(start).Low);
    }

    [Fact]
    public void The_colour_range_spans_about_a_screen_around_the_centre()
    {
        // ±640 × ±400 pixels at zoom 15: ±2.04 km east (±102 m) and ±1.28 km north (±32 m).
        var terrain = Shown(new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz });
        terrain.Layer = TerrainLayer.Hoehenschichten;

        var style = Assert.IsType<TerrainStyle>(terrain.Style);

        Assert.InRange(style.Low, 410, 430);
        Assert.InRange(style.High, 670, 690);
    }

    [Fact]
    public void The_key_tells_the_view_what_changed_and_nothing_when_nothing_did()
    {
        var map = new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        var raised = new List<string?>();
        terrain.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        terrain.Layer = TerrainLayer.Hangneigung;
        Assert.Contains(nameof(TerrainOverlay.LegendText), raised);
        Assert.Contains(nameof(TerrainOverlay.ShowsLegend), raised);
        Assert.Contains(nameof(TerrainOverlay.ShowsSlopeKey), raised);
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsRelativeKey), raised);
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsRampKey), raised);

        // Another layer with a key: the key still shows, so only its text and swatches change.
        raised.Clear();
        terrain.Layer = TerrainLayer.Hoehenschichten;
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsLegend), raised);
        Assert.Contains(nameof(TerrainOverlay.ShowsRampKey), raised);
        Assert.Contains(nameof(TerrainOverlay.ShowsSlopeKey), raised);
        terrain.Layer = TerrainLayer.Hangneigung;

        raised.Clear();
        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 30, 15), 15));
        Assert.DoesNotContain(nameof(TerrainOverlay.LegendText), raised);
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsSlopeKey), raised);

        terrain.Einsatzort = TerrainFixture.Marktplatz;
        terrain.Layer = TerrainLayer.RelativZumEinsatzort;
        Assert.True(terrain.ShowsRelativeKey);
        Assert.False(terrain.ShowsSlopeKey);
        raised.Clear();
        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 60, 15), 15));
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsRelativeKey), raised);

        terrain.Layer = TerrainLayer.Hoehenschichten;
        Assert.True(terrain.ShowsRampKey);
        raised.Clear();
        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 90, 15), 15));
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsRampKey), raised);
        Assert.DoesNotContain(nameof(TerrainOverlay.ShowsLegend), raised);
    }

    [Fact]
    public async Task The_Fliessweg_waits_for_an_Einsatzort_then_runs_from_it()
    {
        var terrain = Shown();
        terrain.Layer = TerrainLayer.Fliessweg;

        Assert.Null(terrain.FlowPoints);
        Assert.Equal("Für den Fließweg erst den Einsatzort setzen.", terrain.LegendText);
        Assert.False(terrain.ShowsFlowKey);
        var raised = new List<string?>();
        terrain.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        terrain.Einsatzort = TerrainFixture.Marktplatz;
        await terrain.FlowSettled;

        Assert.Contains(nameof(TerrainOverlay.ShowsFlowKey), raised);
        Assert.False(terrain.IsTracingFlow);
        var points = Assert.IsAssignableFrom<IReadOnlyList<GeoPoint>>(terrain.FlowPoints);
        Assert.True(points.Count > 10);
        Assert.Equal(TerrainFixture.Marktplatz.Latitude, points[0].Latitude, precision: 4);
        Assert.Equal(TerrainFixture.Marktplatz.Longitude, points[0].Longitude, precision: 4);
        Assert.StartsWith("Fließweg ", terrain.LegendText, StringComparison.Ordinal);
        Assert.True(terrain.ShowsFlowKey);
        Assert.Equal(new TerrainStyle(TerrainLayer.Fliessweg), terrain.Style);
    }

    [Fact]
    public async Task The_Fliessweg_is_traced_once_per_Einsatzort_not_on_every_pan()
    {
        var map = new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        terrain.Einsatzort = TerrainFixture.Marktplatz;
        terrain.Layer = TerrainLayer.Fliessweg;
        await terrain.FlowSettled;
        var first = Assert.IsType<FlowRoute>(terrain.FlowRoute);

        map.ChangeViewCommand.Execute(new MapViewChange(Shift(TerrainFixture.Marktplatz, 300, 15), 15));
        Assert.Same(first, terrain.FlowRoute);
        Assert.False(terrain.IsTracingFlow);

        terrain.Einsatzort = Shift(TerrainFixture.Marktplatz, 100, 15);
        await terrain.FlowSettled;
        Assert.NotSame(first, terrain.FlowRoute);
    }

    [Fact]
    public void While_the_Fliessweg_is_traced_the_key_says_so()
    {
        var terrain = Shown();
        terrain.Einsatzort = TerrainFixture.Marktplatz;

        terrain.Layer = TerrainLayer.Fliessweg;

        // Without a UI thread to come back to, the trace's result arrives later; until then:
        Assert.True(terrain.IsTracingFlow || terrain.FlowRoute is not null);
        if (terrain.IsTracingFlow)
        {
            Assert.Equal("Fließweg wird berechnet …", terrain.LegendText);
        }
    }

    [Fact]
    public async Task A_trace_for_an_Einsatzort_that_moved_meanwhile_is_dropped()
    {
        using var first = new ManualResetEventSlim();
        var terrain = new TerrainOverlay(
            new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz },
            (elevation, at) =>
            {
                if (Equals(at, TerrainFixture.Marktplatz))
                {
                    first.Wait(TimeSpan.FromSeconds(10));
                }

                return FlowPath.Trace(elevation, at);
            });
        terrain.Show([TerrainFixture.Pack(_dir)]);
        terrain.Einsatzort = TerrainFixture.Marktplatz;
        terrain.Layer = TerrainLayer.Fliessweg;
        var stale = terrain.FlowSettled;

        // Moved to where there are no heights: the new trace finds nothing, the old one must not win.
        terrain.Einsatzort = new GeoPoint(52.52, 13.40);
        await terrain.FlowSettled;
        first.Set();
        await stale;

        Assert.Null(terrain.FlowRoute);
        Assert.Equal("Am Einsatzort gibt es keine Höhendaten.", terrain.LegendText);
    }

    [Fact]
    public async Task A_trace_that_fails_says_so_in_the_key()
    {
        var terrain = new TerrainOverlay(
            new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz },
            (_, _) => throw new InvalidOperationException("broken"));
        terrain.Show([TerrainFixture.Pack(_dir)]);
        terrain.Einsatzort = TerrainFixture.Marktplatz;

        terrain.Layer = TerrainLayer.Fliessweg;
        await terrain.FlowSettled;

        Assert.Null(terrain.FlowRoute);
        Assert.False(terrain.IsTracingFlow);
        Assert.Equal("Der Fließweg ließ sich nicht berechnen.", terrain.LegendText);
    }

    [Fact]
    public async Task Leaving_the_layer_while_tracing_drops_the_result()
    {
        var terrain = Shown();
        terrain.Einsatzort = TerrainFixture.Marktplatz;
        terrain.Layer = TerrainLayer.Fliessweg;
        var stale = terrain.FlowSettled;

        terrain.Layer = TerrainLayer.Schummerung;
        await stale;

        Assert.Null(terrain.FlowRoute);
        Assert.False(terrain.IsTracingFlow);
        Assert.Equal("Relief überhöht dargestellt", terrain.LegendText);
    }

    [Fact]
    public async Task Leaving_the_Fliessweg_takes_the_line_off_the_map_and_coming_back_draws_it_again()
    {
        var terrain = Shown();
        terrain.Einsatzort = TerrainFixture.Marktplatz;
        terrain.Layer = TerrainLayer.Fliessweg;
        await terrain.FlowSettled;

        terrain.Layer = TerrainLayer.Schummerung;
        Assert.Null(terrain.FlowPoints);
        Assert.False(terrain.ShowsFlowKey);

        terrain.Layer = TerrainLayer.Fliessweg;
        await terrain.FlowSettled;
        Assert.NotNull(terrain.FlowPoints);
    }

    [Fact]
    public async Task The_Fliessweg_is_a_line_and_shows_even_too_far_out_for_relief()
    {
        var map = new MapViewport { Zoom = TerrainSampler.MinZoom - 1, Center = TerrainFixture.Marktplatz };
        var terrain = Shown(map);
        terrain.Einsatzort = TerrainFixture.Marktplatz;

        terrain.Layer = TerrainLayer.Fliessweg;
        await terrain.FlowSettled;

        Assert.NotNull(terrain.FlowPoints);
        Assert.StartsWith("Fließweg ", terrain.LegendText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_heights_under_the_Einsatzort_the_key_says_so()
    {
        var terrain = Shown();
        terrain.Einsatzort = new GeoPoint(52.52, 13.40);

        terrain.Layer = TerrainLayer.Fliessweg;
        await terrain.FlowSettled;

        Assert.Null(terrain.FlowPoints);
        Assert.Equal("Am Einsatzort gibt es keine Höhendaten.", terrain.LegendText);
    }

    private TerrainOverlay Shown(MapViewport? map = null)
    {
        var terrain = new TerrainOverlay(map ?? new MapViewport { Zoom = 15, Center = TerrainFixture.Marktplatz });
        terrain.Show([TerrainFixture.Pack(_dir)]);
        return terrain;
    }

    private static GeoPoint Shift(GeoPoint point, double pixelsEast, int zoom)
    {
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return WebMercator.ToGeo(x + pixelsEast, y, zoom);
    }
}
