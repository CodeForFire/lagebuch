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
    public void Each_kind_of_water_point_has_its_own_symbol_from_zoom_15()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        GeoPoint East(double pixels) => WebMercator.ToGeo(WebMercator.ToWorldPixel(Fuerstenfeldbruck, 15).X + pixels, WebMercator.ToWorldPixel(Fuerstenfeldbruck, 15).Y, 15);
        control.WaterPoints =
        [
            new WaterPoint(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100"),
            new WaterPoint(East(60), WaterPointKind.Loeschwasserbehaelter, string.Empty),
            new WaterPoint(East(-60), WaterPointKind.Saugstelle, string.Empty),
            new WaterPoint(East(120), WaterPointKind.Loeschteich, string.Empty),
        ];
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, WaterPointLayer.MinZoom));
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Color.FromRgb(0xD3, 0x2F, 0x2F), PixelAt(frame, 200, 150));
        Assert.Equal(Color.FromRgb(0x15, 0x65, 0xC0), PixelAt(frame, 260, 150));

        // A Saugstelle is a ring: white in the middle, blue on its rim.
        Assert.Equal(Colors.White, PixelAt(frame, 140, 150));
        Assert.Equal(Color.FromRgb(0x15, 0x65, 0xC0), PixelAt(frame, 144, 150));

        // A Löschteich is a diamond: blue in the middle, but not in the corners a square would fill.
        Assert.Equal(Color.FromRgb(0x15, 0x65, 0xC0), PixelAt(frame, 320, 150));
        Assert.Equal(Land, PixelAt(frame, 325, 145));
    }

    [AvaloniaFact]
    public void The_water_point_whose_card_is_open_is_ringed()
    {
        var (window, control, viewport) = Show(new SolidTiles());
        var hydrant = new WaterPoint(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100");
        control.WaterPoints = [hydrant];
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, WaterPointLayer.MinZoom));
        Dispatcher.UIThread.RunJobs();
        using (var before = window.CaptureRenderedFrame()!)
        {
            Assert.Equal(Land, PixelAt(before, 210, 150));
        }

        control.SelectedWaterPoint = hydrant;
        Dispatcher.UIThread.RunJobs();

        using var after = window.CaptureRenderedFrame()!;
        Assert.Equal(Color.FromRgb(0x21, 0x21, 0x21), PixelAt(after, 210, 150));
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
