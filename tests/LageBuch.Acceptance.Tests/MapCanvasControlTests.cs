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

    [AvaloniaFact]
    public void The_tiles_of_the_pack_are_drawn_under_the_centre()
    {
        var (window, _, _) = Show(new SolidTiles(Colors.SteelBlue));

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Colors.SteelBlue, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Without_a_pack_the_map_is_plain_land_colour()
    {
        var (window, _, _) = Show(null);

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Color.FromRgb(0xE5, 0xE3, 0xDF), PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Past_the_pack_s_finest_zoom_its_tiles_are_enlarged_rather_than_missing()
    {
        var (window, _, viewport) = Show(new SolidTiles(Colors.SeaGreen, maxZoom: 15));
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, 18));
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Colors.SeaGreen, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void Dragging_moves_the_map_with_the_hand()
    {
        var (window, _, viewport) = Show(new SolidTiles(Colors.SteelBlue));
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
        var (window, _, viewport) = Show(new SolidTiles(Colors.SteelBlue));
        var before = viewport.Center;

        window.MouseMove(new Point(300, 200));

        Assert.Equal(before, viewport.Center);
    }

    [AvaloniaFact]
    public void The_wheel_zooms_and_keeps_the_spot_under_the_pointer_in_place()
    {
        var (window, control, viewport) = Show(new SolidTiles(Colors.SteelBlue));
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
        var (window, control, viewport) = Show(new SolidTiles(Colors.SteelBlue));
        control.WheelZoomNeedsControl = true;

        window.MouseWheel(new Point(200, 150), new Vector(0, 1));
        Assert.Equal(13, viewport.Zoom);

        window.MouseWheel(new Point(200, 150), new Vector(0, 1), RawInputModifiers.Control);
        Assert.Equal(14, viewport.Zoom);
    }

    [AvaloniaFact]
    public void The_keyboard_zooms_and_pans()
    {
        var (window, control, viewport) = Show(new SolidTiles(Colors.SteelBlue));
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

    // Two neighbouring packs whose tiles are opaque everywhere, as real edge tiles are: empty land
    // outside the pack's area. Each must show only within its own area, or the first (or last)
    // pack's empty margin covers the other's streets.
    [AvaloniaFact]
    public void Each_pack_is_drawn_only_within_its_own_area()
    {
        GeoPoint[] Box(double west, double east) => [new(47, west), new(47, east), new(49, east), new(49, west)];
        var west = new MapTileLayer(new SolidTiles(Colors.IndianRed), [Box(10, Fuerstenfeldbruck.Longitude)]);
        var east = new MapTileLayer(new SolidTiles(Colors.SteelBlue), [Box(Fuerstenfeldbruck.Longitude, 12)]);
        var (window, _, _) = Show(new CombinedMapTileSource([west, east]));

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Colors.IndianRed, PixelAt(frame, 100, 150));
        Assert.Equal(Colors.SteelBlue, PixelAt(frame, 300, 150));
    }

    [AvaloniaFact]
    public void Outside_every_area_the_map_is_plain_land_colour()
    {
        GeoPoint[] box = [new(48.17, 11.25), new(48.17, 11.26), new(48.18, 11.26), new(48.18, 11.25)];
        var (window, _, _) = Show(new CombinedMapTileSource([new MapTileLayer(new SolidTiles(Colors.SteelBlue), [box])]));

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Color.FromRgb(0xE5, 0xE3, 0xDF), PixelAt(frame, 5, 5));
    }

    [AvaloniaFact]
    public void Each_kind_of_water_point_has_its_own_symbol_from_zoom_15()
    {
        var (window, control, viewport) = Show(new SolidTiles(Colors.SteelBlue));
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
        Assert.Equal(Colors.SteelBlue, PixelAt(frame, 325, 145));
    }

    [AvaloniaFact]
    public void The_water_point_whose_card_is_open_is_ringed()
    {
        var (window, control, viewport) = Show(new SolidTiles(Colors.SteelBlue));
        var hydrant = new WaterPoint(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100");
        control.WaterPoints = [hydrant];
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, WaterPointLayer.MinZoom));
        Dispatcher.UIThread.RunJobs();
        using (var before = window.CaptureRenderedFrame()!)
        {
            Assert.Equal(Colors.SteelBlue, PixelAt(before, 210, 150));
        }

        control.SelectedWaterPoint = hydrant;
        Dispatcher.UIThread.RunJobs();

        using var after = window.CaptureRenderedFrame()!;
        Assert.Equal(Color.FromRgb(0x21, 0x21, 0x21), PixelAt(after, 210, 150));
    }

    [AvaloniaFact]
    public void Further_out_than_zoom_15_no_water_point_is_drawn()
    {
        var (window, control, viewport) = Show(new SolidTiles(Colors.SteelBlue));
        control.WaterPoints = [new WaterPoint(Fuerstenfeldbruck, WaterPointKind.Hydrant, "UH 100")];
        viewport.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, WaterPointLayer.MinZoom - 1));
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Colors.SteelBlue, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void An_overzoomed_tile_reads_its_quarter_of_the_parent()
    {
        // Zoom 16, tile (3, 1) lies in the right half, top half of zoom-15 tile (1, 0).
        var (zoom, x, y, source) = Assert.NotNull(MapDrawing.Overzoom(16, 3, 1, 15));

        Assert.Equal((15, 1, 0), (zoom, x, y));
        Assert.Equal(new Rect(128, 128, 128, 128), source);
    }

    [AvaloniaFact]
    public void Within_the_pack_s_zoom_nothing_is_overzoomed() => Assert.Null(MapDrawing.Overzoom(15, 3, 1, 15));

    private static (Window Window, MapCanvasControl Control, MapViewport Viewport) Show(IMapTileSource? tiles)
    {
        var viewport = new MapViewport { TileSource = tiles, Center = Fuerstenfeldbruck, Zoom = 13 };
        var control = new MapCanvasControl
        {
            [!MapCanvasControl.TileSourceProperty] = new Avalonia.Data.Binding(nameof(MapViewport.TileSource)),
            [!MapCanvasControl.CenterProperty] = new Avalonia.Data.Binding(nameof(MapViewport.Center)),
            [!MapCanvasControl.ZoomProperty] = new Avalonia.Data.Binding(nameof(MapViewport.Zoom)),
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

    /// <summary>Every tile one colour, down to <paramref name="maxZoom"/> and none finer.</summary>
    private sealed class SolidTiles(Color color, int maxZoom = 19) : IMapTileSource
    {
        private readonly byte[] _png = Png(color);

        public byte[]? GetTile(int zoom, int x, int y) => zoom <= maxZoom ? _png : null;

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => maxZoom;

        private static byte[] Png(Color color)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
            using (var context = bitmap.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(color), new Rect(0, 0, 256, 256));
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
    }
}
