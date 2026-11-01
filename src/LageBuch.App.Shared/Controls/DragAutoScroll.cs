using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Scrolls while something is dragged near the edge of a scroller, so a drop target outside the
/// visible area can still be reached (#826). Each scroller moves along the axes it scrolls, and only
/// while the pointer is over it. DragOver comes only with a move, so a timer keeps the scroller going
/// while the pointer rests at the edge.
/// </summary>
public sealed class DragAutoScroll
{
    private readonly ScrollViewer[] _scrollers;
    private readonly Vector[] _steps;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public DragAutoScroll(params ScrollViewer[] scrollers)
    {
        ArgumentNullException.ThrowIfNull(scrollers);
        _scrollers = scrollers;
        _steps = new Vector[scrollers.Length];
        _timer.Tick += (_, _) =>
        {
            // At the end of the scroller there is nothing left to move.
            if (!Apply())
            {
                Stop();
            }
        };
    }

    /// <summary>
    /// How far to scroll per step with the pointer at <paramref name="position"/> along a viewport
    /// <paramref name="extent"/> long: back inside the band at its start, forward inside the band at
    /// its end, faster the nearer the edge, and not at all in between. The band is at most a quarter
    /// of the viewport, so a small one still has a middle to hold a drag in.
    /// </summary>
    public static double Step(double position, double extent, double band = 48, double maxStep = 24)
    {
        var edge = Math.Min(band, extent / 4);
        if (edge <= 0)
        {
            return 0;
        }

        var depth = position < edge ? position - edge
            : position > extent - edge ? position - (extent - edge)
            : 0;
        return Math.Clamp(depth / edge, -1, 1) * maxStep;
    }

    /// <summary>Takes the pointer's place from a DragOver and scrolls one step if it is at an edge.</summary>
    public void Update(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        for (var i = 0; i < _scrollers.Length; i++)
        {
            _steps[i] = StepFor(_scrollers[i], e.GetPosition(_scrollers[i]));
        }

        if (Apply())
        {
            _timer.Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>Ends the scrolling: the drag left, dropped or was cancelled.</summary>
    public void Stop()
    {
        _timer.Stop();
        Array.Clear(_steps);
    }

    private static Vector StepFor(ScrollViewer scroller, Point point)
    {
        var size = scroller.Bounds.Size;
        if (point.X < 0 || point.Y < 0 || point.X > size.Width || point.Y > size.Height)
        {
            return default;
        }

        return new Vector(
            scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0 : Step(point.X, size.Width),
            scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0 : Step(point.Y, size.Height));
    }

    // True when any scroller moved.
    private bool Apply()
    {
        var moved = false;
        for (var i = 0; i < _scrollers.Length; i++)
        {
            var scroller = _scrollers[i];
            var max = scroller.Extent - scroller.Viewport;
            var from = scroller.Offset;
            var to = new Vector(
                Math.Clamp(from.X + _steps[i].X, 0, Math.Max(0, max.Width)),
                Math.Clamp(from.Y + _steps[i].Y, 0, Math.Max(0, max.Height)));
            if (Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) > 0.01)
            {
                scroller.Offset = to;
                moved = true;
            }
        }

        return moved;
    }
}
