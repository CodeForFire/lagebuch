using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The children of one head on the Führung board's org chart: the Abschnitte below the
/// Einsatzleitung, the Unterabschnitte below their Abschnitt. Used as the ItemsPanel of the list
/// right under the head, which it finds itself (see <see cref="HeadOf"/>), so it works inside
/// DataTemplates where an ElementName cannot reach.
/// </summary>
/// <remarks>
/// Wide, the children stand in a row under a bus: a line drops from the head to the bus and from
/// the bus to each child, like the A3 sheet. Compact (a phone), they stack indented under the head
/// with an elbow line into each, like a file tree. This panel only lays out and works the lines out
/// (<see cref="Segments"/>); a Panel cannot draw in Avalonia 12, so <see cref="OrgChart"/> draws them.
/// </remarks>
public sealed class OrgChildrenPanel : Panel
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<OrgChildrenPanel, bool>(nameof(IsCompact));

    /// <summary>Marks the element inside a head the lines start from, when the head is more than one card (the top row).</summary>
    public static readonly AttachedProperty<bool> IsAnchorProperty =
        AvaloniaProperty.RegisterAttached<OrgChildrenPanel, Control, bool>("IsAnchor");

    // Room above the row for the drop, the bus and the drops; the gap between children; the
    // compact indent and where its elbow meets a child (about the card's name line).
    private const double Gap = 28;
    private const double Spacing = 24;
    private const double Indent = 22;
    private const double CompactSpacing = 10;
    private const double ElbowY = 18;

    static OrgChildrenPanel()
    {
        AffectsMeasure<OrgChildrenPanel>(IsCompactProperty);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// The lines, worked out from where the head and the children stand now, in this panel's
    /// coordinates: a drop from the head starts above 0. Empty without a head or children.
    /// </summary>
    public IReadOnlyList<(Point From, Point To)> Segments
    {
        get
        {
            var segments = new List<(Point From, Point To)>();
            var children = Children.Where(c => c.IsVisible).ToList();
            if (children.Count == 0 || HeadOf(this) is not { } head
                || head.TranslatePoint(new Point(head.Bounds.Width / 2, head.Bounds.Height), this) is not { } headBottom)
            {
                return segments;
            }

            if (IsCompact)
            {
                var x = Indent / 2;
                segments.Add((new Point(x, Math.Min(headBottom.Y, 0)), new Point(x, children[^1].Bounds.Top + ElbowY)));
                foreach (var child in children)
                {
                    var y = child.Bounds.Top + ElbowY;
                    segments.Add((new Point(x, y), new Point(child.Bounds.Left, y)));
                }

                return segments;
            }

            var busY = Gap / 2;
            var first = children[0].Bounds.Center.X;
            var last = children[^1].Bounds.Center.X;
            segments.Add((headBottom, new Point(headBottom.X, busY)));
            segments.Add((new Point(Math.Min(headBottom.X, first), busY), new Point(Math.Max(headBottom.X, last), busY)));
            foreach (var child in children)
            {
                segments.Add((new Point(child.Bounds.Center.X, busY), new Point(child.Bounds.Center.X, child.Bounds.Top)));
            }

            return segments;
        }
    }

    public static bool GetIsAnchor(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(IsAnchorProperty);
    }

    public static void SetIsAnchor(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(IsAnchorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        if (visible.Count == 0)
        {
            return default;
        }

        if (IsCompact)
        {
            var inner = new Size(Math.Max(0, availableSize.Width - Indent), double.PositiveInfinity);
            double height = 0, width = 0;
            foreach (var child in visible)
            {
                child.Measure(inner);
                height += child.DesiredSize.Height;
                width = Math.Max(width, child.DesiredSize.Width);
            }

            return new Size(width + Indent, height + (CompactSpacing * visible.Count));
        }

        double rowWidth = 0, rowHeight = 0;
        foreach (var child in visible)
        {
            child.Measure(Size.Infinity);
            rowWidth += child.DesiredSize.Width;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        return new Size(rowWidth + (Spacing * (visible.Count - 1)), Gap + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        if (IsCompact)
        {
            var y = CompactSpacing;
            foreach (var child in visible)
            {
                child.Arrange(new Rect(Indent, y, Math.Max(0, finalSize.Width - Indent), child.DesiredSize.Height));
                y += child.DesiredSize.Height + CompactSpacing;
            }
        }
        else
        {
            double x = 0;
            foreach (var child in visible)
            {
                child.Arrange(new Rect(x, Gap, child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width + Spacing;
            }
        }

        return finalSize;
    }

    /// <summary>
    /// The head the lines come from: in the branch holding this panel's list, the element right
    /// above that list, or the element inside it marked <see cref="IsAnchorProperty"/>.
    /// </summary>
    private static Control? HeadOf(OrgChildrenPanel panel)
    {
        var list = panel.FindAncestorOfType<ItemsControl>();
        if (list?.GetVisualParent() is not Panel branch)
        {
            return null;
        }

        var index = branch.Children.IndexOf(list);
        if (index <= 0)
        {
            return null;
        }

        var above = branch.Children[index - 1];
        return above.GetVisualDescendants().OfType<Control>().FirstOrDefault(GetIsAnchor) ?? above;
    }
}
