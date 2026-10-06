using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Arrow keys across a matrix of tile buttons (#543): set on an <see cref="ItemsControl"/> of rows
/// whose template holds an inner ItemsControl of buttons, as the CO-Messung's floors hold their
/// Wohnungen. ← → move to the previous or next tile in the same row, ↑ ↓ to the tile at the same
/// position one row up or down, or that row's last tile when it is shorter. A row with no tiles is
/// passed over. At an edge focus stays where it is.
///
/// Movement follows the tiles' order, not where they are drawn: a long floor wraps over several
/// lines, and ↓ still means the floor below, which is how the matrix is read. Enter needs nothing
/// here — a focused tile is a button. The handler is <b>tunneling</b>, so the surrounding
/// ScrollViewer does not scroll on the arrow instead.
/// </summary>
public static class GridArrowNavigation
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("IsEnabled", typeof(GridArrowNavigation));

    static GridArrowNavigation()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((rows, e) =>
        {
            rows.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            if (e.NewValue is true)
            {
                rows.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
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

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || sender is not ItemsControl rows
            || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)
            || e.Source is not Visual source || source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault() is not { } tile
            || !Locate(rows, tile, out var row, out var column))
        {
            return;
        }

        var target = e.Key switch
        {
            Key.Left => TileAt(rows, row, column - 1),
            Key.Right => TileAt(rows, row, column + 1),
            Key.Up => TileInRow(rows, row, -1, column),
            _ => TileInRow(rows, row, +1, column),
        };

        target?.Focus(NavigationMethod.Directional);
        target?.BringIntoView();
        e.Handled = true; // at an edge too: the arrow belongs to the matrix, not to the page's scrolling
    }

    // Where the tile sits: the row's index in the outer list, and its own in that row's list.
    private static bool Locate(ItemsControl rows, Button tile, out int row, out int column)
    {
        row = -1;
        column = -1;
        foreach (var control in tile.GetSelfAndVisualAncestors().OfType<Control>().TakeWhile(c => !ReferenceEquals(c, rows)))
        {
            if (column < 0 && Tiles(control) is { } inner && inner.IndexFromContainer(control) is >= 0 and var index)
            {
                column = index;
            }

            if (rows.IndexFromContainer(control) is >= 0 and var rowIndex)
            {
                row = rowIndex;
                break;
            }
        }

        return row >= 0 && column >= 0;
    }

    // The inner list a tile container belongs to.
    private static ItemsControl? Tiles(Control container) =>
        container.GetVisualAncestors().OfType<ItemsControl>().FirstOrDefault();

    private static ItemsControl? TilesOfRow(ItemsControl rows, int row) =>
        rows.ContainerFromIndex(row)?.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();

    private static Button? TileAt(ItemsControl rows, int row, int column) =>
        TilesOfRow(rows, row) is { } tiles && column >= 0 && column < tiles.ItemCount
            ? tiles.ContainerFromIndex(column)?.GetSelfAndVisualDescendants().OfType<Button>().FirstOrDefault()
            : null;

    // The nearest row in that direction that has tiles, at the same position or its last tile.
    private static Button? TileInRow(ItemsControl rows, int row, int step, int column)
    {
        for (var next = row + step; next >= 0 && next < rows.ItemCount; next += step)
        {
            if (TilesOfRow(rows, next) is { ItemCount: > 0 } tiles)
            {
                return TileAt(rows, next, Math.Min(column, tiles.ItemCount - 1));
            }
        }

        return null;
    }
}
