using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Strg+C on a <see cref="DataGrid"/> copies the cell that was clicked, and a right-click on a cell
/// offers „Kopieren“ for that cell and „Zeile kopieren“ for its whole row (#836).
///
/// The grid's own copy took the selected row, every value in quotes. Template columns, which have
/// no binding of their own to copy from, came out as <c>""</c>, so an Aufgabe or a Trupp copied as a
/// row of empty quotes. A column now copies what its <see cref="DataGridColumn.ClipboardContentBinding"/>
/// names. A text column falls back to its <c>Binding</c>, and a template column states one in XAML.
/// A column without one, such as a column of buttons, copies nothing on its own. Strg+C there copies
/// the row, and „Zeile kopieren“ leaves it out.
///
/// The cell is the one holding the key or the click. When focus is on the grid itself, as a click
/// leaves it, the cell is the grid's current cell in the selected row. A field in a cell keeps
/// Strg+C for its own selection.
///
/// A row is copied as its values separated by tabs, without quotes, so it pastes into a table as
/// one row. A line break or tab inside a value becomes a space there. A single cell is copied as it
/// stands.
/// </summary>
public static class GridCopy
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("IsEnabled", typeof(GridCopy));

    private static readonly ConditionalWeakTable<DataGrid, CopyMenu> Menus = new();

    static GridCopy()
    {
        IsEnabledProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            grid.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            grid.ContextRequested -= OnContextRequested;
            if (Menus.TryGetValue(grid, out var old))
            {
                if (ReferenceEquals(grid.ContextMenu, old.Menu))
                {
                    grid.ContextMenu = null;
                }

                Menus.Remove(grid);
            }

            if (e.NewValue is true)
            {
                // Tunnel: the grid answers Strg+C itself, with the whole row.
                grid.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

                // Before the menu is set: the menu opens from a ContextRequested handler of its own,
                // so this one has to run first to aim it at the cell.
                grid.ContextRequested += OnContextRequested;
                var menu = new CopyMenu(grid);
                Menus.Add(grid, menu);
                grid.ContextMenu = menu.Menu;
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

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not DataGrid grid || e.Source is TextBox || !IsCopyGesture(grid, e)
            || CellAt(grid, e.Source as Control) is not ({ } item, var column))
        {
            return;
        }

        e.Handled = true;
        Copy(grid, ValueOf(column, item) ?? RowText(grid, item));
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not DataGrid grid || !Menus.TryGetValue(grid, out var menu))
        {
            return;
        }

        // The header and the space below the last row have nothing to copy, so they open no menu.
        if (CellAt(grid, e.Source as Control) is not ({ } item, var column))
        {
            e.Handled = true;
            return;
        }

        menu.AimAt(ValueOf(column, item), RowText(grid, item));
    }

    private static (object? Item, DataGridColumn? Column) CellAt(DataGrid grid, Control? source) =>
        source?.FindAncestorOfType<DataGridRow>(includeSelf: true) is { DataContext: { } item }
            ? (item, DataGridColumn.GetColumnContainingElement(source))
            : (grid.SelectedItem, grid.CurrentColumn);

    // Null when the column names nothing to copy; an empty cell copies as "".
    private static string? ValueOf(DataGridColumn? column, object item)
    {
        if (column?.ClipboardContentBinding is not { } binding)
        {
            return null;
        }

        var probe = new TextBlock { DataContext = item };
        using (probe.Bind(TextBlock.TextProperty, binding))
        {
            return probe.Text ?? string.Empty;
        }
    }

    private static string RowText(DataGrid grid, object item) =>
        string.Join(
            '\t',
            grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex)
                .Select(c => ValueOf(c, item)).OfType<string>()
                .Select(value => value.ReplaceLineEndings(" ").Replace('\t', ' ')));

    // The platform's own copy gesture, the one TextBox answers to: Strg+C, or Cmd+C on macOS.
    private static bool IsCopyGesture(Visual visual, KeyEventArgs e) =>
        visual.GetPlatformSettings()?.HotkeyConfiguration.Copy is { Count: > 0 } gestures
            ? gestures.Any(g => g.Matches(e))
            : e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control;

    // Not awaited, as TextBox's own copy is not: a clipboard the platform refuses keeps what it
    // held, and the next paste shows that.
    private static void Copy(Visual visual, string text)
    {
        if (TopLevel.GetTopLevel(visual)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    private sealed class CopyMenu
    {
        private readonly MenuItem _cell = new() { Header = "Kopieren" };
        private readonly MenuItem _row = new() { Header = "Zeile kopieren" };
        private string _cellText = string.Empty;
        private string _rowText = string.Empty;

        public CopyMenu(DataGrid grid)
        {
            _cell.Click += (_, _) => Copy(grid, _cellText);
            _row.Click += (_, _) => Copy(grid, _rowText);
            Menu = new ContextMenu { Items = { _cell, _row } };
        }

        public ContextMenu Menu { get; }

        public void AimAt(string? cellText, string rowText)
        {
            _cell.IsEnabled = cellText is not null;
            _cellText = cellText ?? string.Empty;
            _rowText = rowText;
        }
    }
}
