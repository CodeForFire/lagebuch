using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Tab through a <see cref="DataGrid"/> enters the selected row's controls and then leaves the grid
/// (#724). The arrows choose the row, and <see cref="RowKeys"/> gives Enter, F2 and Del to the
/// selected row.
///
/// Before this, Tab entered every row in turn, and that failed in three ways:
///
/// <list type="bullet">
/// <item>The grid builds only the rows on screen and recycles them. After a scroll they sit in the
/// visual tree in reverse order, so Tab walked the rows bottom-up, the trap
/// <see cref="RowTabNavigation"/> fixes for the Stammdaten lists.</item>
/// <item>Tab entered the row built last, never the selected one.</item>
/// <item>A row not built was unreachable, and every row cost one or two Tab stops. An ETB with 300
/// entries took about 600 presses to get past.</item>
/// </list>
///
/// Every other row is closed to Tab. Focusing a control in a row by click selects that row, so the
/// row Tab walks is always the one focus is in. A Tab from the grid itself first scrolls the selected
/// row into view if it has been scrolled away, so the row exists to be entered.
/// </summary>
public static class SelectedRowTab
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("IsEnabled", typeof(SelectedRowTab));

    static SelectedRowTab()
    {
        IsEnabledProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            grid.LoadingRow -= OnLoadingRow;
            grid.SelectionChanged -= OnSelectionChanged;
            grid.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
            grid.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            if (e.NewValue is true)
            {
                grid.LoadingRow += OnLoadingRow;
                grid.SelectionChanged += OnSelectionChanged;
                grid.AddHandler(InputElement.GotFocusEvent, OnGotFocus);

                // Tunnel: the row has to exist before the focus manager resolves the Tab.
                grid.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
                OpenOnlySelected(grid);
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

    // A recycled row arrives with its new item, and is opened or closed for that one.
    private static void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            Open(e.Row, grid.SelectedItem);
        }
    }

    private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            OpenOnlySelected(grid);
        }
    }

    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is DataGrid grid && e.Source is Visual focused
            && focused.FindAncestorOfType<DataGridRow>() is { DataContext: { } item }
            && !ReferenceEquals(item, grid.SelectedItem))
        {
            grid.SelectedItem = item;
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None || sender is not DataGrid grid
            || !ReferenceEquals(e.Source, grid))
        {
            return;
        }

        // Focus put on the grid by code has selected nothing; the grid selects its current row on a
        // Tab landing on it anyway, so a Tab leaving it does the same, falling back to the first.
        if (grid.SelectedItem is null)
        {
            grid.SelectedItem = Rows(grid).FirstOrDefault(row => row.GetVisualDescendants().OfType<DataGridCell>().Any(c => c.Classes.Contains(":current")))?.DataContext
                ?? grid.ItemsSource?.Cast<object>().FirstOrDefault();
        }

        if (grid.SelectedItem is not { } selected || Rows(grid).Any(row => ReferenceEquals(row.DataContext, selected)))
        {
            return;
        }

        grid.ScrollIntoView(selected, null);
        grid.UpdateLayout();
    }

    private static void OpenOnlySelected(DataGrid grid)
    {
        foreach (var row in Rows(grid))
        {
            Open(row, grid.SelectedItem);
        }
    }

    private static void Open(DataGridRow row, object? selected) =>
        KeyboardNavigation.SetTabNavigation(
            row,
            selected is not null && ReferenceEquals(row.DataContext, selected) ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);

    // Only the rows the grid shows: a recycled row it has set aside stays in the tree, hidden.
    private static IEnumerable<DataGridRow> Rows(DataGrid grid) =>
        grid.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.IsVisible);
}
