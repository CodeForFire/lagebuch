using Avalonia;
using Avalonia.Controls;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The Führung board's top row: the Einsatzleitung Feuerwehr with the partner Einsatzleitungen and
/// the Fachberater beside it, joined by a dashed line each, as on the A3 sheet: peers, not a chain of
/// command, hence dashed. Wide, one row of equal-width cards that scrolls with the chart; compact (a
/// phone), one full-width column without lines. Lays out only; <see cref="OrgChart"/> draws the
/// <see cref="Connectors"/>.
/// </summary>
public sealed class OrgPeersPanel : Panel
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<OrgPeersPanel, bool>(nameof(IsCompact));

    // The cards' width, the 320 the sections use too; the gap the dash spans; the dash's height,
    // about the cards' name line; the phone's row spacing.
    private const double ItemWidth = 320;
    private const double Spacing = 32;
    private const double LineY = 34;
    private const double CompactSpacing = 12;

    static OrgPeersPanel()
    {
        AffectsMeasure<OrgPeersPanel>(IsCompactProperty);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>The dashed lines between neighbouring cards, in this panel's coordinates; none when compact.</summary>
    public IReadOnlyList<(Point From, Point To)> Connectors
    {
        get
        {
            if (IsCompact)
            {
                return [];
            }

            var visible = Children.Where(c => c.IsVisible).ToList();
            return [.. visible.Zip(visible.Skip(1), (a, b) => (new Point(a.Bounds.Right, LineY), new Point(b.Bounds.Left, LineY)))];
        }
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
            double height = 0, width = 0;
            foreach (var child in visible)
            {
                child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                height += child.DesiredSize.Height;
                width = Math.Max(width, child.DesiredSize.Width);
            }

            return new Size(width, height + (CompactSpacing * (visible.Count - 1)));
        }

        double rowHeight = 0;
        foreach (var child in visible)
        {
            child.Measure(new Size(ItemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        return new Size((ItemWidth * visible.Count) + (Spacing * (visible.Count - 1)), rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double offset = 0;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            if (IsCompact)
            {
                child.Arrange(new Rect(0, offset, finalSize.Width, child.DesiredSize.Height));
                offset += child.DesiredSize.Height + CompactSpacing;
            }
            else
            {
                child.Arrange(new Rect(offset, 0, ItemWidth, child.DesiredSize.Height));
                offset += ItemWidth + Spacing;
            }
        }

        return finalSize;
    }
}
