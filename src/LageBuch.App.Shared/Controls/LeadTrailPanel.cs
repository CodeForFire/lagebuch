using Avalonia;
using Avalonia.Controls;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Puts its first child at the left and its second at the right of one line, and moves the second
/// onto a line of its own — still right-aligned — once the two no longer fit side by side.
/// </summary>
/// <remarks>
/// Written for the incident header, which used a <c>*,Auto</c> Grid for this. A Grid hands the
/// <c>Auto</c> column everything it asks for and the <c>*</c> column whatever is left, down to
/// nothing; the star column's horizontal StackPanel then kept its full width anyway and was drawn
/// underneath the right-hand group, so on a laptop-width window the Stichwort, the
/// Lagebuchführer readout and the sharing status sat on top of each other. A fixed breakpoint
/// (a container query at N px) would not do either: how wide the line needs to be depends on the
/// Stichwort, the address and the operator's name, not on the window. This panel decides from the
/// children's own desired widths, so the header stays on one line exactly as long as it fits.
/// </remarks>
public sealed class LeadTrailPanel : Panel
{
    /// <summary>Minimum horizontal gap between the two children when they share a line.</summary>
    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<LeadTrailPanel, double>(nameof(ItemSpacing));

    /// <summary>Vertical gap between the lines once the second child has wrapped.</summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<LeadTrailPanel, double>(nameof(LineSpacing));

    public static readonly DirectProperty<LeadTrailPanel, bool> IsWrappedProperty =
        AvaloniaProperty.RegisterDirect<LeadTrailPanel, bool>(nameof(IsWrapped), p => p.IsWrapped);

    private bool _isWrapped;

    static LeadTrailPanel()
    {
        AffectsMeasure<LeadTrailPanel>(ItemSpacingProperty, LineSpacingProperty);
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

    /// <summary>Whether the second child currently sits on its own line.</summary>
    public bool IsWrapped
    {
        get => _isWrapped;
        private set => SetAndRaise(IsWrappedProperty, ref _isWrapped, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (lead, trail) = (Child(0), Child(1));

        // Each child is measured at the full width: the lead is a horizontal run of text that
        // wants its natural width, and the fit decision below is made on those natural widths.
        lead?.Measure(availableSize);
        trail?.Measure(availableSize);

        var leadSize = lead?.DesiredSize ?? default;
        var trailSize = trail?.DesiredSize ?? default;

        var gap = lead is not null && trail is not null ? ItemSpacing : 0;
        var oneLineWidth = leadSize.Width + gap + trailSize.Width;
        var wraps = lead is not null && trail is not null && oneLineWidth > availableSize.Width;
        IsWrapped = wraps;

        return wraps
            ? new Size(
                Math.Min(availableSize.Width, Math.Max(leadSize.Width, trailSize.Width)),
                leadSize.Height + LineSpacing + trailSize.Height)
            : new Size(oneLineWidth, Math.Max(leadSize.Height, trailSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (lead, trail) = (Child(0), Child(1));
        var leadSize = lead?.DesiredSize ?? default;
        var trailSize = trail?.DesiredSize ?? default;

        if (IsWrapped)
        {
            lead?.Arrange(new Rect(0, 0, Math.Min(leadSize.Width, finalSize.Width), leadSize.Height));
            var trailWidth = Math.Min(trailSize.Width, finalSize.Width);
            trail?.Arrange(new Rect(
                finalSize.Width - trailWidth, leadSize.Height + LineSpacing, trailWidth, trailSize.Height));
            return finalSize;
        }

        // One line: both vertically centred on it, the trail flush right.
        var lineHeight = finalSize.Height;
        lead?.Arrange(new Rect(0, (lineHeight - leadSize.Height) / 2, leadSize.Width, leadSize.Height));
        trail?.Arrange(new Rect(
            finalSize.Width - trailSize.Width, (lineHeight - trailSize.Height) / 2, trailSize.Width, trailSize.Height));
        return finalSize;
    }

    // Only the first two children take part; collapsed ones count as absent so the other gets the line.
    private Control? Child(int index) =>
        index < Children.Count && Children[index].IsVisible ? Children[index] : null;
}
