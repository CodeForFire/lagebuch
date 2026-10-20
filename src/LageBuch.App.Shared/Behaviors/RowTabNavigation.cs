using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Keeps Tab and Shift+Tab walking row by row through an <see cref="ItemsControl"/> whose panel
/// builds only the rows on screen, set on the Stammdaten editor's long lists. Two things break
/// there that a fully built list never shows:
///
/// <list type="bullet">
/// <item>Tab follows the visual tree, and the panel does not keep its children in item order: after
/// a jump to the bottom, as "+ HINZUFÜGEN" makes, they run backwards, and Shift+Tab left the list
/// from the last row. So every row is its own tab group ranked by its item index, and the list a
/// group around them; Avalonia orders a group's members by TabIndex before tree order.</item>
/// <item>A row below or above the fold does not exist until it is scrolled to, so Tab from the
/// last row built left the list for the editor's header. So a Tab that leaves a row for one not
/// built yet builds it and moves focus in: to its first field, or its last on Shift+Tab, as walking
/// a fully built list does.</item>
/// </list>
///
/// Everywhere else, inside a row, between rows already built and past the first and last row,
/// Avalonia's own navigation does the work.
/// </summary>
public static class RowTabNavigation
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("IsEnabled", typeof(RowTabNavigation));

    static RowTabNavigation()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            list.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            list.ContainerPrepared -= OnContainerPrepared;
            list.ContainerIndexChanged -= OnContainerIndexChanged;
            if (e.NewValue is true)
            {
                KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Local);
                list.ContainerPrepared += OnContainerPrepared;
                list.ContainerIndexChanged += OnContainerIndexChanged;

                // Tunnel: this has to see the Tab before the focus manager resolves it.
                list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            }
        });
    }

    public static void SetIsEnabled(ItemsControl target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(ItemsControl target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    private static void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e) => Rank(e.Container, e.Index);

    private static void OnContainerIndexChanged(object? sender, ContainerIndexChangedEventArgs e) => Rank(e.Container, e.NewIndex);

    private static void Rank(Control container, int index)
    {
        KeyboardNavigation.SetTabNavigation(container, KeyboardNavigationMode.Local);
        KeyboardNavigation.SetTabIndex(container, index);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || sender is not ItemsControl list || e.Source is not Visual focused)
        {
            return;
        }

        var back = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var index = RowIndexOf(list, focused, out var row);
        var neighbour = back ? index - 1 : index + 1;
        if (row is null || neighbour < 0 || neighbour >= list.ItemCount
            || list.ContainerFromIndex(neighbour) is not null || !IsEdgeOf(row, focused, back))
        {
            return;
        }

        // Built here and focused here: a row realized during this key press is not laid out yet
        // when Avalonia resolves the Tab, which then skips past it out of the list.
        list.ScrollIntoView(neighbour);
        if (list.ContainerFromIndex(neighbour) is not { } next || TabStops(next) is not { Count: > 0 } stops)
        {
            return;
        }

        var target = back ? stops[^1] : stops[0];
        target.Focus(NavigationMethod.Tab, e.KeyModifiers);
        if (target is Control control)
        {
            control.BringIntoView();
        }

        e.Handled = true;
    }

    private static List<InputElement> TabStops(Control row) =>
        row.GetVisualDescendants().OfType<InputElement>()
            .Where(s => s.Focusable && s.IsEffectivelyVisible && s.IsEffectivelyEnabled && KeyboardNavigation.GetIsTabStop(s))
            .ToList();

    // Whether the focused control is the row's last tab stop (or first, going back), so the Tab
    // leaves the row. Elsewhere in the row a neighbour scrolled in early would jump the list.
    private static bool IsEdgeOf(Control row, Visual focused, bool back)
    {
        var stops = TabStops(row);
        if (stops.Count == 0)
        {
            return true;
        }

        var edge = back ? stops[0] : stops[^1];
        return ReferenceEquals(edge, focused) || edge.IsVisualAncestorOf(focused) || focused.IsVisualAncestorOf(edge);
    }

    // The index and container of the row the visual sits in, or -1 and null outside every row.
    private static int RowIndexOf(ItemsControl list, Visual visual, out Control? row)
    {
        foreach (var ancestor in visual.GetSelfAndVisualAncestors().OfType<Control>())
        {
            if (ReferenceEquals(ancestor, list))
            {
                break;
            }

            var index = list.IndexFromContainer(ancestor);
            if (index >= 0)
            {
                row = ancestor;
                return index;
            }
        }

        row = null;
        return -1;
    }
}
