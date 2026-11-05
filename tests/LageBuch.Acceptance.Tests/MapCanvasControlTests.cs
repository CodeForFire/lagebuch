using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LageBuch.App.Shared.Controls;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;

namespace LageBuch.Acceptance.Tests;

public class MapCanvasControlTests
{
    private static readonly GeoPoint Fuerstenfeldbruck = new(48.1785, 11.2555);

    // The vector style's land and water, which a tile of one whole polygon shows.
    private static readonly Color Land = Color.FromRgb(0xF2, 0xEF, 0xE9);
    private static readonly Color Water = Color.FromRgb(0xAA, 0xD3, 0xDF);

    // The 1.600–3.199 l/min class.
    private static readonly Color Green = Color.FromRgb(0x2E, 0x7D, 0x46);

    // A pixel inside a flag's coloured field at zoom 16, off its glyph: the body is 20 px high and
    // stands on an 8 px mast above the point at (200, 150).
    private static readonly PixelPoint MediumField = new(203, 125);

    [AvaloniaFact]
    public void The_tiles_of_the_pack_are_drawn_under_the_centre()
    {
        var (window, _, _) = Show(new SolidTiles());

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Land, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Without_a_pack_the_map_is_plain_land_colour()
    {
        var (window, _, _) = Show(null);

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Color.FromRgb(0xE5, 0xE3, 0xDF), PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Switched_to_night_the_tiles_already_drawn_are_drawn_again_dark()
    {
        var (window, control, _) = Show(new SolidTiles());
        using (var day = window.CaptureRenderedFrame()!)
        {
            Assert.Equal(Land, PixelAt(day, 200, 150));
        }

        control.IsNight = true;
        Dispatcher.UIThread.RunJobs();

        using var night = window.CaptureRenderedFrame()!;
        Assert.Equal(Color.FromRgb(0x1C, 0x1E, 0x21), PixelAt(night, 200, 150));
    }

    [AvaloniaFact]
    public void At_night_a_map_without_a_pack_is_dark_too()
    {
        var (window, control, _) = Show(null);

        control.IsNight = true;
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;
        Assert.Equal(Color.FromRgb(0x14, 0x15, 0x18), PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Past_the_pack_s_finest_zoom_its_tiles_are_drawn_rather_than_missing()
    {
        var (window, _, viewport) = Show(new SolidTiles(maxZoom: 15));
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, 18));
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Land, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Dragging_moves_the_map_with_the_hand()
    {
        var (window, _, viewport) = Show(new SolidTiles());
        var before = viewport.Center;

        window.MouseDown(new Point(200, 150), MouseButton.Left);
        window.MouseMove(new Point(300, 200), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(300, 200), MouseButton.Left);

        // Dragged right and down: the map now shows what lay to the west and north.
        Assert.True(viewport.Center.Longitude < before.Longitude);
        Assert.True(viewport.Center.Latitude > before.Latitude);
        var (x0, y0) = WebMercator.ToWorldPixel(before, viewport.Zoom);
        var (x1, y1) = WebMercator.ToWorldPixel(viewport.Center, viewport.Zoom);
        Assert.Equal(-100, x1 - x0, precision: 3);
        Assert.Equal(-50, y1 - y0, precision: 3);
    }

    [AvaloniaFact]
    public void Moving_without_a_button_held_does_not_pan()
    {
        var (window, _, viewport) = Show(new SolidTiles());
        var before = viewport.Center;

        window.MouseMove(new Point(300, 200));

        Assert.Equal(before, viewport.Center);
    }

    [AvaloniaFact]
    public void The_wheel_zooms_and_keeps_the_spot_under_the_pointer_in_place()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        var pointer = new Point(300, 100);
        var before = GeoUnder(control, viewport, pointer);

        window.MouseWheel(pointer, new Vector(0, 1));

        Assert.Equal(14, viewport.Zoom);
        var after = GeoUnder(control, viewport, pointer);
        Assert.Equal(before.Latitude, after.Latitude, precision: 5);
        Assert.Equal(before.Longitude, after.Longitude, precision: 5);
    }

    [AvaloniaFact]
    public void A_map_inside_a_scrolling_page_zooms_on_the_wheel_only_with_ctrl()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        control.WheelZoomNeedsControl = true;

        window.MouseWheel(new Point(200, 150), new Vector(0, 1));
        Assert.Equal(13, viewport.Zoom);

        window.MouseWheel(new Point(200, 150), new Vector(0, 1), RawInputModifiers.Control);
        Assert.Equal(14, viewport.Zoom);
    }

    [AvaloniaFact]
    public void The_keyboard_zooms_and_pans()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        control.Focus();
        var before = viewport.Center;

        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.None);
        Assert.Equal(14, viewport.Zoom);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.True(viewport.Center.Longitude > before.Longitude);
    }

    [AvaloniaTheory]
    [InlineData(1.0, 0)]
    [InlineData(2.0, 1)]
    [InlineData(4.0, 2)]
    [InlineData(0.5, -1)]
    [InlineData(1.3, 0)]
    [InlineData(0.0, -7)]
    public void A_pinch_to_twice_the_size_is_one_zoom_level(double scale, int delta) =>
        Assert.Equal(delta, MapCanvasControl.PinchScaleToZoomDelta(scale));

    // Each pack's tiles are cut to its area, so the packs are simply drawn in order, the later on top.
    [AvaloniaFact]
    public void Several_packs_are_drawn_one_over_the_other_in_their_order()
    {
        var (waterOnTop, _, _) = Show(new CombinedMapTileSource([new SolidTiles("earth"), new SolidTiles("water")]));
        var (landOnTop, _, _) = Show(new CombinedMapTileSource([new SolidTiles("water"), new SolidTiles("earth")]));

        using var water = waterOnTop.CaptureRenderedFrame()!;
        using var land = landOnTop.CaptureRenderedFrame()!;

        Assert.Equal(Water, PixelAt(water, 200, 150));
        Assert.Equal(Land, PixelAt(land, 200, 150));
    }

    [AvaloniaFact]
    public void From_zoom_16_a_water_point_is_a_flag_whose_mast_foot_stands_on_it()
    {
        var (window, _, _) = ShowWaterPoints(WaterPointLayer.MediumMinZoom, Hydrant(1600));

        using var frame = window.CaptureRenderedFrame()!;

        // The mast narrows to the point; left of it and below it is the map.
        AssertNear(Green, PixelAt(frame, 200, 147));
        Assert.Equal(Land, PixelAt(frame, 194, 147));
        Assert.Equal(Land, PixelAt(frame, 200, 158));

        // The body is above and right of the point: the coloured field, then white.
        AssertNear(Green, PixelAt(frame, MediumField.X, MediumField.Y));
        Assert.True(Has(frame, new PixelRect(220, 124, 30, 16), Colors.White));
    }

    [AvaloniaTheory]
    [InlineData(null, 0x5F6B78)]
    [InlineData(300, 0xB3261E)]
    [InlineData(600, 0xD9731A)]
    [InlineData(1200, 0xB8960F)]
    [InlineData(1600, 0x2E7D46)]
    [InlineData(3200, 0x0F5C6E)]
    public void The_flow_class_colours_the_flag_s_field(int? litresPerMinute, uint rgb)
    {
        var (window, _, _) = ShowWaterPoints(WaterPointLayer.MediumMinZoom, Hydrant(litresPerMinute));

        using var frame = window.CaptureRenderedFrame()!;

        AssertNear(Color.FromUInt32(0xFF000000 | rgb), PixelAt(frame, MediumField.X, MediumField.Y));
    }

    [AvaloniaFact]
    public void A_stronger_source_lies_over_a_weaker_one_on_the_same_spot()
    {
        var (window, _, _) = ShowWaterPoints(WaterPointLayer.MediumMinZoom, Hydrant(3200), Hydrant(300));

        using var frame = window.CaptureRenderedFrame()!;

        AssertNear(Color.FromRgb(0x0F, 0x5C, 0x6E), PixelAt(frame, MediumField.X, MediumField.Y));
    }

    [AvaloniaFact]
    public void At_zoom_15_a_water_point_is_a_disc_on_it_whose_ring_fills_with_its_class()
    {
        var (window, _, _) = ShowWaterPoints(WaterPointLayer.MinZoom, Hydrant(1600));

        using var frame = window.CaptureRenderedFrame()!;

        AssertNear(Green, PixelAt(frame, 200, 148));

        // Four classes of five: from twelve o'clock clockwise past nine, the last fifth left grey.
        AssertNear(Green, PixelAt(frame, 205, 150));
        AssertNear(Green, PixelAt(frame, 194, 150));
        AssertNear(Color.FromRgb(0xD5, 0xD9, 0xDD), PixelAt(frame, 197, 145));

        // No flag.
        Assert.Equal(Land, PixelAt(frame, 215, 125));
    }

    [AvaloniaFact]
    public void A_four_digit_estimate_stays_inside_the_flag_s_frame()
    {
        var (window, _, _) = ShowWaterPoints(WaterPointLayer.FullMinZoom, Hydrant(1600) with { IsEstimated = true, NominalDiameter = 100 });

        using var frame = window.CaptureRenderedFrame()!;

        // Body rows: the mast's 10 px above the point, then 32 px of body.
        var (top, bottom) = (150 - 10 - 32, 150 - 10);
        var frameRight = Enumerable.Range(200, 200).Last(x => IsNear(Green, PixelAt(frame, x, (top + bottom) / 2)));
        Assert.True(HasText(frame, new PixelRect(230, top, frameRight - 230, bottom - top)));
        Assert.False(HasText(frame, new PixelRect(frameRight + 1, top - 4, 399 - frameRight, bottom - top + 8)));
    }

    [AvaloniaFact]
    public void Out_of_service_the_flag_loses_its_colour_and_is_struck_through_red()
    {
        var (window, _, _) = ShowWaterPoints(WaterPointLayer.MediumMinZoom, Hydrant(1600) with { IsOutOfService = true });

        using var frame = window.CaptureRenderedFrame()!;

        AssertNear(Color.FromRgb(0x8A, 0x91, 0x99), PixelAt(frame, MediumField.X, MediumField.Y));
        Assert.True(Has(frame, new PixelRect(220, 122, 40, 20), Color.FromRgb(0xD3, 0x2F, 0x2F)));
    }

    [AvaloniaFact]
    public void The_water_point_whose_card_is_open_is_framed_dark()
    {
        var hydrant = Hydrant(1600);
        var (window, control, _) = ShowWaterPoints(WaterPointLayer.MediumMinZoom, hydrant);
        var frameTop = new PixelRect(225, 119, 20, 6);
        using (var before = window.CaptureRenderedFrame()!)
        {
            Assert.False(Has(before, frameTop, Color.FromRgb(0x21, 0x21, 0x21)));
        }

        control.SelectedWaterPoint = hydrant;
        Dispatcher.UIThread.RunJobs();

        using var after = window.CaptureRenderedFrame()!;
        Assert.True(Has(after, frameTop, Color.FromRgb(0x21, 0x21, 0x21)));
    }

    [AvaloniaFact]
    public void On_the_night_map_the_open_card_s_flag_keeps_its_white_body_and_is_framed_light()
    {
        var hydrant = Hydrant(1600);
        var (window, control, _) = ShowWaterPoints(WaterPointLayer.MediumMinZoom, hydrant);
        control.IsNight = true;
        control.SelectedWaterPoint = hydrant;
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.True(Has(frame, new PixelRect(225, 119, 20, 6), Color.FromRgb(0xE4, 0xE4, 0xE4)));
        Assert.True(Has(frame, new PixelRect(225, 126, 20, 12), Colors.White));
    }

    [AvaloniaFact]
    public void A_place_name_gives_way_to_a_hydrant_s_label()
    {
        // The name is centred on the screen; left of the middle is its first half, clear of the hydrant's dot and label.
        var (window, control, viewport) = Show(new PlaceNameTiles(Fuerstenfeldbruck, "Musterstadt", zoom: WaterPointLayer.MediumMinZoom));
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, WaterPointLayer.MediumMinZoom));
        var farAway = WebMercator.ToGeo(WebMercator.ToWorldPixel(Fuerstenfeldbruck, WaterPointLayer.MediumMinZoom).X, WebMercator.ToWorldPixel(Fuerstenfeldbruck, WaterPointLayer.MediumMinZoom).Y + 100, WaterPointLayer.MediumMinZoom);
        control.WaterPoints = [new WaterPoint(farAway, WaterPointKind.Hydrant, "UH 100")];
        Dispatcher.UIThread.RunJobs();
        using (var named = window.CaptureRenderedFrame()!)
        {
            Assert.True(HasInk(named, 160, 140, 185, 160));
        }

        control.WaterPoints = [new WaterPoint(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100")];
        Dispatcher.UIThread.RunJobs();

        using var covered = window.CaptureRenderedFrame()!;
        Assert.False(HasInk(covered, 160, 140, 185, 160));
    }

    [AvaloniaFact]
    public void Further_out_than_zoom_15_no_water_point_is_drawn()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        control.WaterPoints = [new WaterPoint(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100")];
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, WaterPointLayer.MinZoom - 1));
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Land, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Shift_and_the_wheel_turn_the_map_a_step_and_leave_the_zoom_alone()
    {
        var (window, _, viewport) = Show(new SolidTiles());

        window.MouseWheel(new Point(200, 150), new Vector(0, 1), RawInputModifiers.Shift);
        Assert.Equal(15, viewport.Rotation, precision: 9);

        window.MouseWheel(new Point(200, 150), new Vector(0, -1), RawInputModifiers.Shift);
        window.MouseWheel(new Point(200, 150), new Vector(0, -1), RawInputModifiers.Shift);
        Assert.Equal(345, viewport.Rotation, precision: 9);
        Assert.Equal(13, viewport.Zoom);
    }

    [AvaloniaFact]
    public void A_touchpad_s_small_shift_scrolls_turn_the_map_by_as_much_as_was_scrolled()
    {
        var (window, _, viewport) = Show(new SolidTiles());

        // A two-finger swipe arrives as many fractions of a wheel notch, not as notches.
        for (var i = 0; i < 5; i++)
        {
            window.MouseWheel(new Point(200, 150), new Vector(0, 0.2), RawInputModifiers.Shift);
        }

        Assert.Equal(15, viewport.Rotation, precision: 6);
        Assert.Equal(13, viewport.Zoom);
    }

    [AvaloniaFact]
    public void A_shift_scroll_the_system_sends_sideways_turns_the_map_too()
    {
        var (window, _, viewport) = Show(new SolidTiles());

        window.MouseWheel(new Point(200, 150), new Vector(1, 0), RawInputModifiers.Shift);

        Assert.Equal(15, viewport.Rotation, precision: 6);
    }

    [AvaloniaFact]
    public void Shift_and_the_arrows_turn_the_map_and_plain_arrows_still_pan()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        control.Focus();
        var before = viewport.Center;

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.Equal(15, viewport.Rotation, precision: 9);
        Assert.Equal(before, viewport.Center);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.Equal(345, viewport.Rotation, precision: 9);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.NotEqual(before, viewport.Center);
        Assert.Equal(345, viewport.Rotation, precision: 9);
    }

    [AvaloniaFact]
    public void On_a_turned_map_a_point_east_of_the_centre_is_drawn_below_it_at_a_quarter_turn()
    {
        var east = WebMercator.ToGeo(WebMercator.ToWorldPixel(Fuerstenfeldbruck, 13).X + 100, WebMercator.ToWorldPixel(Fuerstenfeldbruck, 13).Y, 13);
        var size = new Size(400, 300);

        var at = MapDrawing.ToScreen(Fuerstenfeldbruck, 13, size, east, rotation: 90);

        Assert.Equal(200, at.X, precision: 6);
        Assert.Equal(250, at.Y, precision: 6);
        var back = MapDrawing.ToGeo(Fuerstenfeldbruck, 13, size, at, rotation: 90);
        Assert.Equal(east.Latitude, back.Latitude, precision: 9);
        Assert.Equal(east.Longitude, back.Longitude, precision: 9);
    }

    [AvaloniaFact]
    public void On_a_turned_map_dragging_keeps_the_grabbed_spot_under_the_hand()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, 60));
        Dispatcher.UIThread.RunJobs();
        var grabbed = control.ScreenToGeo(new Point(200, 150))!;

        window.MouseDown(new Point(200, 150), MouseButton.Left);
        window.MouseMove(new Point(300, 200), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(300, 200), MouseButton.Left);

        var under = control.ScreenToGeo(new Point(300, 200))!;
        Assert.Equal(grabbed.Latitude, under.Latitude, precision: 6);
        Assert.Equal(grabbed.Longitude, under.Longitude, precision: 6);
        Assert.Equal(60, viewport.Rotation, precision: 9);
    }

    [AvaloniaFact]
    public void On_a_turned_map_the_wheel_zooms_towards_the_spot_under_the_pointer()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, 200));
        Dispatcher.UIThread.RunJobs();
        var pointer = new Point(300, 100);
        var before = control.ScreenToGeo(pointer)!;

        window.MouseWheel(pointer, new Vector(0, 1));

        var after = control.ScreenToGeo(pointer)!;
        Assert.Equal(14, viewport.Zoom);
        Assert.Equal(before.Latitude, after.Latitude, precision: 5);
        Assert.Equal(before.Longitude, after.Longitude, precision: 5);
    }

    [AvaloniaFact]
    public void A_turned_map_still_fills_every_corner_of_the_screen()
    {
        var (window, _, viewport) = Show(new SolidTiles());
        viewport.ChangeViewCommand.Execute(new MapViewChange(viewport.Center, 13, 45));
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.All([(1, 1), (398, 1), (1, 298), (398, 298)], c => Assert.Equal(Land, PixelAt(frame, c.Item1, c.Item2)));
    }

    [AvaloniaTheory]
    [InlineData(10, 40, 30)]
    [InlineData(40, 10, -30)]
    [InlineData(350, 10, 20)] // across north: a small turn, not nearly a whole one back
    [InlineData(10, 350, -20)]
    public void A_twist_is_measured_the_short_way_round(double start, double angle, double twist) =>
        Assert.Equal(twist, MapCanvasControl.TwistBetween(start, angle), precision: 9);

    [AvaloniaFact]
    public void A_Kartenebene_s_area_is_filled_around_its_hole_and_the_hole_stays_open()
    {
        var (window, control, _) = Show(null);
        static GeoPoint At(double dLat, double dLon) => new(Fuerstenfeldbruck.Latitude + dLat, Fuerstenfeldbruck.Longitude + dLon);
        IReadOnlyList<GeoPoint> outer = [At(-0.02, -0.02), At(-0.02, 0.02), At(0.02, 0.02), At(0.02, -0.02), At(-0.02, -0.02)];
        IReadOnlyList<GeoPoint> hole = [At(-0.005, -0.005), At(-0.005, 0.005), At(0.005, 0.005), At(0.005, -0.005), At(-0.005, -0.005)];
        control.Overlays =
        [
            new OverlayShape(OverlayShapeKind.Area, [outer, hole], new OverlayStyle(0xCCFF0000, 0xFFFF0000, 2), null, GeoBounds.Of(outer)),
        ];
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        // About 60 px east of the centre lies inside the area, the centre inside its hole.
        var filled = PixelAt(frame, 260, 150);
        Assert.True(filled.R > 0xE0 && filled.G < 0x60, $"area pixel {filled}");
        Assert.Equal(Color.FromRgb(0xE5, 0xE3, 0xDF), PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void A_Kartenebene_off_screen_draws_nothing()
    {
        var (window, control, _) = Show(null);
        IReadOnlyList<GeoPoint> far = [new(52.0, 13.0), new(52.0, 13.1), new(52.1, 13.1), new(52.0, 13.0)];
        control.Overlays = [new OverlayShape(OverlayShapeKind.Area, [far], new OverlayStyle(0xCCFF0000, 0xFFFF0000, 2), null, GeoBounds.Of(far))];
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Color.FromRgb(0xE5, 0xE3, 0xDF), PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void A_Fliessweg_of_one_point_on_flat_ground_still_marks_where_it_ends()
    {
        var (window, control, _) = Show(null);
        control.FlowPath = [Fuerstenfeldbruck];
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(FlowBlue, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void The_Fliessweg_is_drawn_over_a_Kartenebene()
    {
        var (window, control, _) = Show(null);
        static GeoPoint At(double dLat, double dLon) => new(Fuerstenfeldbruck.Latitude + dLat, Fuerstenfeldbruck.Longitude + dLon);
        IReadOnlyList<GeoPoint> plume = [At(-0.02, -0.02), At(-0.02, 0.02), At(0.02, 0.02), At(0.02, -0.02), At(-0.02, -0.02)];
        control.Overlays = [new OverlayShape(OverlayShapeKind.Area, [plume], new OverlayStyle(0xCCFF0000, 0xFFFF0000, 2), null, GeoBounds.Of(plume))];
        control.FlowPath = [At(0, -0.01), Fuerstenfeldbruck];
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(FlowBlue, PixelAt(frame, 200, 150));
    }

    private static readonly Color FlowBlue = Color.FromRgb(0x0D, 0x47, 0xA1);

    private static (Window Window, MapCanvasControl Control, MapViewport Viewport) Show(IMapTileSource? tiles)
    {
        var viewport = new MapViewport { TileSource = tiles, Center = Fuerstenfeldbruck, Zoom = 13 };
        var control = new MapCanvasControl
        {
            [!MapCanvasControl.TileSourceProperty] = new Avalonia.Data.Binding(nameof(MapViewport.TileSource)),
            [!MapCanvasControl.CenterProperty] = new Avalonia.Data.Binding(nameof(MapViewport.Center)),
            [!MapCanvasControl.ZoomProperty] = new Avalonia.Data.Binding(nameof(MapViewport.Zoom)),
            [!MapCanvasControl.RotationProperty] = new Avalonia.Data.Binding(nameof(MapViewport.Rotation)),
            ViewChangedCommand = viewport.ChangeViewCommand,
            DataContext = viewport,
        };
        var window = new Window { Content = control, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, control, viewport);
    }

    private static WaterPoint Hydrant(int? litresPerMinute) =>
        new(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100") { FlowLitresPerMinute = litresPerMinute };

    private static (Window Window, MapCanvasControl Control, MapViewport Viewport) ShowWaterPoints(int zoom, params WaterPoint[] points)
    {
        var (window, control, viewport) = Show(new SolidTiles());
        control.WaterPoints = points;
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, zoom));
        Dispatcher.UIThread.RunJobs();
        return (window, control, viewport);
    }

    private static bool IsNear(Color expected, Color actual) =>
        Math.Abs(expected.R - actual.R) <= 12 && Math.Abs(expected.G - actual.G) <= 12 && Math.Abs(expected.B - actual.B) <= 12;

    private static void AssertNear(Color expected, Color actual) =>
        Assert.True(IsNear(expected, actual), $"Expected about {expected}, was {actual}.");

    // Whether any pixel in the box is about `colour`.
    private static bool Has(WriteableBitmap frame, PixelRect box, Color colour)
    {
        for (var x = box.X; x < box.Right; x++)
        {
            for (var y = box.Y; y < box.Bottom; y++)
            {
                if (IsNear(colour, PixelAt(frame, x, y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether any pixel in the box is as dark as the marker's numbers, and grey rather than a class colour.
    private static bool HasText(WriteableBitmap frame, PixelRect box)
    {
        for (var x = box.X; x < box.Right; x++)
        {
            for (var y = box.Y; y < box.Bottom; y++)
            {
                var c = PixelAt(frame, x, y);
                if (c.R < 0x70 && c.G < 0x70 && c.B < 0x70 && Math.Abs(c.R - c.G) < 0x18)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static GeoPoint GeoUnder(MapCanvasControl control, MapViewport viewport, Point screen)
    {
        var (x, y) = WebMercator.ToWorldPixel(viewport.Center, viewport.Zoom);
        return WebMercator.ToGeo(x + screen.X - (control.Bounds.Width / 2), y + screen.Y - (control.Bounds.Height / 2), viewport.Zoom);
    }

    private static Color PixelAt(WriteableBitmap frame, int x, int y)
    {
        using var buffer = frame.Lock();
        Assert.Equal(PixelFormat.Rgba8888, buffer.Format);
        var offset = (y * buffer.RowBytes) + (x * 4);
        return Color.FromArgb(
            Marshal.ReadByte(buffer.Address, offset + 3),
            Marshal.ReadByte(buffer.Address, offset),
            Marshal.ReadByte(buffer.Address, offset + 1),
            Marshal.ReadByte(buffer.Address, offset + 2));
    }

    // Whether any pixel in the box is as dark as a name's glyphs.
    private static bool HasInk(WriteableBitmap frame, int left, int top, int right, int bottom)
    {
        for (var x = left; x < right; x++)
        {
            for (var y = top; y < bottom; y++)
            {
                if (PixelAt(frame, x, y).R < 0x80)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>One town named <paramref name="name"/> at <paramref name="at"/>, on its tile at <paramref name="zoom"/> only.</summary>
    private sealed class PlaceNameTiles(GeoPoint at, string name, int zoom) : IMapTileSource
    {
        private readonly int _zoom = zoom;
        private readonly (int X, int Y) _tile = WebMercator.ToTileIndex(WebMercator.ToWorldPixel(at, zoom).X, WebMercator.ToWorldPixel(at, zoom).Y);
        private readonly byte[] _bytes = Persistence.Tests.VectorTileFixtures.Tile(
            "places",
            (Persistence.Map.Vector.VectorGeometryType.Point, [("kind", "locality"), ("name", name)], [[InTile(at, zoom)]]));

        public byte[]? GetTile(int zoom, int x, int y) => zoom == _zoom && (x, y) == _tile ? _bytes : null;

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => _zoom;

        // Where the point lies on its tile, in the tile's 4096 units.
        private static (int X, int Y) InTile(GeoPoint point, int level)
        {
            var (x, y) = WebMercator.ToWorldPixel(point, level);
            const double unitsPerPixel = 4096.0 / WebMercator.TileSizePixels;
            return ((int)Math.Round((x % WebMercator.TileSizePixels) * unitsPerPixel), (int)Math.Round((y % WebMercator.TileSizePixels) * unitsPerPixel));
        }
    }

    /// <summary>
    /// Every tile one polygon of <paramref name="layer"/> covering it whole, down to
    /// <paramref name="maxZoom"/> and none finer, so the map shows that layer's colour.
    /// </summary>
    private sealed class SolidTiles(string layer = "earth", int maxZoom = 19) : IMapTileSource
    {
        private readonly byte[] _tile = Persistence.Tests.VectorTileFixtures.Tile(
            layer,
            (Persistence.Map.Vector.VectorGeometryType.Polygon, [("kind", layer)], [[(0, 0), (4096, 0), (4096, 4096), (0, 4096)]]));

        public byte[]? GetTile(int zoom, int x, int y) => zoom <= maxZoom ? _tile : null;

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => maxZoom;
    }
}
