using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// The keyboard half of a triage <see cref="ListBox"/> (#246), the Niedrig | Mittel | Hoch scale
/// styled as <c>ListBox.triage</c>. Two things the plain ListBox does not do:
/// <list type="bullet">
/// <item>Take focus from code. A ListBox is not focusable itself, only its items are, so
/// <see cref="EntryForm"/>'s FirstField and <see cref="Overlay"/>'s InitialFocus, which call
/// <c>Focus()</c> on the control they are given, found nothing to focus. The list now accepts it and
/// hands it straight on to the chosen segment. It stays out of the Tab order itself: Tab still
/// lands on the chosen segment directly, and Shift+Tab out of a segment never bounces back in.</item>
/// <item>Step the scale with Left/Right. ListBox asks its items panel for the neighbour, and the
/// UniformGrid that shares the width out evenly cannot answer, so the arrows did nothing.</item>
/// <item>Let Enter through. The ListBox marks Enter handled after selecting the focused segment,
/// which the arrows have already done, so the form around it (<see cref="Overlay"/>'s SPEICHERN,
/// <see cref="EntryForm"/>'s HINZUFÜGEN) never heard it. Enter submits from every field.</item>
/// </list>
/// </summary>
public static class TriageSegments
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("IsEnabled", typeof(TriageSegments));

    public static void SetIsEnabled(ListBox target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(ListBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    static TriageSegments()
    {
        IsEnabledProperty.Changed.AddClassHandler<ListBox>((list, e) =>
        {
            list.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
            list.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            list.RemoveHandler(InputElement.KeyDownEvent, OnEnterSelected);
            var enabled = e.NewValue is true;
            list.Focusable = enabled;
            KeyboardNavigation.SetIsTabStop(list, !enabled);
            if (enabled)
            {
                list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
                list.AddHandler(InputElement.GotFocusEvent, OnGotFocus);

                // Tunneling above: ahead of the ListBox's own arrow handling, which would find no
                // neighbour. Bubbling here, handled ones too: after the ListBox has taken Enter.
                list.AddHandler(InputElement.KeyDownEvent, OnEnterSelected, RoutingStrategies.Bubble, handledEventsToo: true);
            }
        });
    }

    private static void OnEnterSelected(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = false; // on to the form, which submits
        }
    }

    private static void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        // Only focus that landed on the list itself; a segment taking focus is already right.
        if (sender is ListBox list && ReferenceEquals(e.Source, list))
        {
            FocusSegment(list, Math.Max(list.SelectedIndex, 0));
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var step = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            _ => 0,
        };
        if (step == 0)
        {
            return;
        }

        // The ends hold: Hoch does not wrap round to Niedrig.
        var index = Math.Clamp(list.SelectedIndex + step, 0, list.ItemCount - 1);
        list.SelectedIndex = index;
        FocusSegment(list, index);
        e.Handled = true;
    }

    // Tab, so the segment shows its focus ring: the keyboard user is the one who gets here.
    private static void FocusSegment(ListBox list, int index) =>
        list.ContainerFromIndex(index)?.Focus(NavigationMethod.Tab);
}
