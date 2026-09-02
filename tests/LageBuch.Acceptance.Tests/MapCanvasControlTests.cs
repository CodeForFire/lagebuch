using System.Collections.ObjectModel;
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
using CommunityToolkit.Mvvm.Input;
using LageBuch.App.Shared.Controls;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.Acceptance.Tests;

// Issue #150 (Plan B): the map canvas the operator draws a Wasserförderung route on.
public class MapCanvasControlTests
{
    private sealed class FakeTileSource : IMapTileSource
    {
        public byte[]? GetTile(int zoom, int x, int y) => SolidTilePng.Bytes;

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => null;
    }

    // A real, decodable 4x4 solid-color PNG (built once via Avalonia's own encoder) so
    // MapCanvasControl's Bitmap(stream) decode path is exercised with genuine image bytes.
    private static class SolidTilePng
    {
        public static readonly byte[] Bytes = Build();

        private static byte[] Build()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(4, 4));
            using (var ctx = bitmap.CreateDrawingContext())
            {
                ctx.FillRectangle(Avalonia.Media.Brushes.SteelBlue, new Rect(0, 0, 4, 4));
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, PngBitmapEncoderOptions.Default);
            return ms.ToArray();
        }
    }

    private static (Window Window, MapCanvasControl Control) ShowControl(
        IReadOnlyList<GeoPoint>? routePoints = null,
        RelayCommand<GeoPoint>? onPointClicked = null,
        RelayCommand? onUndo = null,
        RelayCommand<MapViewChange>? onViewChanged = null)
    {
        var control = new MapCanvasControl
        {
            Width = 400,
            Height = 300,
            TileSource = new FakeTileSource(),
            CenterLatitude = 48.0,
            CenterLongitude = 11.0,
            Zoom = 15,
            RoutePoints = routePoints,
            PointClickedCommand = onPointClicked,
            UndoRequestedCommand = onUndo,
            ViewChangedCommand = onViewChanged,
        };
        var window = new Window { Content = control, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, control);
    }

    [AvaloniaFact]
    public void Renders_tiles_and_a_route_without_throwing()
    {
        var (window, _) = ShowControl(routePoints: new[] { new GeoPoint(48.0, 11.0), new GeoPoint(48.002, 11.0) });

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    // #150 follow-up: selecting an existing Leitung in the grid must show its saved route on the
    // map as a reference overlay, distinct from any in-progress RoutePoints sketch.
    [AvaloniaFact]
    public void SelectedRoutePoints_render_in_a_distinct_color_from_RoutePoints()
    {
        var control = new MapCanvasControl
        {
            Width = 200,
            Height = 200,
            CenterLatitude = 48.0,
            CenterLongitude = 11.0,
            Zoom = 15,
            RoutePoints = new[] { new GeoPoint(48.0, 11.002) }, // offset from center
            SelectedRoutePoints = new[] { new GeoPoint(48.0, 11.0) }, // == control's center
        };
        var window = new Window { Content = control, Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()!;

        Assert.Equal(Colors.DodgerBlue, SamplePixel(frame, 100, 100));
    }

    // Regression: RoutePoints is bound to the VM's live ObservableCollection<GeoPoint>
    // (WasserfoerderungView.axaml), which is mutated in place -- Add/RemoveAt/Clear -- never
    // reassigned. AffectsRender only reacts to the property's own value (the collection reference)
    // changing, so a newly drawn point did not repaint until something unrelated invalidated the
    // control. This asserts the actually-composited pixel, not just VM state, since a forced
    // re-render (e.g. CaptureRenderedFrame after a resize) would mask the bug.
    [AvaloniaFact]
    public void Mutating_the_bound_RoutePoints_collection_in_place_repaints_without_reassigning_the_property()
    {
        var points = new ObservableCollection<GeoPoint>();
        var control = new MapCanvasControl
        {
            Width = 200,
            Height = 200,
            CenterLatitude = 48.0,
            CenterLongitude = 11.0,
            Zoom = 15,
            RoutePoints = points,
        };
        var window = new Window { Content = control, Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using (var before = window.CaptureRenderedFrame()!)
        {
            Assert.NotEqual(Colors.OrangeRed, SamplePixel(before, 100, 100));
        }

        // Same collection instance, mutated in place -- exactly how the real binding works.
        points.Add(new GeoPoint(48.0, 11.0)); // == control's center -> draws exactly at (100,100)
        Dispatcher.UIThread.RunJobs();

        using var after = window.CaptureRenderedFrame()!;
        Assert.Equal(Colors.OrangeRed, SamplePixel(after, 100, 100));
    }

    private static Color SamplePixel(WriteableBitmap frame, int x, int y)
    {
        using var buffer = frame.Lock();
        Assert.Equal(PixelFormat.Rgba8888, buffer.Format);
        var offset = (y * buffer.RowBytes) + (x * 4);
        var r = Marshal.ReadByte(buffer.Address, offset);
        var g = Marshal.ReadByte(buffer.Address, offset + 1);
        var b = Marshal.ReadByte(buffer.Address, offset + 2);
        var a = Marshal.ReadByte(buffer.Address, offset + 3);
        return Color.FromArgb(a, r, g, b);
    }

    // #150 follow-up: zooming past a tile source's actual max detail must still draw something
    // (an overzoomed ancestor tile), not silently skip the tile.
    private sealed class MaxZoomSpyTileSource : IMapTileSource
    {
        public List<(int Zoom, int X, int Y)> Requested { get; } = new();

        public byte[]? GetTile(int zoom, int x, int y)
        {
            Requested.Add((zoom, x, y));
            return zoom <= 15 ? SolidTilePng.Bytes : null;
        }

        public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds() => null;

        public int? GetMaxZoom() => 15;
    }

    [AvaloniaFact]
    public void Zooming_past_the_sources_max_detail_falls_back_to_the_overzoomed_ancestor_tile()
    {
        var spy = new MaxZoomSpyTileSource();
        var control = new MapCanvasControl
        {
            Width = 256,
            Height = 256,
            TileSource = spy,
            CenterLatitude = 48.0,
            CenterLongitude = 11.0,
            Zoom = 17,
        };
        var window = new Window { Content = control, Width = 256, Height = 256 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Contains(spy.Requested, r => r.Zoom == 17); // tried the exact tile first
        Assert.Contains(spy.Requested, r => r.Zoom == 15); // fell back to the max-zoom ancestor
    }

    // #150 follow-up: scroll-wheel zoom was entirely missing before this fix -- only two tiny
    // +/- buttons existed. Ctrl+scrolling up over the control's exact center must zoom in by one
    // level while keeping that same geo point (the center) stationary, i.e. center unchanged.
    [AvaloniaFact]
    public void CtrlScrolling_up_zooms_in_by_one_level_keeping_the_cursors_geo_point_stationary()
    {
        MapViewChange? change = null;
        var command = new RelayCommand<MapViewChange>(c => change = c);
        var (window, control) = ShowControl(onViewChanged: command);

        var center = control.TranslatePoint(new Point(200, 150), window)!.Value;
        window.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Control);

        Assert.NotNull(change);
        Assert.Equal(16, change!.Zoom);
        Assert.Equal(48.0, change.CenterLatitude, 3);
        Assert.Equal(11.0, change.CenterLongitude, 3);
    }

    [AvaloniaFact]
    public void CtrlScrolling_down_zooms_out_by_one_level()
    {
        MapViewChange? change = null;
        var command = new RelayCommand<MapViewChange>(c => change = c);
        var (window, control) = ShowControl(onViewChanged: command);

        var center = control.TranslatePoint(new Point(200, 150), window)!.Value;
        window.MouseWheel(center, new Vector(0, -1), RawInputModifiers.Control);

        Assert.NotNull(change);
        Assert.Equal(14, change!.Zoom);
    }

    // #150 follow-up regression fix: the map sits inside the tab's outer ScrollViewer (see
    // WasserfoerderungView.axaml) -- plain scroll (no Ctrl) must NOT be swallowed as a zoom, or
    // the whole page becomes unscrollable wherever the cursor happens to be over the map, and a
    // laptop trackpad's rapid wheel-delta stream during a scroll swipe zooms wildly, rendering the
    // map unreadable.
    [AvaloniaFact]
    public void Plain_scrolling_without_ctrl_does_not_zoom()
    {
        MapViewChange? change = null;
        var command = new RelayCommand<MapViewChange>(c => change = c);
        var (window, control) = ShowControl(onViewChanged: command);

        var center = control.TranslatePoint(new Point(200, 150), window)!.Value;
        window.MouseWheel(center, new Vector(0, 1));
        window.MouseWheel(center, new Vector(0, -1));

        Assert.Null(change);
        Assert.Equal(15, control.Zoom);
    }

    // #150 follow-up: pinch-to-zoom (primarily for Android touch) can't be driven through
    // Avalonia.Headless (no touch/gesture simulation API exists there), so its scale->zoom-delta
    // math is unit-tested directly instead.
    [Theory]
    [InlineData(1.0, 0)] // no pinch movement -> no zoom change
    [InlineData(2.0, 1)] // pinch-out to double size -> zoom in one level
    [InlineData(4.0, 2)] // quadruple size -> zoom in two levels
    [InlineData(0.5, -1)] // pinch-in to half size -> zoom out one level
    [InlineData(1.4, 0)] // small movement rounds back to no change
    [InlineData(1.6, 1)] // past the rounding midpoint commits to the next level
    public void PinchScaleToZoomDelta_rounds_to_the_nearest_zoom_level(double relativeScale, int expectedDelta)
        => Assert.Equal(expectedDelta, MapCanvasControl.PinchScaleToZoomDelta(relativeScale));

    [AvaloniaFact]
    public void Left_click_at_the_controls_center_invokes_PointClicked_with_the_center_geo_point()
    {
        GeoPoint? clicked = null;
        var command = new RelayCommand<GeoPoint>(p => clicked = p);
        var (window, control) = ShowControl(onPointClicked: command);

        var center = control.TranslatePoint(new Point(200, 150), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        Assert.NotNull(clicked);
        Assert.Equal(48.0, clicked!.Latitude, 3);
        Assert.Equal(11.0, clicked.Longitude, 3);
    }

    [AvaloniaFact]
    public void Right_click_invokes_UndoRequested_instead_of_PointClicked()
    {
        GeoPoint? clicked = null;
        var undoCount = 0;
        var pointCommand = new RelayCommand<GeoPoint>(p => clicked = p);
        var undoCommand = new RelayCommand(() => undoCount++);
        var (window, control) = ShowControl(onPointClicked: pointCommand, onUndo: undoCommand);

        var center = control.TranslatePoint(new Point(200, 150), window)!.Value;
        window.MouseDown(center, MouseButton.Right);
        window.MouseUp(center, MouseButton.Right);

        Assert.Equal(1, undoCount);
        Assert.Null(clicked);
    }

    // #150 follow-up: cursor-anchored zoom drifts the center with no drag-to-pan to correct it,
    // so once the view drifted off the region entirely there was no way back. Ctrl+drag pans
    // (moving the map opposite the drag direction, standard map UX), without also adding a route
    // point (which plain left-click still does).
    [AvaloniaFact]
    public void CtrlDrag_pans_the_map_and_does_not_add_a_route_point()
    {
        GeoPoint? clicked = null;
        MapViewChange? change = null;
        var pointCommand = new RelayCommand<GeoPoint>(p => clicked = p);
        var viewCommand = new RelayCommand<MapViewChange>(c => change = c);
        var (window, control) = ShowControl(onPointClicked: pointCommand, onViewChanged: viewCommand);

        var start = control.TranslatePoint(new Point(200, 150), window)!.Value;
        var end = control.TranslatePoint(new Point(240, 150), window)!.Value; // drag 40px right

        window.MouseDown(start, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);

        Assert.Null(clicked); // dragging must not also place a route point
        Assert.NotNull(change);
        Assert.Equal(15, change!.Zoom); // pan alone leaves zoom untouched

        // Dragging right reveals content that was to the left -- the center's longitude decreases.
        Assert.True(change.CenterLongitude < 11.0, $"Expected longitude to decrease, was {change.CenterLongitude}");
        Assert.Equal(48.0, change.CenterLatitude, 3); // purely horizontal drag -> latitude unchanged
    }

    [AvaloniaFact]
    public void Plain_drag_without_ctrl_still_adds_a_route_point_and_does_not_pan()
    {
        GeoPoint? clicked = null;
        MapViewChange? change = null;
        var pointCommand = new RelayCommand<GeoPoint>(p => clicked = p);
        var viewCommand = new RelayCommand<MapViewChange>(c => change = c);
        var (window, control) = ShowControl(onPointClicked: pointCommand, onViewChanged: viewCommand);

        var start = control.TranslatePoint(new Point(200, 150), window)!.Value;
        var end = control.TranslatePoint(new Point(240, 150), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);

        Assert.NotNull(clicked); // existing click-to-add-point behaviour is unaffected
        Assert.Null(change); // no Ctrl held -> no pan
    }
}
