using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

public sealed class LagekarteRenderTests : IDisposable
{
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;
    private readonly HttpClient _http = new();

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [AvaloniaFact]
    public void The_map_shows_the_Einsatzort_and_the_placed_units_and_lists_every_unit()
    {
        var (window, view, vm, session) = Show();
        session.SetIncidentLocation(Marktplatz);
        session.PlaceForceUnit(session.Incident.Forces[0].Id, new GeoPoint(48.1792, 11.2541));
        session.PlaceForceUnit(session.Incident.Forces[1].Id, new GeoPoint(48.1779, 11.2572));
        vm.CenterOnEinsatzortCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var panel = view.GetVisualDescendants().OfType<MapMarkerPanel>().Single();
        Assert.Equal(3, panel.Children.Count);
        Assert.Equal(3, view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "UnitList").ItemCount);
        Assert.False(view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NoMapBanner").IsVisible);
        Assert.NotNull(view.GetVisualDescendants().OfType<MapCanvasControl>().Single().TileSource);

        // The PR screenshot.
        var shots = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(shots);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(shots, "fuehrung-karte.png"));
    }

    [AvaloniaFact]
    public void A_pack_with_vector_tiles_is_drawn_from_them_with_its_Einsatzort_units_and_hydranten()
    {
        var (window, view, vm, session) = Show(vectorMap: true);
        WaitForWaterPoints(vm);
        session.SetIncidentLocation(Marktplatz);
        session.PlaceForceUnit(session.Incident.Forces[0].Id, new GeoPoint(48.1792, 11.2541));
        session.PlaceForceUnit(session.Incident.Forces[1].Id, new GeoPoint(48.1779, 11.2572));
        vm.CenterOnEinsatzortCommand.Execute(null);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        var layer = Assert.Single(Assert.IsType<Persistence.Map.CombinedMapTileSource>(vm.Map.TileSource).Layers);
        Assert.IsType<Persistence.Map.Vector.PmTilesFileSource>(layer.Tiles);
        Assert.Null(layer.Boundary);
        Assert.Equal(3, view.GetVisualDescendants().OfType<MapMarkerPanel>().Single().Children.Count);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-vektor.png"));
    }

    [AvaloniaFact]
    public void Close_up_the_packs_hydranten_are_drawn_and_the_note_says_they_are_from_OSM()
    {
        var (window, view, vm, _) = Show();
        WaitForWaterPoints(vm);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, view.GetVisualDescendants().OfType<MapCanvasControl>().Single().WaterPoints?.Count);
        Assert.True(view.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "WaterPointsToggle").IsVisible);
        var note = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "WaterPointsNote");
        Assert.True(note.IsVisible);
        Assert.Equal("Hydranten aus OpenStreetMap – nicht amtlich.", note.Text);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-hydranten.png"));
    }

    [AvaloniaFact]
    public void Clicking_a_hydrant_opens_its_card_and_the_legend_explains_the_symbols()
    {
        var (window, view, vm, session) = Show();
        WaitForWaterPoints(vm);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var water = vm.WaterPoints;
        Assert.NotNull(water);
        var hydrant = water.First(w => w.Label == "ÜH 150");
        var at = map.TranslatePoint(MapDrawing.ToScreen(vm.Map.Center, vm.Map.Zoom, map.Bounds.Size, hydrant.Position), window)
            ?? throw new InvalidOperationException("Not in the window.");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(hydrant, vm.SelectedWaterPoint);
        Assert.Same(hydrant, map.SelectedWaterPoint);
        Assert.True(view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WaterPointCard").IsVisible);
        Assert.Equal(2, view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "WaterPointDetails").ItemCount);
        Assert.True(view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WaterLegend").IsVisible);
        Assert.Null(session.Incident.Location);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-hydrant-karte.png"));
    }

    [AvaloniaFact]
    public async Task Searching_an_address_and_clicking_the_hit_marks_the_Einsatzort_there()
    {
        var (window, view, vm, session) = Show();
        var box = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AddressQueryBox");
        box.Text = "Musterstr 1";
        await vm.SearchAddressCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "AddressHitList");
        Assert.Equal(3, list.ItemCount);
        using (var frame = window.CaptureRenderedFrame()!)
        {
            frame.SavePng(Path.Join(Shots(), "fuehrung-karte-adresse.png"));
        }

        var first = list.GetVisualDescendants().OfType<Button>().First();
        Assert.Equal("Musterstraße 1, 82256 Musterstadt", Avalonia.Automation.AutomationProperties.GetName(first));
        var at = MiddleOf(first, window);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(FictionalRegionPack.Musterstrasse1, session.Incident.Location);
        Assert.Equal(0, list.ItemCount);
    }

    [AvaloniaFact]
    public void Picking_a_unit_and_clicking_the_map_puts_it_there()
    {
        var (window, view, vm, session) = Show();
        vm.PlaceUnitCommand.Execute(vm.Units[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PlacingBanner").IsVisible);

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var middle = MiddleOf(map, window);
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);

        var placed = session.Incident.Forces.Single(u => u.Id == vm.Units[0].UnitId).Position;
        Assert.NotNull(placed);
        Assert.Equal(vm.Map.Center.Latitude, placed.Latitude, precision: 5);
        Assert.Equal(vm.Map.Center.Longitude, placed.Longitude, precision: 5);
        Assert.False(vm.IsPlacing);
    }

    [AvaloniaFact]
    public void A_click_without_a_pick_places_nothing_and_a_drag_only_pans()
    {
        var (window, view, vm, session) = Show();
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var middle = MiddleOf(map, window);
        var before = vm.Map.Center;

        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        vm.PlaceEinsatzortCommand.Execute(null);
        window.MouseDown(middle, MouseButton.Left);
        window.MouseMove(new Point(middle.X + 60, middle.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(middle.X + 60, middle.Y), MouseButton.Left);

        Assert.Null(session.Incident.Location);
        Assert.True(vm.IsPlacing);
        Assert.True(vm.Map.Center.Longitude < before.Longitude);
    }

    [AvaloniaFact]
    public void Dragging_a_mark_moves_the_unit_and_letting_go_stores_it()
    {
        var (window, view, vm, session) = Show();
        var unitId = session.Incident.Forces[0].Id;
        session.PlaceForceUnit(unitId, Marktplatz);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 16));
        Dispatcher.UIThread.RunJobs();

        var mark = view.GetVisualDescendants().OfType<MapMarkerPanel>().Single().Children.Single();
        var start = MiddleOf(mark, window);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 40, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 80, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X + 80, start.Y), MouseButton.Left);

        var moved = session.Incident.Forces.Single(u => u.Id == unitId).Position;
        Assert.NotNull(moved);
        Assert.True(moved.Longitude > Marktplatz.Longitude);
        Assert.Equal(Marktplatz.Latitude, moved.Latitude, precision: 4);
        Assert.Equal(Marktplatz, vm.Map.Center);
    }

    [AvaloniaFact]
    public void Fuehrung_has_a_KARTE_tab_that_shows_the_Lagekarte()
    {
        var session = NewSession();
        var workspace = new IncidentWorkspaceViewModel(
            session, new FixedClock(), new NoopTicker(), MasterDataSet.Empty, new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var view = new LeadershipTabsView { DataContext = workspace.LeadershipTabs };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        workspace.LeadershipTabs.SelectedTabIndex = LeadershipTabsViewModel.MapTab;
        Dispatcher.UIThread.RunJobs();

        var tab = view.GetVisualDescendants().OfType<TabItem>().Single(t => t.Name == "MapTab");
        Assert.Equal("KARTE", tab.Header);
        Assert.Single(view.GetVisualDescendants().OfType<LagekarteView>());
        Assert.Same(workspace.Lagekarte, workspace.LeadershipTabs.Map);
        workspace.Dispose();
    }

    private (Window Window, LagekarteView View, LagekarteViewModel Vm, LocalIncidentSession Session) Show(bool vectorMap = false)
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root, vectorMap);
        var session = NewSession();
        session.AddForceUnit("FF Musterstadt", 9, "Florian Musterstadt 40/1", "Im Einsatz");
        session.AddForceUnit("FF Musterstadt", 3, "Florian Musterstadt 11/1", "Im Einsatz");
        session.AddForceUnit("FF Musterdorf", 9, "Florian Musterdorf 42/1", "Auf Anfahrt");
        var vm = new LagekarteViewModel(session, () => { }, new RegionPackLibrary(_http, root));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 16));
        var view = new LagekarteView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, session);
    }

    private static string Shots()
    {
        var shots = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(shots);
        return shots;
    }

    // The pack's water.geojson is read off the UI thread; its result arrives as a dispatcher job.
    private static void WaitForWaterPoints(LagekarteViewModel vm) =>
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return vm.WaterPoints is not null || vm.WaterPointsError is not null;
            },
            TimeSpan.FromSeconds(10)));

    private static Point MiddleOf(Visual visual, Window window) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Not in the window.");

    private static LocalIncidentSession NewSession() => TestSession.StartNew(
        new FakeStore(),
        new FixedClock(),
        new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
        "/x.lagebuch",
        Array.Empty<(string, bool)>(),
        Array.Empty<(string, bool)>());
}
