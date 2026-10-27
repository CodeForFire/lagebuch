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
        Assert.Equal("LF 10/1 auf der Karte", marker.AutomationName);
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
