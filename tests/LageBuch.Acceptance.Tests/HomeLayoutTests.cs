using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The Übersicht used to re-lay-out horizontally while it was scrolled: the content column was
// sized to its widest child, and that child is a virtualized ListBox whose desired width changes
// as rows are realized and recycled. A recent entry with a long path made the column 720px wide;
// scrolling it out of view dropped the column to ~643px and the whole page jumped sideways.
//
// The second group pins the corner decoration, which rendered as a 320x320 grey rectangle with
// hard left and bottom edges instead of a glow fading into the corner.
public class HomeLayoutTests
{
    private const double ColumnMaxWidth = 720.0;

    // The MaxHeight the recent list used to carry, which gave it a scrollbar of its own.
    private const double OldMaxHeight = 420.0;

    // Long enough that the row's path TextBlock wants more than the column's MaxWidth. Measure,
    // not arrange, is what matters here: TextTrimming ellipsizes on arrange but the TextBlock
    // still reports the full untrimmed width when it is measured.
    private const string LongPath =
        "/home/operator/Development/arbeit/traeger/feuerwehr/lagebuch/docs/samples/uebung-mit-langem-namen.fwincident";

    private static readonly string[] ShortPaths =
    {
        "/home/operator/Einsaetze/20260906-1344.fwincident",
        "/home/operator/Einsaetze/20260906-1224.fwincident",
        "/home/operator/Einsaetze/20260831-1519.fwincident",
    };

    private sealed class Md : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class Recent : IRecentFilesStore
    {
        private readonly IReadOnlyList<string> _list;

        public Recent(params string[] paths) => _list = paths;

        public IReadOnlyList<string> GetRecent() => _list;

        public void Add(string path)
        {
        }
    }

    private static Window ShowHome(params string[] recentPaths) => ShowHome(1100, recentPaths);

    private static Window ShowHome(double width, params string[] recentPaths)
    {
        var vm = new HomeViewModel(
            new FakeStore(),
            new Md(),
            new Recent(recentPaths),
            new FakeDialogs(),
            new FixedClock(),
            new ManualTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0");

        var window = new Window { Content = new HomeView { DataContext = vm }, Width = width, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // By name, like OpenErrorBanner and RecentFilesHint: matching on MaxWidth/Width would compare
    // doubles for exact equality, which CodeQL rightly rejects (cs/equality-on-floats).
    private static T ByName<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static StackPanel ContentColumn(Window window) => ByName<StackPanel>(window, "ContentColumn");

    private static Border Glow(Window window) => ByName<Border>(window, "HazardGlow");

    private static ScrollViewer PageScroller(Window window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().First();

    [AvaloniaFact]
    public void Content_column_width_does_not_depend_on_which_recent_rows_are_realized()
    {
        var withLongPath = ContentColumn(ShowHome(new[] { LongPath }.Concat(ShortPaths).ToArray())).Bounds.Width;
        var withoutLongPath = ContentColumn(ShowHome(ShortPaths)).Bounds.Width;

        // The failure this guards is the page jumping between the two widths mid-scroll, so the
        // two must agree -- and agree on the full column width, not on whatever the text happens
        // to measure.
        Assert.Equal(withLongPath, withoutLongPath, precision: 0);
        Assert.Equal(ColumnMaxWidth, withoutLongPath, precision: 0);
    }

    // The column is capped at 720px and the window is wider, so it has to be centred in the
    // leftover space. This is the guard on using HorizontalAlignment="Stretch" to get a
    // content-independent width: Avalonia centres a Stretch element that MaxWidth has made
    // narrower than its slot, and if that ever stopped being true the page would slide left.
    [AvaloniaFact]
    public void Content_column_stays_centred()
    {
        var window = ShowHome(ShortPaths);
        var column = ContentColumn(window);
        var page = PageScroller(window);

        // Measured against the viewport rather than the window: a page scrollbar narrows the slot
        // the column is centred in, and that is still centred.
        var centre = column.TranslatePoint(new Point(column.Bounds.Width / 2, 0), page)!.Value.X;

        Assert.True(
            Math.Abs(centre - (page.Viewport.Width / 2)) <= 1.0,
            $"the column's centre is at x={centre} in a {page.Viewport.Width}px viewport");
    }

    // One scrollbar, on the page. A scrollable ListBox nested in the page's ScrollViewer ate the
    // wheel until it bottomed out, so the list moved several rows before the page moved at all.
    [AvaloniaFact]
    public void Recent_list_does_not_scroll_on_its_own()
    {
        var paths = Enumerable.Range(0, 10)
            .Select(i => $"/home/operator/Einsaetze/2026090{i}-1200.fwincident")
            .ToArray();
        var window = ShowHome(paths);

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        var inner = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var page = PageScroller(window);

        Assert.Equal(10, list.ItemCount);

        // The list lays out all ten rows rather than clipping them into a window of its own, so its
        // own scroller has nothing left to scroll.
        Assert.True(
            list.Bounds.Height > OldMaxHeight,
            $"the recent list is only {list.Bounds.Height}px tall for ten rows -- it is still capped");
        Assert.True(
            inner.Extent.Height <= inner.Viewport.Height,
            $"the list still scrolls on its own (extent {inner.Extent.Height}, viewport {inner.Viewport.Height})");

        // ...and the wheel, delivered over the middle of the list, moves the page.
        var overflow = $"the page does not overflow (extent {page.Extent.Height}, viewport "
            + $"{page.Viewport.Height}) -- there would be nothing for the wheel to do";
        Assert.True(page.Extent.Height > page.Viewport.Height, overflow);

        Assert.Equal(0, page.Offset.Y, precision: 0);
        window.MouseWheel(new Point(window.Width / 2, 400), new Vector(0, -3));
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            page.Offset.Y > 0,
            "wheeling over the recent list left the page where it was -- the list ate the event");
    }

    // The decoration is a glow, so it has to reach the background before it reaches its own
    // bounds. It did not: a LinearGradientBrush fading to `Transparent` (which is transparent
    // WHITE, and interpolates through grey) with its last stop at offset 0.7 cut the box
    // mid-fade, leaving a one-pixel step from (0,0,0) to (4,3,3) down its left edge.
    [AvaloniaFact]
    public void Corner_glow_fades_out_before_its_own_edges()
    {
        // 1400px wide: the centred 720px column then ends at x=1060 and the decoration starts at
        // x=1080, so nothing but the decoration can put a step on the pixels this scans.
        var window = ShowHome(1400, ShortPaths);
        var bounds = Glow(window).Bounds;

        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = new FramePixels(frame);

        var left = (int)Math.Round(bounds.Left);
        var bottom = (int)Math.Round(bounds.Bottom);
        var top = Math.Max(0, (int)Math.Round(bounds.Top));
        var right = Math.Min(pixels.Width - 1, (int)Math.Round(bounds.Right));

        var seams = new List<string>();
        for (var y = top; y < Math.Min(bottom, pixels.Height); y++)
        {
            if (pixels.Raw(left, y) != pixels.Raw(left - 1, y))
            {
                seams.Add($"left edge at y={y}");
            }
        }

        for (var x = left; x < right; x++)
        {
            if (pixels.Raw(x, bottom - 1) != pixels.Raw(x, bottom + 1))
            {
                seams.Add($"bottom edge at x={x}");
            }
        }

        Assert.True(
            seams.Count == 0,
            $"the decoration has a visible rectangular edge at {seams.Count} pixels, first: {seams.FirstOrDefault()}");
    }

    // ...and it has to glow at the corner it decorates. StartPoint="0%,0%" put the bright end at
    // the box's top-LEFT while the box is anchored top-right, so the glow floated in the middle of
    // the page and the corner itself was the dimmest part of it.
    [AvaloniaFact]
    public void Corner_glow_is_brightest_at_the_corner()
    {
        var window = ShowHome(1400, ShortPaths);

        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = new FramePixels(frame);

        // Both samples sit inside the decoration under either layout; 40px in from the right keeps
        // clear of the page scrollbar.
        var atCorner = pixels.Brightness(pixels.Width - 40, 40);
        var awayFromCorner = pixels.Brightness(pixels.Width - 240, 40);

        var wrongWay = $"the glow is dimmer at the corner ({atCorner}) than 200px inside the page "
            + $"({awayFromCorner}) -- it is pointing the wrong way";
        Assert.True(atCorner > awayFromCorner, wrongWay);
    }
}

// Reads a captured frame pixel by pixel. Every assertion here either compares whole pixels or
// sums the three colour channels, so the BGRA/RGBA question never arises.
internal sealed class FramePixels : IDisposable
{
    private readonly ILockedFramebuffer _fb;

    public FramePixels(WriteableBitmap bitmap) => _fb = bitmap.Lock();

    public int Width => _fb.Size.Width;

    public int Height => _fb.Size.Height;

    public int Raw(int x, int y) => Marshal.ReadInt32(_fb.Address, (y * _fb.RowBytes) + (x * 4));

    public int Brightness(int x, int y)
    {
        var raw = Raw(x, y);
        return (raw & 0xFF) + ((raw >> 8) & 0xFF) + ((raw >> 16) & 0xFF);
    }

    public void Dispose() => _fb.Dispose();
}
