using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Draws a <see cref="TextBlock"/> from <see cref="TextSegment"/> runs, marking the search hits
/// amber so the Lagebuchführer sees <em>why</em> a row matched: the word "Gefahrgut" lit up
/// inside a Notiz explains itself where a bare list of names does not.
/// </summary>
/// <remarks>
/// Which runs are hits is decided in the view model and unit-tested there; this only turns them
/// into <see cref="Run"/>s, which a binding cannot do because <see cref="TextBlock.Inlines"/> is
/// not a bindable collection of view-model items. Amber, not Signal red: red is the app's
/// primary-action and alarm colour, and a search hit is neither.
/// </remarks>
public static class HighlightedText
{
    public static readonly AttachedProperty<IReadOnlyList<TextSegment>?> SegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TextSegment>?>("Segments", typeof(HighlightedText));

    public static void SetSegments(TextBlock target, IReadOnlyList<TextSegment>? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SegmentsProperty, value);
    }

    public static IReadOnlyList<TextSegment>? GetSegments(TextBlock target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(SegmentsProperty);
    }

    static HighlightedText()
    {
        SegmentsProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
            Render(block, e.NewValue as IReadOnlyList<TextSegment>));
    }

    private static void Render(TextBlock block, IReadOnlyList<TextSegment>? segments)
    {
        var inlines = new InlineCollection();
        foreach (var segment in segments ?? Array.Empty<TextSegment>())
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.Foreground = Brush(block, "AmberBrush");
                run.Background = Brush(block, "AmberWashBrush");
                run.FontWeight = FontWeight.SemiBold;
            }

            inlines.Add(run);
        }

        block.Inlines = inlines;
    }

    // The binding can set the runs while an item template is still being built, before the block
    // has a parent to look the token up through, so the application's resources are the fallback.
    private static IBrush? Brush(TextBlock block, string key) =>
        block.TryFindResource(key, out var value) || (Application.Current?.TryFindResource(key, out value) ?? false)
            ? value as IBrush
            : null;
}
