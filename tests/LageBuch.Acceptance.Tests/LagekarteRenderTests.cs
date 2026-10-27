using Avalonia;
using Avalonia.Controls;
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

    private (Window Window, LagekarteView View, LagekarteViewModel Vm, LocalIncidentSession Session) Show()
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root);
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
