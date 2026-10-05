using System.Windows.Input;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>What a key on a list row does to the control marked with it (#543).</summary>
public enum RowKeyRole
{
    None,

    /// <summary>Enter or F2: the row's main action — edit, correct, transfer, open.</summary>
    Primary,

    /// <summary>Del: remove. The marked button asks first; a row without one ignores Del, as ETB rows do.</summary>
    Remove,

    /// <summary>Space: toggle, as an Aufgabe's done tick.</summary>
    Toggle,
}

/// <summary>
/// The row-key convention for grids and lists (#543, part of #545). <see cref="IsEnabledProperty"/>
/// goes on the list — a <see cref="DataGrid"/> or the narrow layout's card <see cref="ItemsControl"/>
/// — and <see cref="RoleProperty"/> on the controls inside a row. A key then presses the current
/// row's control with the matching role, exactly as a click would: through its automation peer, so
/// a button's flyout opens and its command runs, and a confirm the command asks for is asked.
///
/// The current row is the one holding focus, or in a grid that has focus itself, its selected row.
/// A row without the role, or whose control is hidden or disabled, leaves the key alone.
///
/// Nothing happens in a field that types (Del deletes a character there, Space types one), in a
/// picker, or inside an overlay; and Enter and Space on a focused button press that button, as
/// they always have. The handler is <b>tunneling</b>: a DataGrid takes Enter itself, to move to
/// the next row.
///
/// <see cref="UndoCommandProperty"/> is the matching Ctrl+Z, set once on the workspace. It too
/// leaves a text field alone, where Ctrl+Z undoes typing.
/// </summary>
public static class RowKeys
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(RowKeys));

    public static readonly AttachedProperty<RowKeyRole> RoleProperty =
        AvaloniaProperty.RegisterAttached<Control, RowKeyRole>("Role", typeof(RowKeys));

    public static readonly AttachedProperty<ICommand?> UndoCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("UndoCommand", typeof(RowKeys));

    static RowKeys()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((list, e) =>
        {
            list.RemoveHandler(InputElement.KeyDownEvent, OnListKeyDown);
            if (e.NewValue is true)
            {
                list.AddHandler(InputElement.KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
            }
        });

        UndoCommandProperty.Changed.AddClassHandler<Control>((root, e) =>
        {
            root.RemoveHandler(InputElement.KeyDownEvent, OnUndoKeyDown);
            if (e.NewValue is ICommand)
            {
                root.AddHandler(InputElement.KeyDownEvent, OnUndoKeyDown, RoutingStrategies.Tunnel);
            }
        });
    }

    public static void SetIsEnabled(Control target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    public static void SetRole(Control target, RowKeyRole value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(RoleProperty, value);
    }

    public static RowKeyRole GetRole(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(RoleProperty);
    }

    public static void SetUndoCommand(Control target, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(UndoCommandProperty, value);
    }

    public static ICommand? GetUndoCommand(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(UndoCommandProperty);
    }

    private static void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || sender is not Control list
            || e.Source is not Visual source || RoleOf(e.Key) is not { } role
            || ClaimsKey(list, source, e.Key) || CurrentRow(list, source) is not { } row
            || Marked(row, role) is not { } target)
        {
            return;
        }

        e.Handled = Press(target);
    }

    private static void OnUndoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Z || e.KeyModifiers != KeyModifiers.Control
            || sender is not Control root || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().Any(v => v is TextBox) || Overlay.IsInOverlay(source))
        {
            return;
        }

        var command = GetUndoCommand(root);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private static RowKeyRole? RoleOf(Key key) => key switch
    {
        Key.Enter or Key.F2 => RowKeyRole.Primary,
        Key.Delete => RowKeyRole.Remove,
        Key.Space => RowKeyRole.Toggle,
        _ => null,
    };

    // Walks from the focused control up to the list: a control that has its own use for the key keeps it.
    private static bool ClaimsKey(Control list, Visual source, Key key)
    {
        if (Overlay.IsInOverlay(source))
        {
            return true;
        }

        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (ReferenceEquals(visual, list))
            {
                return false;
            }

            switch (visual)
            {
                case TextBox:
                case AutoCompleteBox:
                case ComboBox:
                case NumericUpDown:
                case Button when key is Key.Enter or Key.Space: // a CheckBox is a Button too
                    return true;
            }
        }

        return true; // not inside this list at all
    }

    private static Control? CurrentRow(Control list, Visual source)
    {
        if (list is DataGrid grid)
        {
            return source.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault()
                ?? (grid.SelectedItem is { } selected
                    ? grid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, selected))
                    : null);
        }

        if (list is ItemsControl items)
        {
            return source.GetSelfAndVisualAncestors().OfType<Control>()
                .TakeWhile(c => !ReferenceEquals(c, list))
                .FirstOrDefault(c => items.IndexFromContainer(c) >= 0);
        }

        return null;
    }

    private static Control? Marked(Control row, RowKeyRole role) =>
        row.GetSelfAndVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => GetRole(c) == role && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);

    // Through the automation peer, not the command: a click is what opens a button's flyout, and
    // what a toggle's two-way binding listens to.
    private static bool Press(Control target)
    {
        switch (ControlAutomationPeer.CreatePeerForElement(target))
        {
            case IToggleProvider toggle:
                toggle.Toggle();
                return true;
            case IInvokeProvider invoke:
                invoke.Invoke();
                return true;
            default:
                return false;
        }
    }
}
