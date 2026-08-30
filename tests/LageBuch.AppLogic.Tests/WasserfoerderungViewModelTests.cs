using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.AppLogic.Tests;

public class WasserfoerderungViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(2));

    private sealed class FakeElevationSampler : IElevationSampler
    {
        public IReadOnlyList<GeoPoint>? LastPolyline { get; private set; }

        public IReadOnlyList<ElevationProfileSample> Sample(IReadOnlyList<GeoPoint> polyline)
        {
            LastPolyline = polyline;
            // Matches the interior-crest fixture used elsewhere: forces exactly 1 pump.
            return new[]
            {
                new ElevationProfileSample(0, 0),
                new ElevationProfileSample(200, 50),
                new ElevationProfileSample(400, 0),
            };
        }
    }

    private sealed class FakeTileSource : IMapTileSource
    {
        public byte[]? GetTile(int zoom, int x, int y) => null;
    }

    private static (LocalIncidentSession Session, FakeStore Store) NewSession()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(store, clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        return (session, store);
    }

    [Fact]
    public void Map_mode_is_unavailable_when_no_samplers_are_configured()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { });

        Assert.False(vm.IsMapModeAvailable);
    }

    [Fact]
    public void Map_mode_is_available_when_both_samplers_are_configured()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());

        Assert.True(vm.IsMapModeAvailable);
    }

    [Fact]
    public void AddRoutePoint_appends_and_UndoLastRoutePoint_removes_the_last_one()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());

        vm.AddRoutePointCommand.Execute(new GeoPoint(48.0, 11.0));
        vm.AddRoutePointCommand.Execute(new GeoPoint(48.001, 11.0));
        Assert.Equal(2, vm.DrawnRoutePoints.Count);

        vm.UndoLastRoutePointCommand.Execute(null);
        Assert.Single(vm.DrawnRoutePoints);
        Assert.Equal(new GeoPoint(48.0, 11.0), vm.DrawnRoutePoints[0]);
    }

    [Fact]
    public void ClearRoute_empties_the_drawn_points()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());
        vm.AddRoutePointCommand.Execute(new GeoPoint(48.0, 11.0));

        vm.ClearRouteCommand.Execute(null);

        Assert.Empty(vm.DrawnRoutePoints);
    }

    [Fact]
    public void FinishRoute_requires_at_least_two_points()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());

        Assert.False(vm.FinishRouteCommand.CanExecute(null));

        vm.AddRoutePointCommand.Execute(new GeoPoint(48.0, 11.0));
        Assert.False(vm.FinishRouteCommand.CanExecute(null));

        vm.AddRoutePointCommand.Execute(new GeoPoint(48.002, 11.0));
        Assert.True(vm.FinishRouteCommand.CanExecute(null));
    }

    [Fact]
    public void FinishRoute_samples_the_profile_and_adds_the_leitung_then_clears_the_drawn_route()
    {
        var (session, _) = NewSession();
        var changedCount = 0;
        var sampler = new FakeElevationSampler();
        var vm = new WasserfoerderungViewModel(session, () => changedCount++, sampler, new FakeTileSource())
        {
            NewUebergabestelle = "TLF 20/8",
            NewAnsprechpartner = "FFB 1/44/1",
        };
        var route = new[] { new GeoPoint(48.0, 11.0), new GeoPoint(48.002, 11.0) };
        vm.AddRoutePointCommand.Execute(route[0]);
        vm.AddRoutePointCommand.Execute(route[1]);

        vm.FinishRouteCommand.Execute(null);

        Assert.Equal(route, sampler.LastPolyline);
        var leitung = Assert.Single(session.Incident.Wasserfoerderung);
        Assert.Equal(route, leitung.RoutePoints);
        Assert.Equal(1, leitung.PumpCount); // interior crest, per FakeElevationSampler's profile
        Assert.Equal("TLF 20/8", leitung.Uebergabestelle);
        Assert.Empty(vm.DrawnRoutePoints);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Zoom_commands_step_within_bounds()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());
        var startZoom = vm.MapZoom;

        vm.ZoomInCommand.Execute(null);
        Assert.Equal(startZoom + 1, vm.MapZoom);

        vm.ZoomOutCommand.Execute(null);
        vm.ZoomOutCommand.Execute(null);
        Assert.True(vm.MapZoom >= 3); // never below the lower bound, whatever it is
    }

    [Fact]
    public void Manuell_mode_AddLeitung_still_works_unchanged()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { })
        {
            NewLengthMeters = 2000,
            NewElevationRiseMeters = 0,
        };

        vm.AddLeitungCommand.Execute(null);

        var leitung = Assert.Single(session.Incident.Wasserfoerderung);
        Assert.Null(leitung.RoutePoints);
        Assert.Equal(2000, leitung.LengthMeters);
    }
}
