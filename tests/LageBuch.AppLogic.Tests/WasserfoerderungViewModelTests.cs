using LageBuch.AppLogic.Services;
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

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => null;
    }

    private static (LocalIncidentSession Session, FakeStore Store) NewSession()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
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

    // #150 follow-up: with a real region pack, the map must open already centered on the
    // configured Einsatzgebiet's tiles, not the unrelated hardcoded German fallback.
    [Fact]
    public void Map_opens_centered_on_the_given_initial_view_when_provided()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(
            session,
            () => { },
            new FakeElevationSampler(),
            new FakeTileSource(),
            initialMapCenter: new GeoPoint(48.19, 11.15),
            initialMapZoom: 11);

        Assert.Equal(48.19, vm.MapCenterLatitude);
        Assert.Equal(11.15, vm.MapCenterLongitude);
        Assert.Equal(11, vm.MapZoom);
    }

    [Fact]
    public void Map_falls_back_to_the_hardcoded_default_when_no_initial_view_is_given()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());

        Assert.Equal(48.14, vm.MapCenterLatitude);
        Assert.Equal(11.58, vm.MapCenterLongitude);
        Assert.Equal(14, vm.MapZoom);
    }

    // #150 follow-up: wheel/pinch zoom on the map canvas routes through this command instead of
    // two-way property binding (matching PointClickedCommand/UndoRequestedCommand's existing
    // control->VM pattern), so it must apply the new view exactly as given.
    [Fact]
    public void ChangeMapView_applies_the_given_center_and_zoom()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());

        vm.ChangeMapViewCommand.Execute(new MapViewChange(48.2, 11.3, 13));

        Assert.Equal(48.2, vm.MapCenterLatitude);
        Assert.Equal(11.3, vm.MapCenterLongitude);
        Assert.Equal(13, vm.MapZoom);
    }

    [Fact]
    public void ChangeMapView_clamps_zoom_to_the_configured_min_and_max()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(
            session,
            () => { },
            new FakeElevationSampler(),
            new FakeTileSource(),
            initialMinZoom: 11);

        vm.ChangeMapViewCommand.Execute(new MapViewChange(48.2, 11.3, 5));
        Assert.Equal(11, vm.MapZoom);

        vm.ChangeMapViewCommand.Execute(new MapViewChange(48.2, 11.3, 25));
        Assert.Equal(19, vm.MapZoom);
    }

    // #150 follow-up: with no drag-to-pan and no way back, zooming (cursor-anchored, so it shifts
    // the center) could drift the operator's view off the region's tiles entirely with no way to
    // recover. ResetMapViewCommand must restore the view exactly where it started.
    [Fact]
    public void ResetMapView_restores_the_initial_center_and_zoom_after_drifting_away()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(
            session,
            () => { },
            new FakeElevationSampler(),
            new FakeTileSource(),
            initialMapCenter: new GeoPoint(48.19, 11.15),
            initialMapZoom: 13,
            initialMinZoom: 11);

        vm.ChangeMapViewCommand.Execute(new MapViewChange(50.0, 20.0, 19));
        Assert.Equal(50.0, vm.MapCenterLatitude);

        vm.ResetMapViewCommand.Execute(null);

        Assert.Equal(48.19, vm.MapCenterLatitude);
        Assert.Equal(11.15, vm.MapCenterLongitude);
        Assert.Equal(13, vm.MapZoom);
    }

    [Fact]
    public void ResetMapView_restores_the_hardcoded_default_when_no_initial_view_was_given()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());

        vm.ChangeMapViewCommand.Execute(new MapViewChange(50.0, 20.0, 19));
        vm.ResetMapViewCommand.Execute(null);

        Assert.Equal(48.14, vm.MapCenterLatitude);
        Assert.Equal(11.58, vm.MapCenterLongitude);
        Assert.Equal(14, vm.MapZoom);
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

    // Selecting an existing (Plan B) route in the grid must show it on the map -- previously
    // nothing wired DataGrid.SelectedItem to anything, so it silently did nothing.
    [Fact]
    public void Selecting_a_row_with_a_saved_route_exposes_it_and_switches_to_map_mode()
    {
        var (session, _) = NewSession();
        var sampler = new FakeElevationSampler();
        var vm = new WasserfoerderungViewModel(session, () => { }, sampler, new FakeTileSource());
        var route = new[] { new GeoPoint(48.0, 11.0), new GeoPoint(48.002, 11.0) };
        vm.AddRoutePointCommand.Execute(route[0]);
        vm.AddRoutePointCommand.Execute(route[1]);
        vm.FinishRouteCommand.Execute(null);
        vm.IsMapMode = false; // simulate having switched back to MANUELL after finishing

        vm.SelectedRow = Assert.Single(vm.Rows);

        Assert.Equal(route, vm.SelectedRoutePoints);
        Assert.True(vm.IsMapMode);
    }

    [Fact]
    public void Selecting_a_manual_row_without_a_route_exposes_no_selected_route_and_does_not_force_map_mode()
    {
        var (session, _) = NewSession();
        var vm = new WasserfoerderungViewModel(session, () => { }, new FakeElevationSampler(), new FakeTileSource());
        session.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 100);

        vm.SelectedRow = Assert.Single(vm.Rows);

        Assert.Null(vm.SelectedRoutePoints);
        Assert.False(vm.IsMapMode);
    }

    [Fact]
    public void Deselecting_the_row_clears_the_selected_route()
    {
        var (session, _) = NewSession();
        var sampler = new FakeElevationSampler();
        var vm = new WasserfoerderungViewModel(session, () => { }, sampler, new FakeTileSource());
        var route = new[] { new GeoPoint(48.0, 11.0), new GeoPoint(48.002, 11.0) };
        vm.AddRoutePointCommand.Execute(route[0]);
        vm.AddRoutePointCommand.Execute(route[1]);
        vm.FinishRouteCommand.Execute(null);
        vm.SelectedRow = Assert.Single(vm.Rows);

        vm.SelectedRow = null;

        Assert.Null(vm.SelectedRoutePoints);
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
