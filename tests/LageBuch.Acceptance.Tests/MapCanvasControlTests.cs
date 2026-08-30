using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LageBuch.App.Shared.Controls;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.Acceptance.Tests;

// Issue #150 (Plan B): the map canvas the operator draws a Wasserförderung route on.
public class MapCanvasControlTests
{
    private sealed class FakeTileSource : IMapTileSource
    {
        public byte[]? GetTile(int zoom, int x, int y) => SolidTilePng.Bytes;
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
                ctx.FillRectangle(Avalonia.Media.Brushes.SteelBlue, new Rect(0, 0, 4, 4));
            using var ms = new MemoryStream();
            bitmap.Save(ms, PngBitmapEncoderOptions.Default);
            return ms.ToArray();
        }
    }

    private static (Window Window, MapCanvasControl Control) ShowControl(
        IReadOnlyList<GeoPoint>? routePoints = null, RelayCommand<GeoPoint>? onPointClicked = null, RelayCommand? onUndo = null)
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
}
