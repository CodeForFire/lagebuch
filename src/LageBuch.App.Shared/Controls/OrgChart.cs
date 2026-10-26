using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Draws the Führung board's org chart lines under the cards it wraps: the solid lines each
/// <see cref="OrgChildrenPanel"/> works out, and the dashed ones between the peers of each
/// <see cref="OrgPeersPanel"/>. One drawer for the whole chart, because a Panel cannot draw in
/// Avalonia 12; it sits inside the chart's scroll viewer, so the lines scroll and clip with the cards.
/// </summary>
public sealed class OrgChart : Decorator
{
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<OrgChart, IBrush?>(nameof(LineBrush));

    private const double LineThickness = 1.5;

    static OrgChart()
    {
        AffectsRender<OrgChart>(LineBrushProperty);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        if (LineBrush is not { } brush)
        {
            return;
        }

        var solid = new Pen(brush, LineThickness);
        var dashed = new Pen(brush, LineThickness, new DashStyle([3, 2], 0));
        foreach (var visual in this.GetVisualDescendants())
        {
            var (lines, pen) = visual switch
            {
                OrgChildrenPanel children => (children.Segments, solid),
                OrgPeersPanel peers => (peers.Connectors, dashed),
                _ => ([], solid),
            };

            foreach (var (from, to) in lines)
            {
                if (visual.TranslatePoint(from, this) is { } a && visual.TranslatePoint(to, this) is { } b)
                {
                    context.DrawLine(pen, a, b);
                }
            }
        }
    }

    // The lines follow every card and branch below, but a branch can be laid out again without this
    // control being: a new Unterabschnitt that leaves the chart's size alone stops the layout pass
    // below here, and its line showed only once something else redrew, like scrolling (field
    // report). So redraw after every layout pass; drawing does not lay out again, so this cannot loop.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => InvalidateVisual();
}
