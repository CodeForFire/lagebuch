using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;

namespace LageBuch.AppLogic.Tests;

public class LagekarteViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);
    private static readonly GeoPoint Amperbruecke = new(48.17633, 11.25881);
    private static readonly AddressHit Hauptstrasse12 = new("Hauptstraße", "12", "82256", "Fürstenfeldbruck", string.Empty, Marktplatz);

    [Fact]
    public void A_tap_after_picking_the_Einsatzort_marks_it_there()
    {
        var (session, vm, changes) = Open();

        vm.PlaceEinsatzortCommand.Execute(null);
        Assert.Equal("Auf die Karte tippen: Einsatzort platzieren", vm.PlacingHint);
        vm.MapTappedCommand.Execute(Marktplatz);

        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.False(vm.IsPlacing);
        Assert.True(vm.HasEinsatzort);
        Assert.Equal("EINSATZORT VERSETZEN", vm.EinsatzortLabel);
        Assert.True(Assert.Single(vm.Markers).IsEinsatzort);
        Assert.Equal(1, changes());
    }

    [Fact]
    public void A_tap_with_nothing_picked_places_nothing()
    {
        var (session, vm, changes) = Open();

        vm.MapTappedCommand.Execute(Marktplatz);

        Assert.Null(session.Incident.Location);
        Assert.Empty(vm.Markers);
        Assert.Equal(0, changes());
    }

    [Fact]
    public void A_unit_picked_from_the_list_is_placed_by_the_next_tap()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        var row = Assert.Single(vm.Units);
        Assert.Equal("AUF KARTE", row.PlaceLabel);

        vm.PlaceUnitCommand.Execute(row);
        vm.MapTappedCommand.Execute(Amperbruecke);

        Assert.Equal(Amperbruecke, session.Incident.Forces.Single().Position);
        var marker = Assert.Single(vm.Markers);
        Assert.Equal("LF 10/1", marker.Title);
        Assert.Equal("LF 10/1: Standort", marker.AutomationName);
        Assert.Equal("VERSETZEN", vm.Units.Single().PlaceLabel);
    }

    [Fact]
    public void A_pick_can_be_placed_in_the_middle_of_the_map_instead()
    {
        var (session, vm, _) = Open();
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Amperbruecke, 16));

        vm.PlaceEinsatzortCommand.Execute(null);
        vm.PlaceAtCenterCommand.Execute(null);

        Assert.Equal(Amperbruecke, session.Incident.Location);
    }

    [Fact]
    public void A_pick_that_is_cancelled_places_nothing()
    {
        var (session, vm, _) = Open();

        vm.PlaceEinsatzortCommand.Execute(null);
        vm.CancelPlacingCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);

        Assert.False(vm.IsPlacing);
        Assert.Null(session.Incident.Location);
    }

    [Fact]
    public void A_dragged_marker_moves_the_unit_it_stands_for()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);

        vm.MoveMarker(vm.Markers.Single(), Amperbruecke);

        Assert.Equal(Amperbruecke, session.Incident.Forces.Single().Position);
        Assert.Equal(Amperbruecke, vm.Markers.Single().Position);
    }

    [Fact]
    public void Marker_drag_keeps_the_grab_offset_and_stores_the_release_after_crossing_the_threshold()
    {
        var (session, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        var marker = vm.Markers.Single();
        vm.BeginMarkerDrag(marker, (20, 10), (20, 30));

        Assert.False(vm.ContinueMarkerDrag((25, 15), ToGeo));
        Assert.Equal(Marktplatz, marker.Position);
        Assert.True(vm.ContinueMarkerDrag((26, 10), ToGeo));
        Assert.Equal(new GeoPoint(30, 26), marker.Position);
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.True(vm.EndMarkerDrag((20, 10), ToGeo));

        Assert.Equal(new GeoPoint(30, 20), session.Incident.Location);
        Assert.False(vm.EndMarkerDrag((20, 10), ToGeo));
    }

    [Fact]
    public void Marker_release_below_the_threshold_taps_at_the_hand_without_the_grab_offset()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.BeginMarkerDrag(vm.Markers.Single(), (20, 10), (20, 30));

        Assert.False(vm.ContinueMarkerDrag((25, 15), ToGeo));
        Assert.True(vm.EndMarkerDrag((25, 15), ToGeo));

        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.Equal(new GeoPoint(15, 25), session.Incident.Forces.Single().Position);
    }

    [Fact]
    public void Cancelling_a_marker_drag_restores_the_preview_and_ignores_release()
    {
        var (session, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.BeginMarkerDrag(vm.Markers.Single(), (20, 10), (20, 30));
        Assert.True(vm.ContinueMarkerDrag((26, 10), ToGeo));

        vm.CancelMarkerDrag();

        Assert.Equal(Marktplatz, vm.Markers.Single().Position);
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.False(vm.EndMarkerDrag((26, 10), ToGeo));
    }

    [Fact]
    public void A_closed_Einsatz_treats_a_marker_drag_as_a_tap_without_moving_the_marker()
    {
        var (session, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        session.Close();
        using var closed = new LagekarteViewModel(session, () => { });
        closed.BeginMarkerDrag(closed.Markers.Single(), (20, 10), (20, 30));

        Assert.False(closed.ContinueMarkerDrag((40, 10), ToGeo));
        Assert.True(closed.EndMarkerDrag((40, 10), ToGeo));
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.Equal(Marktplatz, closed.Markers.Single().Position);
    }

    private static GeoPoint ToGeo(double x, double y) => new(y, x);

    [Fact]
    public void Dragging_a_pin_preserves_the_grab_offset_in_preview_and_on_release()
    {
        var (session, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        var marker = vm.Markers.Single();
        static GeoPoint Convert(double x, double y) => new(y, x);

        vm.BeginMarkerDrag(marker, (20, 10), (20, 30));
        Assert.False(vm.ContinueMarkerDrag((25, 15), Convert));
        Assert.Equal(Marktplatz, marker.Position);
        Assert.True(vm.ContinueMarkerDrag((26, 10), Convert));
        Assert.Equal(new GeoPoint(30, 26), marker.Position);
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.True(vm.EndMarkerDrag((30, 12), Convert));

        Assert.Equal(new GeoPoint(32, 30), session.Incident.Location);
        Assert.False(vm.EndMarkerDrag((30, 12), Convert));
    }

    [Fact]
    public void Releasing_an_unmoved_pin_taps_at_the_pointer_not_the_pin_spot()
    {
        var (session, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.BeginMarkerDrag(vm.Markers.Single(), (20, 10), (20, 30));
        Assert.False(vm.ContinueMarkerDrag((25, 15), (x, y) => new GeoPoint(y, x)));

        vm.EndMarkerDrag((25, 15), (x, y) => new GeoPoint(y, x));

        Assert.Equal(new GeoPoint(15, 25), session.Incident.Location);
    }

    [Fact]
    public void Losing_capture_restores_a_pin_preview_and_discards_the_drag()
    {
        var (session, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.BeginMarkerDrag(vm.Markers.Single(), (20, 10), (20, 30));
        vm.ContinueMarkerDrag((30, 10), (_, _) => Amperbruecke);

        vm.CancelMarkerDrag();

        Assert.Equal(Marktplatz, vm.Markers.Single().Position);
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.False(vm.EndMarkerDrag((30, 10), (_, _) => Amperbruecke));
    }

    [Fact]
    public void Sign_drag_uses_the_resolved_offset_and_restores_an_abandoned_preview()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.BeginSignDrag(vm.Signs.Single(), (20, 20), new LabelOffset(-80, 30));
        Assert.False(vm.UpdateSignDrag((25, 25)));
        Assert.True(vm.UpdateSignDrag((30, 26)));
        Assert.Equal(new LabelOffset(-70, 36), vm.Signs.Single().LabelOffset);
        vm.CancelSignDrag();
        Assert.Null(vm.Signs.Single().LabelOffset);
        Assert.False(vm.EndSignDrag((30, 26), (_, _) => Amperbruecke));

        vm.BeginSignDrag(vm.Signs.Single(), (20, 20), new LabelOffset(-80, 30));
        vm.UpdateSignDrag((30, 26));
        vm.EndSignDrag((30, 26), (_, _) => Amperbruecke);

        Assert.Equal(new LabelOffset(-70, 36), session.Incident.Forces.Single().LabelOffset);
        Assert.Equal(Marktplatz, session.Incident.Forces.Single().Position);
    }

    [Fact]
    public void An_abandoned_drag_puts_the_marker_back()
    {
        var (_, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);

        vm.Markers.Single().Position = Amperbruecke;
        vm.RestoreMarkers();

        Assert.Equal(Marktplatz, vm.Markers.Single().Position);
    }

    [Fact]
    public void Taking_a_unit_and_the_Einsatzort_off_the_map_removes_their_marks()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Amperbruecke);

        vm.RemoveUnitCommand.Execute(vm.Units[0]);
        vm.RemoveEinsatzortCommand.Execute(null);

        Assert.Empty(vm.Markers);
        Assert.Null(session.Incident.Location);
        Assert.Null(session.Incident.Forces.Single().Position);
    }

    [Fact]
    public void A_placed_unit_gets_a_pin_and_a_sign_that_are_one_marker()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        session.SetForceUnitVehicleType(session.Incident.Forces[0].Id, "HLF");
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);

        var pin = Assert.Single(vm.Markers);
        Assert.Same(pin, Assert.Single(vm.Signs));
        Assert.Equal("HLF", pin.VehicleType);
        Assert.Equal("0/0/6/6", pin.Strength);
        Assert.Null(pin.LabelOffset); // the map finds it a free place until it is dragged
        Assert.Equal("LF 10/1: taktisches Zeichen", pin.SignAutomationName);
    }

    [Fact]
    public void The_Einsatzort_has_a_pin_but_no_sign()
    {
        var (_, vm, _) = Open();
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);

        Assert.Equal("Einsatzort", Assert.Single(vm.Markers).AutomationName);
        Assert.Empty(vm.Signs);
        Assert.Equal(Marktplatz, vm.EinsatzortPosition);
    }

    [Fact]
    public void Without_an_Einsatzort_there_is_no_pin_head_to_keep_clear_of()
    {
        var (_, vm, _) = Open();

        Assert.Null(vm.EinsatzortPosition);
    }

    [Fact]
    public void The_pins_the_signs_keep_clear_of_are_every_mark_on_the_map()
    {
        var (_, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Amperbruecke);

        Assert.Equal([Marktplatz, Amperbruecke], vm.PinPositions);
    }

    [Fact]
    public void A_unit_row_names_the_feuerwehr_and_status_under_the_funkrufname_or_only_the_status_without_one()
    {
        var withCallSign = ForceUnit.Create("FF Musterstadt", 9, callSign: "LF 10/1", status: "Im Einsatz");
        var withoutCallSign = ForceUnit.Create("FF Musterstadt", 9, status: "Im Einsatz");
        var bare = ForceUnit.Create("FF Musterstadt", 9);

        Assert.Equal("FF Musterstadt · Im Einsatz", new LagekarteUnitRow(withCallSign).Detail);
        Assert.Equal("Im Einsatz", new LagekarteUnitRow(withoutCallSign).Detail);
        Assert.Equal("FF Musterstadt", new LagekarteUnitRow(withoutCallSign).Title);
        Assert.Equal(string.Empty, new LagekarteUnitRow(bare).Detail);
    }

    [Fact]
    public void Dragging_a_sign_moves_only_the_sign_and_keeps_the_unit_where_it_stands()
    {
        var (session, vm, changes) = Open(units: ["LF 10/1"]);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);
        var before = changes();

        vm.MoveLabel(vm.Signs.Single(), new LabelOffset(-80, 30));

        var unit = session.Incident.Forces.Single();
        Assert.Equal(new LabelOffset(-80, 30), unit.LabelOffset);
        Assert.Equal(Marktplatz, unit.Position);
        Assert.Equal(new LabelOffset(-80, 30), vm.Signs.Single().LabelOffset);
        Assert.Equal(before + 1, changes());
    }

    [Fact]
    public void Dragging_the_pin_keeps_the_sign_s_offset()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);
        vm.MoveLabel(vm.Signs.Single(), new LabelOffset(-80, 30));

        vm.MoveMarker(vm.Markers.Single(), Amperbruecke);

        Assert.Equal(new LabelOffset(-80, 30), session.Incident.Forces.Single().LabelOffset);
        Assert.Equal(Amperbruecke, vm.Signs.Single().Position);
    }

    [Fact]
    public void An_abandoned_sign_drag_puts_the_sign_back()
    {
        var (_, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);

        vm.Signs.Single().LabelOffset = new LabelOffset(300, 300);
        vm.RestoreMarkers();

        Assert.Null(vm.Signs.Single().LabelOffset);
    }

    [Fact]
    public void A_closed_Einsatz_takes_no_sign_move()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        vm.MapTappedCommand.Execute(Marktplatz);
        session.Close();
        using var closed = new LagekarteViewModel(session, () => { });

        closed.MoveLabel(closed.Signs.Single(), new LabelOffset(-80, 30));

        Assert.Null(session.Incident.Forces.Single().LabelOffset);
    }

    [Fact]
    public void The_units_are_listed_by_Funkrufname()
    {
        var (_, vm, _) = Open(units: ["RW 1", "ELW 1", "LF 10/1"]);

        Assert.Equal(["ELW 1", "LF 10/1", "RW 1"], vm.Units.Select(u => u.Title));
        Assert.False(vm.HasNoUnits);
    }

    [Fact]
    public void A_closed_Einsatz_shows_its_map_but_takes_no_marks()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);
        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);
        session.Close();
        using var closed = new LagekarteViewModel(session, () => { });

        closed.PlaceEinsatzortCommand.Execute(null);
        closed.MoveMarker(closed.Markers.Single(), Amperbruecke);

        Assert.False(closed.CanEdit);
        Assert.False(closed.IsPlacing);
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.Equal(Marktplatz, closed.Map.Center);
        Assert.Equal(LagekarteViewModel.EinsatzortZoom, closed.Map.Zoom);
    }

    [Fact]
    public void Without_a_pack_the_map_says_so_and_still_takes_marks()
    {
        var (session, vm, _) = Open();

        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);

        Assert.True(vm.HasNoMap);
        Assert.Null(vm.Map.TileSource);
        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public void Every_installed_pack_is_one_map_with_each_licence_line_once()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"));
        library.Preinstall(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg") with { South = 47.8, North = 48.0 });
        library.Preinstall(FakeRegionPackLibrary.Pack("ll", "Landkreis Landsberg am Lech") with { Attribution = "© OSM, DGM5 © LDBV" });
        var (session, _, _) = Open();
        session.SetIncidentLocation(Marktplatz);

        using var vm = new LagekarteViewModel(session, () => { }, library);

        Assert.False(vm.HasNoMap);
        Assert.IsType<CombinedMapTileSource>(vm.Map.TileSource);
        Assert.Equal("© OpenStreetMap contributors © OSM, DGM5 © LDBV", vm.Map.Attribution);
        Assert.Equal(Marktplatz, vm.Map.Center);
        Assert.Equal(LagekarteViewModel.EinsatzortZoom, vm.Map.Zoom);
    }

    [Fact]
    public void The_water_points_of_every_pack_are_drawn_and_one_unreadable_pack_costs_only_its_own()
    {
        var library = FfbLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"));
        library.Preinstall(FakeRegionPackLibrary.Pack("ll", "Landkreis Landsberg am Lech"));
        library.WaterPoints["ffb"] = [new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100")];
        library.WaterPoints["sta"] = [new WaterPoint(Amperbruecke, WaterPointKind.Saugstelle, "Würm")];
        library.WaterFailure = new RegionPackException("Die Wasserentnahmestellen des Kartenpakets sind beschädigt.");
        library.WaterFailureSlug = "ll";
        var (session, _, _) = Open();

        using var vm = new LagekarteViewModel(session, () => { }, library);

        Assert.Equal(["UH 100", "Würm"], vm.WaterPoints?.Select(w => w.Label));
        Assert.Equal("Landkreis Landsberg am Lech: Die Wasserentnahmestellen des Kartenpakets sind beschädigt.", vm.WaterPointsError);
    }

    [Fact]
    public void A_pack_installed_while_the_Einsatz_is_open_joins_the_map_and_the_view_stays()
    {
        var library = FfbLibrary();
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Amperbruecke, 15));
        var before = vm.Map.TileSource;

        library.Preinstall(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"));
        library.WaterPoints["sta"] = [new WaterPoint(Amperbruecke, WaterPointKind.Hydrant, "UH 80")];
        library.Addresses["sta"] = [Hauptstrasse12];
        vm.ReloadPacks();

        Assert.NotSame(before, vm.Map.TileSource);
        Assert.Equal(Amperbruecke, vm.Map.Center);
        Assert.Equal(15, vm.Map.Zoom);
        Assert.Equal(["UH 80"], vm.WaterPoints?.Select(w => w.Label));
    }

    [Fact]
    public void Reloading_the_same_packs_changes_nothing()
    {
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, FfbLibrary());
        var before = vm.Map.TileSource;

        vm.ReloadPacks();

        Assert.Same(before, vm.Map.TileSource);
    }

    [Fact]
    public void A_pack_deleted_while_the_Einsatz_is_open_leaves_the_map_saying_so()
    {
        var library = FfbLibrary();
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);

        library.Remove(library.GetInstalled().Single());
        vm.ReloadPacks();

        Assert.True(vm.HasNoMap);
        Assert.False(vm.CanSearchAddresses);
        Assert.Null(vm.Map.TileSource);
        Assert.Null(vm.WaterPoints);
    }

    [Fact]
    public void A_change_from_another_device_shows_up_without_a_reload()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);

        session.PlaceForceUnit(session.Incident.Forces[0].Id, Marktplatz);

        Assert.Single(vm.Markers);
        Assert.True(vm.Units.Single().IsPlaced);
    }

    [Fact]
    public void The_shown_packs_water_points_are_drawn_until_the_switch_is_turned_off()
    {
        var library = FfbLibrary();
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");
        library.WaterPoints["ffb"] = [hydrant];
        var (session, _, _) = Open();

        using var vm = new LagekarteViewModel(session, () => { }, library);

        Assert.True(vm.HasWaterPoints);
        Assert.Equal([hydrant], vm.VisibleWaterPoints);
        Assert.Equal("Hydranten aus OpenStreetMap – nicht amtlich.", vm.WaterPointsNote);

        vm.ShowWaterPoints = false;

        Assert.Null(vm.VisibleWaterPoints);
        Assert.Null(vm.WaterPointsNote);
        Assert.True(vm.HasWaterPoints);
    }

    [Fact]
    public void A_tap_on_a_water_point_opens_its_card_and_a_tap_beside_it_closes_the_card()
    {
        var library = FfbLibrary();
        var hydrant = new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100");
        library.WaterPoints["ffb"] = [hydrant];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));

        vm.MapTappedCommand.Execute(Marktplatz);
        Assert.Same(hydrant, vm.SelectedWaterPoint);
        Assert.True(vm.HasSelectedWaterPoint);

        vm.MapTappedCommand.Execute(Amperbruecke);
        Assert.Null(vm.SelectedWaterPoint);
        Assert.Null(session.Incident.Location);
    }

    [Fact]
    public void A_tap_while_placing_places_and_opens_no_card()
    {
        var library = FfbLibrary();
        library.WaterPoints["ffb"] = [new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100")];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));

        vm.PlaceEinsatzortCommand.Execute(null);
        vm.MapTappedCommand.Execute(Marktplatz);

        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.Null(vm.SelectedWaterPoint);
    }

    [Fact]
    public void Turning_the_water_points_off_closes_the_card_and_the_legend()
    {
        var library = FfbLibrary();
        library.WaterPoints["ffb"] = [new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100")];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        vm.MapTappedCommand.Execute(Marktplatz);
        Assert.True(vm.ShowsWaterLegend);

        vm.ShowWaterPoints = false;

        Assert.Null(vm.SelectedWaterPoint);
        Assert.False(vm.ShowsWaterLegend);
    }

    [Fact]
    public void The_legend_shows_only_where_the_symbols_are_drawn()
    {
        var library = FfbLibrary();
        library.WaterPoints["ffb"] = [new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100")];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, WaterPointLayer.MinZoom - 1));
        Assert.False(vm.ShowsWaterLegend);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, WaterPointLayer.MinZoom));

        Assert.True(vm.ShowsWaterLegend);
        Assert.Contains(nameof(LagekarteViewModel.ShowsWaterLegend), changes);
    }

    [Fact]
    public void The_poi_key_shows_from_the_zoom_the_map_names_pois()
    {
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, FfbLibrary());
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, PoiLayer.MinZoom - 1));
        Assert.False(vm.ShowsPoiLegend);
        Assert.False(vm.ShowsLegend);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, PoiLayer.MinZoom));

        Assert.True(vm.ShowsPoiLegend);
        Assert.True(vm.ShowsLegend);
        Assert.Contains(nameof(LagekarteViewModel.ShowsPoiLegend), changes);
        Assert.Contains(nameof(LagekarteViewModel.ShowsLegend), changes);
    }

    [Fact]
    public void Without_a_pack_there_are_no_pois_to_explain()
    {
        var library = new FakeRegionPackLibrary();
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Assert.False(vm.ShowsPoiLegend);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        library.Preinstall(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"));
        vm.ReloadPacks();

        Assert.Equal(17, vm.Map.Zoom);
        Assert.True(vm.ShowsPoiLegend);
        Assert.Contains(nameof(LagekarteViewModel.ShowsPoiLegend), changes);
    }

    [Fact]
    public void Turning_the_water_points_off_leaves_the_poi_key()
    {
        var library = FfbLibrary();
        library.WaterPoints["ffb"] = [new WaterPoint(Marktplatz, WaterPointKind.Hydrant, "UH 100")];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.ShowWaterPoints = false;

        Assert.False(vm.ShowsWaterLegend);
        Assert.True(vm.ShowsLegend);
        Assert.Contains(nameof(LagekarteViewModel.ShowsLegend), changes);
    }

    [Fact]
    public void Water_points_that_cannot_be_read_are_reported_and_the_map_stays()
    {
        var library = FfbLibrary();
        library.WaterFailure = new RegionPackException("Die Wasserentnahmestellen des Kartenpakets sind beschädigt.");
        var (session, _, _) = Open();

        using var vm = new LagekarteViewModel(session, () => { }, library);

        Assert.Equal("Die Wasserentnahmestellen des Kartenpakets sind beschädigt.", vm.WaterPointsError);
        Assert.False(vm.HasWaterPoints);
        Assert.Null(vm.WaterPointsNote);
        Assert.NotNull(vm.Map.TileSource);
    }

    [Fact]
    public void A_pack_without_water_points_shows_no_switch_and_no_note()
    {
        var (session, _, _) = Open();

        using var vm = new LagekarteViewModel(session, () => { }, FfbLibrary());

        Assert.False(vm.HasWaterPoints);
        Assert.Null(vm.WaterPointsNote);
        Assert.Null(vm.WaterPointsError);
    }

    [Fact]
    public async Task An_address_picked_from_the_search_marks_the_Einsatzort_there_with_one_ETB_entry()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        var changes = 0;
        using var vm = new LagekarteViewModel(session, () => changes++, library);
        var entries = session.Incident.Journal.Count;
        vm.AddressQuery = "Hauptstr 12";

        await vm.SearchAddressCommand.ExecuteAsync(null);
        var hit = Assert.Single(vm.AddressHits);
        Assert.Equal("Hauptstraße 12", hit.Title);
        Assert.Equal("82256 Fürstenfeldbruck", hit.Detail);
        Assert.Equal("Hauptstraße 12, 82256 Fürstenfeldbruck", hit.AutomationName);
        Assert.True(vm.HasAddressHits);
        Assert.Equal("Treffer antippen: setzt den Einsatzort.", vm.AddressHint);
        vm.ChooseAddressCommand.Execute(hit);

        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.Equal(entries + 1, session.Incident.Journal.Count);
        Assert.StartsWith("Einsatzort auf der Lagekarte gesetzt", session.Incident.Journal[^1].Text, StringComparison.Ordinal);
        Assert.Equal(1, changes);
        Assert.Empty(vm.AddressHits);
        Assert.False(vm.HasAddressHits);
        Assert.Equal(Marktplatz, vm.Map.Center);
        Assert.Equal(LagekarteViewModel.EinsatzortZoom, vm.Map.Zoom);
    }

    [Fact]
    public async Task On_a_closed_Einsatz_an_address_hit_is_only_shown()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        session.Close();
        using var vm = new LagekarteViewModel(session, () => { }, library) { AddressQuery = "Hauptstr" };

        await vm.SearchAddressCommand.ExecuteAsync(null);
        Assert.Equal("Treffer antippen: zeigt die Adresse.", vm.AddressHint);
        vm.ChooseAddressCommand.Execute(vm.AddressHits.Single());

        Assert.Null(session.Incident.Location);
        Assert.Equal(Marktplatz, vm.Map.Center);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so()
    {
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, FfbLibrary()) { AddressQuery = "Nirgendwo 1" };

        await vm.SearchAddressCommand.ExecuteAsync(null);

        Assert.Empty(vm.AddressHits);
        Assert.Equal("Keine Adresse gefunden. Straße und Hausnummer prüfen.", vm.AddressStatus);
    }

    [Fact]
    public async Task With_several_packs_the_shown_one_is_searched_first_and_each_hit_names_its_pack()
    {
        var library = FfbLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg") with { South = 47.8, North = 48.0 });
        library.Addresses["ffb"] = [Hauptstrasse12];
        library.Addresses["sta"] = [Hauptstrasse12 with { Postcode = "82319", City = "Starnberg" }];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library) { AddressQuery = "Hauptstraße 12" };

        await vm.SearchAddressCommand.ExecuteAsync(null);

        Assert.Equal(["ffb:Hauptstraße 12", "sta:Hauptstraße 12"], library.Searches);
        Assert.Equal(
            ["82256 Fürstenfeldbruck · Landkreis Fürstenfeldbruck", "82319 Starnberg · Landkreis Starnberg"],
            vm.AddressHits.Select(h => h.Detail));
    }

    [Fact]
    public async Task No_more_hits_are_listed_than_the_cap()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = Enumerable.Range(1, 30).Select(n => Hauptstrasse12 with { HouseNumber = n.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToList();
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library) { AddressQuery = "Hauptstraße" };

        await vm.SearchAddressCommand.ExecuteAsync(null);

        Assert.Equal(LagekarteViewModel.MaxAddressHits, vm.AddressHits.Count);
    }

    [Fact]
    public async Task An_empty_query_searches_nothing()
    {
        var library = FfbLibrary();
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library) { AddressQuery = "   " };

        await vm.SearchAddressCommand.ExecuteAsync(null);

        Assert.Empty(library.Searches);
        Assert.Null(vm.AddressStatus);
    }

    [Fact]
    public void The_search_starts_from_the_recorded_address_while_no_Einsatzort_is_marked()
    {
        var (session, _, _) = Open();
        session.SetAddress("Hauptstraße", "Buchenau", "12", "82256", "Fürstenfeldbruck");

        using var vm = new LagekarteViewModel(session, () => { }, FfbLibrary());

        Assert.True(vm.CanSearchAddresses);
        Assert.Equal("Hauptstraße 12 Fürstenfeldbruck", vm.AddressQuery);
    }

    [Fact]
    public void A_recorded_street_without_a_house_number_starts_the_search_with_the_street_alone()
    {
        var (session, _, _) = Open();
        session.SetAddress("Hauptstraße", null, null, "82256", "Fürstenfeldbruck");

        using var vm = new LagekarteViewModel(session, () => { }, FfbLibrary());

        Assert.Equal("Hauptstraße", vm.AddressQuery);
    }

    [Fact]
    public void Once_the_Einsatzort_is_marked_the_search_starts_empty_and_without_a_pack_there_is_none()
    {
        var (session, vm, _) = Open();
        session.SetAddress("Hauptstraße", null, "12");
        session.SetIncidentLocation(Marktplatz);

        using var marked = new LagekarteViewModel(session, () => { }, FfbLibrary());

        Assert.Equal(string.Empty, marked.AddressQuery);
        Assert.False(vm.CanSearchAddresses);
    }

    [Fact]
    public async Task A_saved_address_with_one_exact_hit_marks_the_Einsatzort_and_zooms_there()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12 with { HouseNumber = "12a", Position = Amperbruecke }, Hauptstrasse12];
        var (session, _, _) = Open();
        var changes = 0;
        using var vm = new LagekarteViewModel(session, () => changes++, library);
        session.SetAddress("Hauptstraße", null, "12", "82256", "Fürstenfeldbruck");
        var entries = session.Incident.Journal.Count;

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(["ffb:Hauptstraße 12 Fürstenfeldbruck"], library.Searches);
        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.Equal(entries + 1, session.Incident.Journal.Count);
        Assert.Equal(1, changes);
        Assert.Empty(vm.AddressHits);
        Assert.Equal(Marktplatz, vm.Map.Center);
        Assert.Equal(LagekarteViewModel.EinsatzortZoom, vm.Map.Zoom);
    }

    [Fact]
    public async Task A_changed_address_moves_the_Einsatzort_already_marked()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        session.SetIncidentLocation(Amperbruecke);
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", null, null);

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
        Assert.StartsWith("Einsatzort auf der Lagekarte verlegt", session.Incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_address_found_by_two_overlapping_packs_is_still_one_address()
    {
        var library = FfbLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg"));
        library.Addresses["ffb"] = [Hauptstrasse12];
        library.Addresses["sta"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", null, "Fürstenfeldbruck");

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public async Task A_saved_address_found_in_two_Orte_leaves_the_hits_to_pick_from()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12, Hauptstrasse12 with { Postcode = "82216", City = "Maisach", Position = Amperbruecke }];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", null, null);

        await vm.LocateRecordedAddressAsync();

        Assert.Null(session.Incident.Location);
        Assert.Equal(2, vm.AddressHits.Count);
        Assert.Equal("Hauptstraße 12", vm.AddressQuery);
    }

    [Fact]
    public async Task The_recorded_PLZ_and_Ort_pick_the_one_hit_that_fits()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12 with { Postcode = "82216", City = "Maisach", Position = Amperbruecke }, Hauptstrasse12];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", "82256", "Fürstenfeldbruck");

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public async Task The_recorded_PLZ_alone_picks_the_one_hit_that_fits()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12 with { Postcode = "82216", City = "Maisach", Position = Amperbruecke }, Hauptstrasse12];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", "82256", null);

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public async Task A_recorded_Ort_matches_the_Ortsteil_OSM_tags()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12 with { City = "Maisach", Position = Amperbruecke }, Hauptstrasse12 with { Suburb = "Buchenau" }];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", null, "buchenau");

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public async Task A_hit_without_PLZ_or_Ort_contradicts_neither()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12 with { Postcode = string.Empty, City = string.Empty }];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", "82256", "Fürstenfeldbruck");

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public async Task One_address_tagged_twice_in_different_case_is_still_one_address()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12, Hauptstrasse12 with { HouseNumber = "12", City = "FÜRSTENFELDBRUCK", Position = Amperbruecke }];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "12", null, null);

        await vm.LocateRecordedAddressAsync();

        Assert.Equal(Marktplatz, session.Incident.Location);
    }

    [Fact]
    public async Task A_house_number_found_only_as_a_prefix_is_not_taken_for_the_address()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, "1", null, null);

        await vm.LocateRecordedAddressAsync();

        Assert.Null(session.Incident.Location);
        Assert.Single(vm.AddressHits);
    }

    [Fact]
    public async Task A_saved_street_without_a_house_number_only_lists_the_hits()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        using var vm = new LagekarteViewModel(session, () => { }, library);
        session.SetAddress("Hauptstraße", null, null, null, null);

        await vm.LocateRecordedAddressAsync();

        Assert.Null(session.Incident.Location);
        Assert.Single(vm.AddressHits);
    }

    [Fact]
    public async Task Without_a_Kartenpaket_a_saved_address_marks_nothing()
    {
        var (session, vm, changes) = Open();
        session.SetAddress("Hauptstraße", null, "12", null, null);

        await vm.LocateRecordedAddressAsync();

        Assert.Null(session.Incident.Location);
        Assert.Equal(0, changes());
    }

    [Fact]
    public async Task A_closed_Einsatz_does_not_mark_the_Einsatzort_from_its_address()
    {
        var library = FfbLibrary();
        library.Addresses["ffb"] = [Hauptstrasse12];
        var (session, _, _) = Open();
        session.SetAddress("Hauptstraße", null, "12", null, null);
        session.Close();
        using var vm = new LagekarteViewModel(session, () => { }, library);

        await vm.LocateRecordedAddressAsync();

        Assert.Null(session.Incident.Location);
        Assert.Empty(library.Searches);
    }

    [Fact]
    public void A_pack_with_heights_offers_the_terrain_and_its_key_joins_the_map_s_legend()
    {
        var root = Directory.CreateTempSubdirectory("lagebuch-terrain-").FullName;
        try
        {
            var library = new FakeRegionPackLibrary();
            library.Preinstall(TerrainFixture.Pack(root));
            var (session, _, _) = Open();
            using var vm = new LagekarteViewModel(session, () => { }, library);
            vm.Map.ChangeViewCommand.Execute(new MapViewChange(TerrainFixture.Marktplatz, 15));
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            Assert.True(vm.Terrain.HasElevation);
            vm.Terrain.Layer = TerrainLayer.Hangneigung;

            Assert.True(vm.ShowsLegend);
            Assert.Contains(nameof(LagekarteViewModel.ShowsLegend), raised);

            raised.Clear();
            vm.Terrain.Layer = TerrainLayer.Hoehenlinien;
            Assert.DoesNotContain(nameof(LagekarteViewModel.ShowsLegend), raised);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_terrain_measures_from_the_Einsatzort_as_it_is_marked_and_moved()
    {
        var (session, vm, _) = Open();

        Assert.Null(vm.Terrain.Einsatzort);
        session.SetIncidentLocation(Marktplatz);
        Assert.Equal(Marktplatz, vm.Terrain.Einsatzort);
        session.SetIncidentLocation(Amperbruecke);
        Assert.Equal(Amperbruecke, vm.Terrain.Einsatzort);
    }

    private static FakeRegionPackLibrary FfbLibrary()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"));
        return library;
    }

    private static (LocalIncidentSession Session, LagekarteViewModel Vm, Func<int> Changes) Open(string[]? units = null)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        foreach (var callSign in units ?? [])
        {
            session.AddForceUnit("FF Musterstadt", 6, callSign);
        }

        var changes = 0;
        var vm = new LagekarteViewModel(session, () => changes++);
        return (session, vm, () => changes);
    }
}
