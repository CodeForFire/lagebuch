using System.Globalization;
using System.Text;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Map;

namespace LageBuch.AppLogic.Tests;

/// <summary>How the Lagekarte shows an Ebene, and what the session stores for one.</summary>
public sealed class LagekarteOverlayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 14, 10, 5, 0, TimeSpan.FromHours(1));

    private readonly FakeStore _store = new();
    private readonly LocalIncidentSession _session;

    public LagekarteOverlayTests()
    {
        _session = TestSession.StartNew(
            _store,
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
    }

    private static string Box(double south, double west, double north, double east)
    {
        string N(double v) => v.ToString(CultureInfo.InvariantCulture);
        return $$$"""{"type": "Polygon", "coordinates": [[[{{{N(west)}}}, {{{N(south)}}}], [{{{N(east)}}}, {{{N(south)}}}], [{{{N(east)}}}, {{{N(north)}}}], [{{{N(west)}}}, {{{N(south)}}}]]]}""";
    }

    private async Task<LagekarteViewModel> ShowAsync(string json, FixedClock? clock = null)
    {
        var vm = new LagekarteViewModel(_session, () => { }, clock: clock);
        await _session.AddMapOverlayAsync("e.geojson", "application/geo+json", Encoding.UTF8.GetBytes(json), new MapOverlaySummary("e", 1, null, null));
        await vm.Overlays.SettledAsync();
        vm.Overlays.Items[^1].ShowCommand.Execute(null);
        return vm;
    }

    [Theory]
    [InlineData(48.0, 11.0, 48.1, 11.1, 12)]
    [InlineData(48.0, 11.0, 48.01, 11.01, 15)]
    [InlineData(40.0, 5.0, 55.0, 20.0, 8)]
    [InlineData(48.0, 11.0, 48.0, 11.0, 21)]
    public async Task Showing_an_Ebene_centres_it_at_the_closest_zoom_that_fits_it_whole(double south, double west, double north, double east, int zoom)
    {
        var vm = await ShowAsync(Box(south, west, north, east));

        Assert.Equal(zoom, vm.Map.Zoom);
        Assert.Equal((south + north) / 2, vm.Map.Center.Latitude, precision: 9);
        Assert.Equal((west + east) / 2, vm.Map.Center.Longitude, precision: 9);
    }

    [Fact]
    public async Task The_forecast_step_shown_follows_the_clock_the_Lagekarte_was_given()
    {
        // 09:00 and 09:15 UTC; the clock reads 09:05 UTC, so the first step has begun and the second not.
        var json = """
            {"type": "FeatureCollection", "features": [
              {"type": "Feature", "properties": {"ftime": "2026-03-14T09:00:00Z"}, "geometry": {"type": "Point", "coordinates": [11.0, 48.0]}},
              {"type": "Feature", "properties": {"ftime": "2026-03-14T09:15:00Z"}, "geometry": {"type": "Point", "coordinates": [11.1, 48.0]}}
            ]}
            """;

        var vm = await ShowAsync(json, new FixedClock(T0));

        Assert.Equal(0, vm.Overlays.Items[0].FrameIndex);
    }

    [Fact]
    public async Task Importing_saves_flushes_then_writes_the_bytes_and_announces_the_change()
    {
        var announced = 0;
        _session.Changed += () => announced++;
        var saves = _store.SaveCount;
        var bytes = Encoding.UTF8.GetBytes(Box(48, 11, 48.1, 11.1));

        await _session.AddMapOverlayAsync("e.geojson", "application/geo+json", bytes, new MapOverlaySummary("e", 1, null, null));

        var overlay = Assert.Single(_session.Incident.MapOverlays);
        Assert.Equal(bytes, await _session.GetFileBytesAsync(overlay.FileId));
        Assert.True(_store.SaveCount > saves);
        Assert.True(_store.FlushCount > 0);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task Removing_saves_and_flushes_before_it_wipes_the_bytes_and_announces_the_change()
    {
        await _session.AddMapOverlayAsync("e.geojson", "application/geo+json", [1, 2, 3], new MapOverlaySummary("e", 1, null, null));
        var overlay = _session.Incident.MapOverlays[0];
        var saves = _store.SaveCount;
        var flushes = _store.FlushCount;
        var announced = 0;
        _session.Changed += () => announced++;

        await _session.RemoveMapOverlayAsync(overlay.Id);

        Assert.Null(await _session.GetFileBytesAsync(overlay.FileId));
        Assert.True(_store.SaveCountAtDelete > saves);
        Assert.True(_store.FlushCountAtDelete > flushes);
        Assert.Equal(1, announced);
    }
}
