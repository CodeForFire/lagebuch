using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class LagekarteViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);
    private static readonly GeoPoint Amperbruecke = new(48.17633, 11.25881);

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
    public void The_map_shows_the_pack_that_covers_the_Einsatzort()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg") with { MinLatitude = 47.8, MaxLatitude = 48.0 });
        library.Preinstall(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"));
        var (session, _, _) = Open();
        session.SetIncidentLocation(Marktplatz);

        using var vm = new LagekarteViewModel(session, () => { }, library);

        Assert.False(vm.HasNoMap);
        Assert.Equal("© OpenStreetMap contributors", vm.Map.Attribution);
        Assert.Equal(Marktplatz, vm.Map.Center);
    }

    [Fact]
    public void A_change_from_another_device_shows_up_without_a_reload()
    {
        var (session, vm, _) = Open(units: ["LF 10/1"]);

        session.PlaceForceUnit(session.Incident.Forces[0].Id, Marktplatz);

        Assert.Single(vm.Markers);
        Assert.True(vm.Units.Single().IsPlaced);
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
