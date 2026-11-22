using System.Net;
using System.Runtime.InteropServices;
using System.Web;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

/// <summary>A Luftbild Ebene on the Lagekarte: where its picture lands, and how LUFTBILD LADEN fills the EBENEN list.</summary>
public sealed class LagekarteLuftbildRenderTests : IDisposable
{
    private static readonly GeoPoint Fuerstenfeldbruck = new(48.1785, 11.2555);

    private static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);

    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    [AvaloniaFact]
    public void A_picture_is_drawn_exactly_into_the_rectangle_of_its_box()
    {
        var (window, control) = ShowMap(rotation: 0);
        control.RasterOverlays = [Picture(BoxOnScreen(control, new Rect(100, 50, 200, 200)))];

        using var frame = Settled(window, control);

        Assert.Equal(Red, PixelAt(frame, 200, 150));
        Assert.Equal(Red, PixelAt(frame, 103, 53));
        Assert.Equal(Red, PixelAt(frame, 296, 246));
        Assert.NotEqual(Red, PixelAt(frame, 96, 150));
        Assert.NotEqual(Red, PixelAt(frame, 304, 150));
        Assert.NotEqual(Red, PixelAt(frame, 200, 46));
        Assert.NotEqual(Red, PixelAt(frame, 200, 254));
    }

    [AvaloniaFact]
    public void On_a_turned_map_the_picture_turns_with_the_streets()
    {
        // A box east of the centre, 100 to 180 px right of it on the north-up map.
        var (window, control) = ShowMap(rotation: 0);
        var box = BoxOnScreen(control, new Rect(300, 130, 80, 40));
        control.RasterOverlays = [Picture(box)];
        control.Rotation = 90;

        using var frame = Settled(window, control);

        // A quarter turn clockwise puts east at the bottom: the box now lies below the centre.
        Assert.Equal(Red, PixelAt(frame, 200, 290));
        Assert.NotEqual(Red, PixelAt(frame, 340, 150));
    }

    [AvaloniaFact]
    public void A_hidden_picture_leaves_the_map_as_it_was()
    {
        var (window, control) = ShowMap(rotation: 0);
        control.RasterOverlays = [Picture(BoxOnScreen(control, new Rect(100, 50, 200, 200)))];
        using (var shown = Settled(window, control))
        {
            Assert.Equal(Red, PixelAt(shown, 200, 150));
        }

        control.RasterOverlays = [];
        using var hidden = Settled(window, control);

        Assert.NotEqual(Red, PixelAt(hidden, 200, 150));
    }

    [AvaloniaFact]
    public void A_picture_that_decodes_to_another_shape_than_its_header_claimed_is_not_drawn()
    {
        var (window, control) = ShowMap(rotation: 0);
        var box = BoxOnScreen(control, new Rect(100, 50, 200, 200));
        control.RasterOverlays = [new RasterImage(Guid.NewGuid(), RedPng(64, 64), box, new RasterImageInfo(RasterFormat.Png, 32, 16))];

        using var frame = Settled(window, control);

        Assert.NotEqual(Red, PixelAt(frame, 200, 150));
    }

    [AvaloniaFact]
    public void LUFTBILD_LADEN_adds_two_Ebenen_and_their_licence_line()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var http = new HttpClient(new LdbvHandler());
        _clients.Add(http);
        using var vm = new LagekarteViewModel(session, () => { }, luftbild: new LdbvImageryClient(http));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(Fuerstenfeldbruck, 16));
        var view = new LagekarteView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LoadLuftbildButton");
        Assert.True(button.IsEffectivelyVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return session.Incident.MapOverlays.Count == 2 && !vm.Overlays.IsLoadingLuftbild && vm.Overlays.Items.All(i => !i.IsLoading);
            },
            TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.Overlays.Error);
        Assert.Equal(["Luftbild (LDBV)", "Flurstücksgrenzen (LDBV)"], vm.Overlays.Items.Select(i => i.Name));
        Assert.Equal(2, view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "OverlayList").ItemCount);
        var attribution = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AttributionText");
        Assert.Contains(LdbvImageryClient.Attribution, attribution.Text, StringComparison.Ordinal);

        // The map asked for what it shows: the box around its centre.
        var box = Assert.IsType<GeoBox>(vm.Overlays.Items[0].RasterBounds);
        Assert.InRange(Fuerstenfeldbruck.Longitude, box.West, box.East);
        Assert.InRange(Fuerstenfeldbruck.Latitude, box.South, box.North);
    }

    // The box on the map that the screen rectangle covers on a north-up map.
    private static GeoBox BoxOnScreen(MapCanvasControl control, Rect screen)
    {
        var northWest = Assert.IsType<GeoPoint>(control.ScreenToGeo(screen.TopLeft));
        var southEast = Assert.IsType<GeoPoint>(control.ScreenToGeo(screen.BottomRight));
        return new GeoBox(northWest.Longitude, southEast.Latitude, southEast.Longitude, northWest.Latitude);
    }

    private static RasterImage Picture(GeoBox bounds)
    {
        var png = RedPng(64, 64);
        Assert.True(RasterImage.TryRead(Guid.NewGuid(), png, bounds, out var image, out _));
        return image;
    }

    private static byte[] RedPng(int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var row = new int[width];
            Array.Fill(row, unchecked((int)Red.ToUInt32()));
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), width);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static (Window Window, MapCanvasControl Control) ShowMap(double rotation)
    {
        var control = new MapCanvasControl { Center = Fuerstenfeldbruck, Zoom = 16, Rotation = rotation };
        var window = new Window { Content = control, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, control);
    }

    // The picture is decoded off the UI thread; the map draws it once it is ready.
    private static WriteableBitmap Settled(Window window, MapCanvasControl control)
    {
        using (window.CaptureRenderedFrame())
        {
            // The first frame starts the decode.
        }

        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return !control.IsRasterBusy;
            },
            TimeSpan.FromSeconds(10)));
        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame.");
    }

    private static Color PixelAt(WriteableBitmap frame, int x, int y)
    {
        using var buffer = frame.Lock();
        Assert.Equal(PixelFormat.Rgba8888, buffer.Format);
        var offset = (y * buffer.RowBytes) + (x * 4);
        return Color.FromRgb(
            Marshal.ReadByte(buffer.Address, offset),
            Marshal.ReadByte(buffer.Address, offset + 1),
            Marshal.ReadByte(buffer.Address, offset + 2));
    }

    /// <summary>Answers like the LDBV with the headers of a JPEG and a PNG of the size asked for: nothing to decode, all to check.</summary>
    private sealed class LdbvHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri?.Query ?? string.Empty);
            var width = int.Parse(query["WIDTH"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var height = int.Parse(query["HEIGHT"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var photo = query["LAYERS"] == "by_dop40c";
            var content = new ByteArrayContent(Encoded(width, height, photo));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(photo ? "image/jpeg" : "image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        // A real, decodable picture of the size asked for: the map draws only what decodes.
        private static byte[] Encoded(int width, int height, bool photo)
        {
            using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var stream = new MemoryStream();
            bitmap.Save(stream, photo ? JpegBitmapEncoderOptions.Default : PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
    }
}
