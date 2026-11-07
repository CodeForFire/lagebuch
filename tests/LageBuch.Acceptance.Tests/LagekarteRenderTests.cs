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

        var panel = Pins(view);
        Assert.Equal(3, panel.Children.Count);
        Assert.Equal(3, view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "UnitList").ItemCount);
        Assert.False(view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NoMapBanner").IsVisible);
        var layer = Assert.Single(Assert.IsType<Persistence.Map.CombinedMapTileSource>(vm.Map.TileSource).Layers);
        Assert.IsType<Persistence.Map.Vector.PmTilesFileSource>(layer);

        // The PR screenshot.
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte.png"));
    }

    [AvaloniaFact]
    public void A_turned_map_keeps_its_marks_on_their_spots_and_offers_the_way_back_to_north()
    {
        var (window, view, vm, session) = Show();
        WaitForWaterPoints(vm);
        session.SetIncidentLocation(Marktplatz);
        var unitAt = new GeoPoint(48.1792, 11.2541);
        session.PlaceForceUnit(session.Incident.Forces[0].Id, unitAt);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();
        var north = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FaceNorthButton");
        Assert.False(north.IsVisible);

        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17, 30));
        Dispatcher.UIThread.RunJobs();

        // The unit's mark sits where the turned map puts its position, and stays upright.
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var panel = Pins(view);
        var expected = MapDrawing.ToScreen(Marktplatz, 17, map.Bounds.Size, unitAt, 30);
        var unit = panel.Children.Single(c => MapMarkerPanel.GetPosition(c) == unitAt);

        // Within a pixel: layout rounds the mark's position to whole pixels.
        Assert.True(Math.Abs(expected.X - unit.Bounds.Center.X) <= 1, $"x {unit.Bounds.Center.X}, expected {expected.X}");
        Assert.True(Math.Abs(expected.Y - unit.Bounds.Center.Y) <= 1, $"y {unit.Bounds.Center.Y}, expected {expected.Y}");
        Assert.Null(unit.RenderTransform);
        Assert.True(north.IsVisible);

        using (var frame = window.CaptureRenderedFrame()!)
        {
            frame.SavePng(Path.Join(Shots(), "fuehrung-karte-gedreht.png"));
        }

        north.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.Map.Rotation, precision: 9);
        Assert.False(north.IsVisible);
    }

    [AvaloniaFact]
    public void Close_up_the_packs_hydranten_are_drawn_and_the_note_says_they_are_from_OSM()
    {
        var (window, view, vm, _) = Show();
        WaitForWaterPoints(vm);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, view.GetVisualDescendants().OfType<MapCanvasControl>().Single().WaterPoints?.Count);
        Assert.True(view.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "WaterPointsToggle").IsVisible);
        var note = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "WaterPointsNote");
        Assert.True(note.IsVisible);
        Assert.Equal("Hydranten aus OpenStreetMap – nicht amtlich.", note.Text);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-hydranten.png"));
    }

    [AvaloniaFact]
    public void The_night_map_button_draws_the_map_and_its_key_dark()
    {
        var (window, view, vm, _) = Show();
        WaitForWaterPoints(vm);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();
        var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "NightMapToggle");
        var legend = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WaterLegend");
        Assert.True(toggle.IsVisible);
        Assert.Equal(Avalonia.Media.Color.Parse("#E6FFFFFF"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(legend.Background).Color);

        window.MouseDown(MiddleOf(toggle, window), MouseButton.Left);
        window.MouseUp(MiddleOf(toggle, window), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsNightMap);
        Assert.True(view.GetVisualDescendants().OfType<MapCanvasControl>().Single().IsNight);
        Assert.Equal(Avalonia.Media.Color.Parse("#E61E2023"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(legend.Background).Color);

        // The PR screenshot.
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-nacht.png"));
    }

    // Without a Kartenpaket the map still takes marks on its plain background, which has a night colour too.
    [AvaloniaFact]
    public void Without_a_map_pack_the_night_map_button_is_still_offered()
    {
        var vm = new LagekarteViewModel(NewSession(), () => { });
        var view = new LagekarteView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HasNoMap);
        Assert.True(view.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "NightMapToggle").IsVisible);
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
        Assert.Equal(3, view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "WaterPointDetails").ItemCount);
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

        var mark = Pins(view).Children.Single();
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

    // #885: the Einsatzstelle is a pin with "ESt" in its head, the tip on the spot itself.
    [AvaloniaFact]
    public void The_Einsatzort_is_a_pin_with_ESt_in_its_head_and_its_tip_on_the_spot()
    {
        var (window, view, vm, session) = Show();
        session.SetIncidentLocation(Marktplatz);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var at = MapDrawing.ToScreen(Marktplatz, 17, map.Bounds.Size, Marktplatz, 0);
        var pin = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "EinsatzortPin" && p.IsEffectivelyVisible);
        var text = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EinsatzortText" && t.IsEffectivelyVisible);
        var shape = pin.Data!.Bounds;
        var tip = pin.TranslatePoint(new Point(shape.Center.X, shape.Bottom), map)!.Value;
        var head = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), map)!.Value;

        Assert.True(pin.IsEffectivelyVisible);
        Assert.Equal("ESt", text.Text);
        Assert.True(Math.Abs(at.X - tip.X) <= 1 && Math.Abs(at.Y - tip.Y) <= 1, $"tip at {tip}, expected {at}");
        Assert.True(head.Y < at.Y - 20, $"head at {head}, expected well above {at}");
        Assert.Equal("Einsatzort", Avalonia.Automation.AutomationProperties.GetName(Pins(view).Children.Single().GetVisualDescendants().OfType<Panel>().First()));

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-est.png"));
    }

    // #885: units crowding the Einsatzstelle get their signs placed clear of the ESt pin's head.
    [AvaloniaFact]
    public void Signs_crowding_the_Einsatzort_keep_clear_of_the_ESt_pin_s_head()
    {
        var (window, view, vm, session) = Show();
        session.SetIncidentLocation(Marktplatz);
        foreach (var unit in session.Incident.Forces.ToList())
        {
            session.PlaceForceUnit(unit.Id, Marktplatz);
        }

        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var pin = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "EinsatzortPin" && p.IsEffectivelyVisible);
        var pinBox = new Rect(pin.TranslatePoint(default, map)!.Value, pin.Bounds.Size);
        var signs = view.GetVisualDescendants().OfType<TacticalSignControl>().ToList();

        Assert.Equal(3, signs.Count);
        var signsPanel = Assert.IsType<MapMarkerPanel>(view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "Signs").ItemsPanelRoot);
        Assert.Equal(Marktplatz, signsPanel.KeepClearEinsatzort);
        Assert.All(signs, sign =>
        {
            var signBox = new Rect(sign.TranslatePoint(default, map)!.Value, sign.Bounds.Size);
            Assert.False(pinBox.Intersects(signBox), $"{sign.Title} at {signBox} covers the ESt pin {pinBox}");
        });

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-est-zeichen.png"));
    }

    // #885: the pin is grabbed by its head, well above the spot; the tip moves with the hand, not to it.
    [AvaloniaFact]
    public void Dragging_the_ESt_pin_by_its_head_moves_its_tip_as_far_as_the_hand()
    {
        var (window, view, vm, session) = Show();
        session.SetIncidentLocation(Marktplatz);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var text = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EinsatzortText" && t.IsEffectivelyVisible);
        var start = MiddleOf(text, window);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 40, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 80, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X + 80, start.Y), MouseButton.Left);

        var moved = session.Incident.Location;
        Assert.NotNull(moved);
        var before = MapDrawing.ToScreen(Marktplatz, 17, map.Bounds.Size, Marktplatz, 0);
        var after = MapDrawing.ToScreen(Marktplatz, 17, map.Bounds.Size, moved, 0);
        Assert.True(Math.Abs(after.X - before.X - 80) <= 1 && Math.Abs(after.Y - before.Y) <= 1, $"tip moved from {before} to {after}, expected 80 px right");
    }

    [AvaloniaFact]
    public void Fuehrung_has_a_LAGEKARTE_tab_that_shows_the_Lagekarte()
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
        Assert.Equal("LAGEKARTE", tab.Header);
        Assert.Equal("Lagekarte", Avalonia.Automation.AutomationProperties.GetName(tab));
        Assert.Single(view.GetVisualDescendants().OfType<LagekarteView>());
        Assert.Same(workspace.Lagekarte, workspace.LeadershipTabs.Map);
        using (var frame = window.CaptureRenderedFrame()!)
        {
            frame.SavePng(Path.Join(Shots(), "fuehrung-lagekarte-tab.png"));
        }

        workspace.Dispose();
    }

    private (Window Window, LagekarteView View, LagekarteViewModel Vm, LocalIncidentSession Session) Show()
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root);
        var session = NewSession();
        session.AddForceUnit("FF Musterstadt", 9, "Florian Musterstadt 40/1", "Im Einsatz", officerCount: 1, vehicleType: "HLF");
        session.AddForceUnit("FF Musterstadt", 3, "Florian Musterstadt 11/1", "Im Einsatz", officerCount: 1, vehicleType: "ELW 1");
        session.AddForceUnit("FF Musterdorf", 9, "Florian Musterdorf 42/1", "Auf Anfahrt", officerCount: 1);
        var vm = new LagekarteViewModel(session, () => { }, new RegionPackLibrary(_http, root));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 16));
        var view = new LagekarteView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, session);
    }

    [AvaloniaFact]
    public void A_unit_s_pin_sits_on_its_spot_and_its_sign_stands_at_the_offset_tied_back_by_a_strich()
    {
        var (_, view, vm, session) = Show();
        var unitId = session.Incident.Forces[0].Id;
        session.PlaceForceUnit(unitId, Marktplatz);
        session.MoveForceUnitLabel(unitId, new LabelOffset(-120, 40));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var at = MapDrawing.ToScreen(Marktplatz, 17, map.Bounds.Size, Marktplatz, 0);
        var pin = Pins(view).Children.Single();
        Assert.True(Math.Abs(at.X - pin.Bounds.Center.X) <= 1 && Math.Abs(at.Y - pin.Bounds.Center.Y) <= 1, $"pin at {pin.Bounds.Center}, expected {at}");

        var sign = Assert.Single(view.GetVisualDescendants().OfType<TacticalSignControl>());
        var anchor = sign.TranslatePoint(TacticalSignControl.SymbolCenter, map)!.Value;
        Assert.True(Math.Abs(at.X - 120 - anchor.X) <= 1 && Math.Abs(at.Y + 40 - anchor.Y) <= 1, $"sign at {anchor}, expected {at} + (-120, 40)");
        Assert.Equal("HLF", sign.VehicleType);
        Assert.Equal("Florian Musterstadt 40/1", sign.Title);
        Assert.Equal("0/1/8/9", sign.Strength);
        Assert.Equal(new LabelOffset(-120, 40), MapMarkerPanel.GetResolvedOffset(sign));
        Assert.Equal("Florian Musterstadt 40/1: taktisches Zeichen", Avalonia.Automation.AutomationProperties.GetName(sign));
        Assert.Equal("Florian Musterstadt 40/1: Standort", Avalonia.Automation.AutomationProperties.GetName(pin.GetVisualDescendants().OfType<Panel>().First()));
    }

    [AvaloniaFact]
    public void Dragging_a_sign_moves_only_the_sign_and_letting_go_stores_its_offset()
    {
        var (window, view, vm, session) = Show();
        var unitId = session.Incident.Forces[0].Id;
        session.PlaceForceUnit(unitId, Marktplatz);
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 16));
        Dispatcher.UIThread.RunJobs();

        var sign = view.GetVisualDescendants().OfType<TacticalSignControl>().Single();
        var placedAt = MapMarkerPanel.GetResolvedOffset(sign);
        Assert.NotNull(placedAt);
        var start = sign.TranslatePoint(TacticalSignControl.SymbolCenter, window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X - 40, start.Y + 10), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X - 80, start.Y + 20), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X - 80, start.Y + 20), MouseButton.Left);

        var unit = session.Incident.Forces.Single(u => u.Id == unitId);
        Assert.Equal(Marktplatz, unit.Position);
        Assert.NotNull(unit.LabelOffset);
        Assert.Equal(placedAt.Dx - 80, unit.LabelOffset.Dx, precision: 6);
        Assert.Equal(placedAt.Dy + 20, unit.LabelOffset.Dy, precision: 6);
        Assert.Equal(Marktplatz, vm.Map.Center);
    }

    [AvaloniaFact]
    public void Several_units_read_as_taktische_Zeichen_each_tied_to_its_pin()
    {
        var (window, view, vm, session) = Show();
        session.SetIncidentLocation(Marktplatz);
        var forces = session.Incident.Forces;
        session.PlaceForceUnit(forces[0].Id, new GeoPoint(48.17885, 11.25475));
        session.PlaceForceUnit(forces[1].Id, new GeoPoint(48.17905, 11.25600));
        session.MoveForceUnitLabel(forces[1].Id, new LabelOffset(36, -70));
        session.PlaceForceUnit(forces[2].Id, new GeoPoint(48.17790, 11.25640));
        session.MoveForceUnitLabel(forces[2].Id, new LabelOffset(60, 36));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, view.GetVisualDescendants().OfType<TacticalSignControl>().Count());
        Assert.Equal(4, Pins(view).Children.Count);

        // The PR screenshot.
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-taktische-zeichen.png"));
    }

    [AvaloniaFact]
    public void Units_placed_close_together_get_signs_that_cover_neither_each_other_nor_any_pin()
    {
        var (window, view, vm, session) = Show();
        session.AddForceUnit("FF Musterstadt", 6, "Florian Musterstadt 19/1", "Im Einsatz", vehicleType: "MTW");
        session.SetIncidentLocation(Marktplatz);
        var forces = session.Incident.Forces;

        // A few metres apart, as vehicles stand along one street: every sign wants the same spot.
        session.PlaceForceUnit(forces[0].Id, new GeoPoint(48.17865, 11.25530));
        session.PlaceForceUnit(forces[1].Id, new GeoPoint(48.17870, 11.25545));
        session.PlaceForceUnit(forces[2].Id, new GeoPoint(48.17862, 11.25560));
        session.PlaceForceUnit(forces[3].Id, new GeoPoint(48.17856, 11.25540));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Marktplatz, 17));
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var signs = view.GetVisualDescendants().OfType<TacticalSignControl>()
            .Select(s => new Rect(s.TranslatePoint(default, map)!.Value, s.Bounds.Size))
            .ToList();
        var pins = Pins(view).Children
            .Select(c => MapDrawing.ToScreen(Marktplatz, 17, map.Bounds.Size, MapMarkerPanel.GetPosition(c)!, 0))
            .ToList();

        Assert.Equal(4, signs.Count);
        Assert.Equal(5, pins.Count);
        for (var i = 0; i < signs.Count; i++)
        {
            for (var j = i + 1; j < signs.Count; j++)
            {
                Assert.False(signs[i].Intersects(signs[j]), $"signs {i} {signs[i]} and {j} {signs[j]} overlap");
            }

            Assert.All(pins, p => Assert.False(signs[i].Inflate(4).Contains(p), $"sign {i} {signs[i]} covers the pin at {p}"));
        }

        // Each strich runs from its pin to its sign's middle; no two may cross, or which sign
        // belongs to which pin is a guess.
        var striche = view.GetVisualDescendants().OfType<TacticalSignControl>()
            .Select(s => (Pin: Subtract(s.TranslatePoint(TacticalSignControl.SymbolCenter, map)!.Value, MapMarkerPanel.GetResolvedOffset(s)!), Sign: s.TranslatePoint(TacticalSignControl.SymbolCenter, map)!.Value))
            .ToList();
        for (var i = 0; i < striche.Count; i++)
        {
            for (var j = i + 1; j < striche.Count; j++)
            {
                Assert.False(Cross(striche[i].Pin, striche[i].Sign, striche[j].Pin, striche[j].Sign), $"striche {i} and {j} cross");
            }
        }

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "fuehrung-karte-zeichen-dicht.png"));
    }

    private static Point Subtract(Point p, LabelOffset offset) => new(p.X - offset.Dx, p.Y - offset.Dy);

    private static bool Cross(Point a, Point b, Point c, Point d)
    {
        static double Turn(Point p, Point q, Point r) => ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));
        return Turn(c, d, a) * Turn(c, d, b) < 0 && Turn(a, b, c) * Turn(a, b, d) < 0;
    }

    private static MapMarkerPanel Pins(LagekarteView view) =>
        Assert.IsType<MapMarkerPanel>(view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "Marks").ItemsPanelRoot);

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
