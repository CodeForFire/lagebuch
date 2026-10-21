using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Leaves keyboard focus on a picker clicked inside a <see cref="DataGrid"/> cell, set on the
/// Kräfte grid for its STATUS combo (#536).
///
/// Every left press inside a cell hands focus to the grid: <c>DataGridCell</c> handles
/// PointerPressed with a class handler that runs even for a press the control under the pointer has
/// already handled, and calls <c>OwningGrid.Focus()</c>. A ComboBox handles its press and takes
/// focus from it, so it lost focus again the moment it was clicked, and once its list closed, Down
/// moved the grid's cell cursor to the next row instead of stepping the status.
///
/// This handler runs on the grid, after the cell's, and puts focus back on the control the press
/// landed on. The row is still selected, as the cell did that first. A press on a cell's text
/// still focuses the grid, which is what the arrow keys between rows need. A button is left out:
/// its click is complete in itself, and a row's buttons keep handing focus to the grid as before.
/// </summary>
public static class GridCellFocus
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("IsEnabled", typeof(GridCellFocus));

    static GridCellFocus()
    {
        IsEnabledProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            grid.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            if (e.NewValue is true)
            {
                // Bubbling and handledEventsToo: the cell's handler has to have run first.
                grid.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            }
        });
    }

    public static void SetIsEnabled(DataGrid target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(DataGrid target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid || e.Source is not Visual source
            || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pressed = source.GetSelfAndVisualAncestors()
            .TakeWhile(v => v is not DataGridCell && !ReferenceEquals(v, grid))
            .OfType<InputElement>()
            .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
        if (pressed is null or Button || pressed.IsKeyboardFocusWithin)
        {
            return;
        }

        pressed.Focus(NavigationMethod.Pointer);
    }
}
