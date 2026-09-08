using Avalonia;
using Avalonia.Controls;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Lays children out in equal-width columns that fill the available line, wrapping to a new line
/// rather than letting a column shrink below <see cref="MinItemWidth"/>.
/// </summary>
/// <remarks>
/// Neither <c>WrapPanel</c> nor <c>UniformGrid</c> does this. WrapPanel keeps each child at its own
/// width, so a floor with two units ends far short of one with fourteen and the CO matrix reads as a
/// ragged list instead of a building. UniformGrid fills the line, but has no lower bound, so a
/// 14-unit floor in a narrow window is squeezed until the ppm reading is trimmed away — which on a
/// CO protocol is the one number that must not disappear. This panel does both: every floor spans
/// the same full width, and a floor too crowded for that width wraps instead of shrinking.
///
/// Column width stays uniform across all lines of a floor, so a wrapped floor's final line is short
/// and left-aligned rather than stretching its few remaining tiles wider than the ones above them.
/// </remarks>
public sealed class EqualWidthWrapPanel : Panel
{
    /// <summary>Legibility floor: a column is never narrower than this, the panel wraps instead.</summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<EqualWidthWrapPanel, double>(nameof(MinItemWidth), 96);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<EqualWidthWrapPanel, double>(nameof(ItemSpacing));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<EqualWidthWrapPanel, double>(nameof(LineSpacing));

    static EqualWidthWrapPanel()
    {
        AffectsMeasure<EqualWidthWrapPanel>(MinItemWidthProperty, ItemSpacingProperty, LineSpacingProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        if (count == 0)
        {
            return default;
        }

        var columns = ColumnsFor(availableSize.Width, count);
        var itemWidth = ItemWidthFor(availableSize.Width, columns);

        var rowHeight = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(itemWidth, availableSize.Height));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (count + columns - 1) / columns;
        var width = double.IsInfinity(availableSize.Width)
            ? (columns * itemWidth) + ((columns - 1) * ItemSpacing)
            : availableSize.Width;

        return new Size(width, (rows * rowHeight) + ((rows - 1) * LineSpacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        if (count == 0)
        {
            return finalSize;
        }

        var columns = ColumnsFor(finalSize.Width, count);
        var itemWidth = ItemWidthFor(finalSize.Width, columns);

        var rowHeight = 0d;
        foreach (var child in Children)
        {
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            Children[i].Arrange(new Rect(
                column * (itemWidth + ItemSpacing),
                row * (rowHeight + LineSpacing),
                itemWidth,
                rowHeight));
        }

        return finalSize;
    }

    private int ColumnsFor(double availableWidth, int count)
    {
        // An infinite width (a measuring parent that imposes no constraint) would otherwise divide
        // by infinity and collapse every column: fall back to one line of MinItemWidth columns.
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return count;
        }

        var fit = (int)((availableWidth + ItemSpacing) / (MinItemWidth + ItemSpacing));
        return Math.Clamp(fit, 1, count);
    }

    private double ItemWidthFor(double availableWidth, int columns)
    {
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return MinItemWidth;
        }

        return Math.Max(MinItemWidth, (availableWidth - ((columns - 1) * ItemSpacing)) / columns);
    }
}
